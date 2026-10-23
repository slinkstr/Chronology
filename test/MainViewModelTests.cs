using System.Text.Json;
using Chronology.Models;
using Chronology.Services;
using Chronology.Services.Config;
using Chronology.Services.TimeLogStores;
using Chronology.ViewModels;
using Xunit;

namespace Chronology.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private DateTime _virtualNow;

    public MainViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChronologyTests_" + Guid.NewGuid());
        _virtualNow = new DateTime(2026, 9, 4, 9, 0, 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private MainViewModel CreateViewModel(TimeSpan initialClock = default, FakeAlertSoundService? alertSound = null, ConfigReminder? reminder = null, ITimeLogStore? store = null, ConfigWeeklySum? weeklySum = null)
    {
        var stateStore = new AppStateStore(_tempDir);
        var effectiveStore = store ?? new FakeTimeLogStore();
        var history = new ActivityHistory(effectiveStore, () => DateOnly.FromDateTime(_virtualNow));

        if (initialClock != default)
        {
            stateStore.Save(new AppState { Clock = initialClock, ClockDate = DateOnly.FromDateTime(_virtualNow) });
        }

        return new MainViewModel(effectiveStore, stateStore, history, () => _virtualNow, alertSound ?? new FakeAlertSoundService(), reminder, weeklySum);
    }

    private MainViewModel CreateViewModelWithRealStore(TimeSpan initialClock = default)
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        if (initialClock != default)
        {
            stateStore.Save(new AppState { Clock = initialClock, ClockDate = DateOnly.FromDateTime(_virtualNow) });
        }

        return new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
    }

    private void AdvanceTime(TimeSpan delta) => _virtualNow += delta;

    // Unit tests (no store)

    [Fact]
    public void Constructor_NoState_ClockStartsAtZero()
    {
        var vm = CreateViewModel();
        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
        Assert.Equal("0:00", vm.TimeExpression);
    }

    [Fact]
    public void Constructor_WithPersistedClock_RestoresClock()
    {
        var vm = CreateViewModel(initialClock: TimeSpan.FromMinutes(37));
        Assert.Equal(TimeSpan.FromMinutes(37), vm.CurrentClock);
        Assert.Equal("0:37", vm.TimeExpression);
    }

    [Fact]
    public void Constructor_PersistedClockFromPreviousDay_ResetsClock()
    {
        var stateStore = new AppStateStore(_tempDir);
        stateStore.Save(new AppState { Clock = TimeSpan.FromMinutes(30), ClockDate = new DateOnly(2026, 9, 3), Activity = "Old task" });

        var vm = CreateViewModel();

        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
        Assert.Equal("Old task", vm.Activity);
    }

    [Fact]
    public void Constructor_StateFileWithoutClockDate_ResetsClock()
    {
        Directory.CreateDirectory(_tempDir);
        var json = JsonSerializer.Serialize(new { ClockTicks = TimeSpan.FromMinutes(30).Ticks, Activity = "Old task" });
        File.WriteAllText(Path.Combine(_tempDir, "state.json"), json);

        var vm = CreateViewModel();

        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
    }

    [Fact]
    public void LogCommand_EmptyActivity_CannotExecute()
    {
        var vm = CreateViewModel();
        Assert.False(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void LogCommand_WithActivity_CanExecute()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.Tick();
        vm.Activity = "Fix bug";
        Assert.True(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void LogCommand_ZeroClock_CannotExecute()
    {
        var vm = CreateViewModel();
        vm.Activity = "Fix bug";
        Assert.False(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void LogCommand_ManualExpressionAtZeroClock_CanExecute()
    {
        var vm = CreateViewModel();
        vm.Activity = "Fix bug";
        vm.TimeExpression = "0:10";
        Assert.True(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void LogCommand_ClockAdvances_BecomesEnabled()
    {
        var vm = CreateViewModel();
        vm.Activity = "Fix bug";
        Assert.False(vm.LogCommand.CanExecute(null));

        AdvanceTime(TimeSpan.FromMinutes(1));
        vm.Tick();
        Assert.True(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void Log_PrefilledValue_LogsEntireElapsed_ResetsClock()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(37));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal("Fix bug", vm.Entries[0].Activity);
        Assert.Equal(TimeSpan.FromMinutes(37), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
    }

    [Fact]
    public void Log_EmptyExpression_QuantizesToWholeMinutes_KeepsRemainder()
    {
        var vm = CreateViewModelWithRealStore();
        AdvanceTime(TimeSpan.FromMinutes(37) + TimeSpan.FromSeconds(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Equal(TimeSpan.FromMinutes(37), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), vm.CurrentClock);
    }

    [Fact]
    public void Log_DisplayedExpression_QuantizesToWholeMinutes_KeepsRemainder()
    {
        var vm = CreateViewModelWithRealStore();
        AdvanceTime(TimeSpan.FromMinutes(37) + TimeSpan.FromSeconds(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "0:37";
        vm.LogCommand.Execute(null);

        Assert.Equal(TimeSpan.FromMinutes(37), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), vm.CurrentClock);
    }

    [Fact]
    public void Log_ExpressionLessThanElapsed_SubtractsFromClock()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(97));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "0:45";
        vm.LogCommand.Execute(null);

        Assert.Equal(TimeSpan.FromMinutes(45), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(52), vm.CurrentClock);
    }

    [Fact]
    public void Log_ExpressionGreaterThanElapsed_ResetsClock()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "1:00";
        vm.LogCommand.Execute(null);

        Assert.Equal(TimeSpan.FromMinutes(60), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
    }

    [Fact]
    public void Log_ExpressionWithArithmetic_ParsesAndLogs()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(97));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "1:37 - 0:12";
        vm.LogCommand.Execute(null);

        Assert.Equal(TimeSpan.FromMinutes(85), vm.Entries[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(12), vm.CurrentClock);
    }

    [Fact]
    public void Log_InvalidExpression_SetsError_NoCommit()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "abc";
        vm.LogCommand.Execute(null);

        Assert.NotEqual("", vm.Error);
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void Activity_InvalidChar_SetsError_DisablesLog()
    {
        var vm = CreateViewModelWithRealStore();
        vm.Activity = "a|b";

        Assert.NotEqual("", vm.Error);
        Assert.False(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void Activity_InvalidThenFixed_ClearsError()
    {
        var vm = CreateViewModelWithRealStore();
        vm.Activity = "a|b";
        Assert.NotEqual("", vm.Error);

        vm.Activity = "ab";
        Assert.Equal("", vm.Error);
    }

    [Fact]
    public void Log_InvalidActivity_NoCommit()
    {
        var vm = CreateViewModelWithRealStore();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "a|b";
        vm.LogCommand.Execute(null);

        Assert.NotEqual("", vm.Error);
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void Log_ClearsActivityAfterLog()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Equal("", vm.Activity);
    }

    [Fact]
    public void Reset_ZeroesClock()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(45));
        vm.Tick();

        vm.ResetCommand.Execute(null);

        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
        Assert.Equal("0:00", vm.TimeExpression);
    }

    [Fact]
    public void Log_AddsActivityToRecentActivities()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Contains("Fix bug", vm.RecentActivities);
    }

    [Fact]
    public void Tick_UpdatesTimeExpression()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.Tick();

        Assert.Equal("0:05", vm.TimeExpression);
    }

    [Fact]
    public void Tick_DoesNotUpdateWhenTimeFieldFocused()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.OnTimeFieldGotFocus();
        vm.TimeExpression = "1:37 - 0:12";
        AdvanceTime(TimeSpan.FromMinutes(2));
        vm.Tick();

        Assert.Equal("1:37 - 0:12", vm.TimeExpression);
    }

    [Fact]
    public void Tick_UpdatesTotalDisplay()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(37));
        vm.Tick();

        Assert.Equal("0:37", vm.TotalDisplay);
    }

    [Fact]
    public void Clock_AdvancesWithTime()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(10), vm.CurrentClock);

        AdvanceTime(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(15), vm.CurrentClock);
    }

    [Fact]
    public void OnTimeFieldLostFocus_KeepsEditUntilGraceExpires()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.OnTimeFieldGotFocus();
        vm.TimeExpression = "editing";
        AdvanceTime(TimeSpan.FromMinutes(3));
        vm.Tick();
        Assert.Equal("editing", vm.TimeExpression);

        vm.OnTimeFieldLostFocus();
        AdvanceTime(TimeSpan.FromSeconds(1));
        vm.Tick();
        Assert.Equal("editing", vm.TimeExpression);

        AdvanceTime(TimeSpan.FromSeconds(2));
        vm.Tick();
        Assert.Equal("0:08", vm.TimeExpression);
    }

    [Fact]
    public void Log_AfterTimeFieldBlur_UsesEditedValue()
    {
        var vm = CreateViewModel();
        vm.OnTimeFieldGotFocus();
        vm.TimeExpression = "0:10";
        vm.OnTimeFieldLostFocus();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(TimeSpan.FromMinutes(10), vm.Entries[0].Duration);
    }

    [Fact]
    public void Log_AfterBlurTickWithinGrace_UsesEditedValue()
    {
        var vm = CreateViewModel();
        vm.OnTimeFieldGotFocus();
        vm.TimeExpression = "0:10";
        vm.OnTimeFieldLostFocus();

        AdvanceTime(TimeSpan.FromSeconds(1));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal(TimeSpan.FromMinutes(10), vm.Entries[0].Duration);
    }

    [Fact]
    public void Log_ZeroClock_SetsError_NoCommit()
    {
        var vm = CreateViewModel();
        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        Assert.NotEqual("", vm.Error);
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void Log_ZeroExpression_SetsError_NoCommit()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(37));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.TimeExpression = "0:00";
        vm.LogCommand.Execute(null);

        Assert.NotEqual("", vm.Error);
        Assert.Empty(vm.Entries);
        Assert.Equal(TimeSpan.FromMinutes(37), vm.CurrentClock);
    }

    [Fact]
    public void GoBack_NoHistory_CannotGoBackOrForward()
    {
        var vm = CreateViewModel();
        Assert.False(vm.CanGoBack);
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void GoBack_WithHistory_SetsMostRecentActivity()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);
        AdvanceTime(TimeSpan.FromMinutes(10));
        vm.Tick();
        vm.Activity = "Second";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        Assert.Equal("Second", vm.Activity);
    }

    [Fact]
    public void GoBack_Twice_RecallsOlderActivity()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);
        AdvanceTime(TimeSpan.FromMinutes(10));
        vm.Tick();
        vm.Activity = "Second";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        Assert.Equal("Second", vm.Activity);
        vm.GoBackCommand.Execute(null);
        Assert.Equal("First", vm.Activity);
    }

    [Fact]
    public void GoForward_AfterBack_RecallsNewerActivity()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);
        AdvanceTime(TimeSpan.FromMinutes(10));
        vm.Tick();
        vm.Activity = "Second";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        vm.GoBackCommand.Execute(null);
        Assert.Equal("First", vm.Activity);

        vm.GoForwardCommand.Execute(null);
        Assert.Equal("Second", vm.Activity);
    }

    [Fact]
    public void GoForward_PastMostRecent_ClearsActivity()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        Assert.Equal("First", vm.Activity);

        vm.GoForwardCommand.Execute(null);
        Assert.Equal("", vm.Activity);
    }

    [Fact]
    public void GoBack_AtOldest_CannotGoBack()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);
        AdvanceTime(TimeSpan.FromMinutes(10));
        vm.Tick();
        vm.Activity = "Second";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        vm.GoBackCommand.Execute(null);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void TypingActivity_ResetsCursor()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "First";
        vm.LogCommand.Execute(null);

        vm.GoBackCommand.Execute(null);
        Assert.Equal("First", vm.Activity);

        vm.Activity = "New activity";
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void Tick_SameDay_NoRollover()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        Assert.Empty(vm.Entries);
        Assert.Equal(TimeSpan.FromMinutes(30), vm.CurrentClock);
    }

    [Fact]
    public void Tick_StartupWithPendingTime_DoesNotAlert()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(initialClock: TimeSpan.FromHours(2), alertSound: alertSound, reminder: reminder);

        vm.Tick();

        Assert.Empty(alertSound.PlayedFiles);
    }

    [Fact]
    public void Tick_IdleBelowReminderInterval_DoesNotAlert()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        vm.Tick();

        Assert.Empty(alertSound.PlayedFiles);
    }

    [Fact]
    public void Tick_IdleAtReminderInterval_PlaysSystemBeep()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.Tick();

        var playedFile = Assert.Single(alertSound.PlayedFiles);
        Assert.Null(playedFile);
    }

    [Fact]
    public void Tick_ConfiguredSoundFile_IsPlayed()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5, SoundFile = "/tmp/reminder.wav" };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();

        var playedFile = Assert.Single(alertSound.PlayedFiles);
        Assert.Equal("/tmp/reminder.wav", playedFile);
    }

    [Fact]
    public void Tick_ReminderDoesNotRepeatEveryTick()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        AdvanceTime(TimeSpan.FromSeconds(1));
        vm.Tick();

        Assert.Single(alertSound.PlayedFiles);
    }

    [Fact]
    public void Tick_ReminderRepeatsAfterNextInterval()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.Tick();

        Assert.Equal(2, alertSound.PlayedFiles.Count);
    }

    [Fact]
    public void Tick_ReminderDisabled_DoesNotAlert()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = false, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(10));
        vm.Tick();

        Assert.Empty(alertSound.PlayedFiles);
    }

    [Fact]
    public void Tick_ReminderSoundFailure_RaisesError()
    {
        var alertSound = new FakeAlertSoundService { Result = false };
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();

        Assert.Equal("Reminder sound", title);
    }

    [Fact]
    public void NotifyInteraction_ResetsReminderSchedule()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        vm.NotifyInteraction();

        AdvanceTime(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        vm.Tick();
        Assert.Single(alertSound.PlayedFiles);

        AdvanceTime(TimeSpan.FromSeconds(2));
        vm.Tick();
        Assert.Equal(2, alertSound.PlayedFiles.Count);
    }

    [Fact]
    public void Tick_PassesReminderCountToAlertSound()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5, Count = 3 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();

        var count = Assert.Single(alertSound.Counts);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Tick_ReminderRefire_CancelsPreviousPlay()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        var firstToken = alertSound.LastToken;

        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.Tick();

        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(alertSound.LastToken.IsCancellationRequested);
    }

    [Fact]
    public void NotifyInteraction_CancelsInFlightReminderPlay()
    {
        var alertSound = new FakeAlertSoundService();
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        vm.NotifyInteraction();

        Assert.True(alertSound.LastToken.IsCancellationRequested);
    }

    [Fact]
    public void Tick_ReminderSoundThrows_RaisesError()
    {
        var alertSound = new FakeAlertSoundService { Failure = new InvalidOperationException("boom") };
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        string? title = null;
        string? message = null;
        vm.ErrorOccurred += (t, m) => { title = t; message = m; };

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();

        Assert.Equal("Reminder sound", title);
        Assert.Equal("Failed to play the reminder sound: boom", message);
    }

    [Fact]
    public async Task Tick_ReminderSoundFailureAfterSuspension_RaisesError()
    {
        var pending = new TaskCompletionSource<bool>();
        var alertSound = new FakeAlertSoundService { PendingResult = pending };
        var reminder = new ConfigReminder { Enabled = true, IntervalMinutes = 5 };
        var vm = CreateViewModel(alertSound: alertSound, reminder: reminder);

        var errorRaised = new TaskCompletionSource<string>();
        vm.ErrorOccurred += (t, _) => errorRaised.TrySetResult(t);

        AdvanceTime(TimeSpan.FromMinutes(6));
        vm.Tick();
        Assert.False(errorRaised.Task.IsCompleted);

        pending.SetResult(false);

        var title = await errorRaised.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Reminder sound", title);
    }

    // Integration tests (PlaintextTimeLogStore)

    [Fact]
    public void Log_PersistsEntryToStore()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        var loaded = store.Load(DateOnly.FromDateTime(_virtualNow)).ToList();
        Assert.Single(loaded);
        Assert.Equal("Fix bug", loaded[0].Activity);
    }

    [Fact]
    public void StatePersistence_ClockRestoredOnReopen()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm1 = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm1.Tick();
        vm1.SaveState();

        var vm2 = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        Assert.Equal(TimeSpan.FromMinutes(30), vm2.CurrentClock);
    }

    [Fact]
    public void StatePersistence_ClockResetOnReopenNextDay()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm1 = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm1.Tick();
        vm1.SaveState();

        AdvanceTime(TimeSpan.FromDays(1));

        var vm2 = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        Assert.Equal(TimeSpan.Zero, vm2.CurrentClock);
    }

    [Fact]
    public void ActivityHistory_LoadsFromDayFiles()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 3), [
            new TimeEntry(new TimeOnly(9, 0), "Old task", TimeSpan.FromMinutes(30)),
            new TimeEntry(new TimeOnly(10, 0), "Another task", TimeSpan.FromMinutes(15)),
        ]);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Recent task", TimeSpan.FromMinutes(45)),
        ]);

        var history = new ActivityHistory(store, () => new DateOnly(2026, 9, 4));

        Assert.Equal("Recent task", history.Activities[0]);
        Assert.Equal("Old task", history.Activities[1]);
        Assert.Equal("Another task", history.Activities[2]);
    }

    [Fact]
    public void ActivityHistory_DeduplicatesAcrossDays()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 3), [
            new TimeEntry(new TimeOnly(9, 0), "Same task", TimeSpan.FromMinutes(30)),
        ]);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Same task", TimeSpan.FromMinutes(45)),
        ]);

        var history = new ActivityHistory(store, () => new DateOnly(2026, 9, 4));

        Assert.Single(history.Activities);
        Assert.Equal("Same task", history.Activities[0]);
    }

    [Fact]
    public void ActivityHistory_CapsAt50()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var entries = new List<TimeEntry>();
        for (var i = 0; i < 60; i++)
        {
            entries.Add(new TimeEntry(new TimeOnly(9, i % 60), $"Task {i}", TimeSpan.FromMinutes(1)));
        }
        store.Save(new DateOnly(2026, 9, 4), entries);

        var history = new ActivityHistory(store, () => new DateOnly(2026, 9, 4));

        Assert.Equal(50, history.Activities.Count);
    }

    [Fact]
    public void ActivityHistory_IgnoresFutureDates()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 5), [
            new TimeEntry(new TimeOnly(9, 0), "Future task", TimeSpan.FromMinutes(30)),
        ]);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Today task", TimeSpan.FromMinutes(45)),
        ]);

        var history = new ActivityHistory(store, () => new DateOnly(2026, 9, 4));

        Assert.DoesNotContain("Future task", history.Activities);
        Assert.Contains("Today task", history.Activities);
    }

    [Fact]
    public void GetTimelogText_ReturnsFormattedEntries()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Fix bug", TimeSpan.FromMinutes(30)),
        ]);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());

        var text = vm.GetTimelogText();
        Assert.Contains("Fix bug", text);
        Assert.Contains("# Chronology log - 2026-09-04", text);
    }

    [Fact]
    public void GetTimelogText_AfterLog_ReturnsFormattedEntries()
    {
        var vm = CreateViewModelWithRealStore();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Test task";
        vm.LogCommand.Execute(null);

        var text = vm.GetTimelogText();
        Assert.Contains("Test task", text);
        Assert.Contains("# Chronology log - 2026-09-04", text);
    }

    [Fact]
    public void Tick_DayRollover_FlushesPendingClockToPreviousDay()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(90));
        vm.Tick();

        var loaded = store.Load(new DateOnly(2026, 9, 4)).ToList();
        var rollover = Assert.Single(loaded);
        Assert.Equal("(unattributed)", rollover.Activity);
        Assert.Equal(TimeSpan.FromMinutes(90), rollover.Duration);
    }

    [Fact]
    public void Tick_DayRollover_UsesTypedActivityName()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        vm.Activity = "Working late";
        AdvanceTime(TimeSpan.FromMinutes(60));
        vm.Tick();

        var loaded = store.Load(new DateOnly(2026, 9, 4)).ToList();
        var rollover = Assert.Single(loaded);
        Assert.Equal("Working late", rollover.Activity);
    }

    [Fact]
    public void Tick_DayRollover_ResetsClock()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(90));
        vm.Tick();

        Assert.Equal(TimeSpan.Zero, vm.CurrentClock);
    }

    [Fact]
    public void Tick_DayRollover_LoadsNewDayEntries()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 5), [
            new TimeEntry(new TimeOnly(9, 0), "New day task", TimeSpan.FromMinutes(30)),
        ]);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(90));
        vm.Tick();

        Assert.Single(vm.Entries);
        Assert.Equal("New day task", vm.Entries[0].Activity);
    }

    [Fact]
    public void Tick_DayRollover_ZeroClock_NoEntryAdded()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        _virtualNow = new DateTime(2026, 9, 5, 0, 0, 0);
        vm.ResetCommand.Execute(null);
        vm.Tick();

        Assert.Empty(store.Load(new DateOnly(2026, 9, 4)).ToList());
    }

    [Fact]
    public void Tick_DayRollover_ReloadsActivityHistory()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Old task", TimeSpan.FromMinutes(30)),
        ]);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 23, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        Assert.Contains("Old task", vm.RecentActivities);

        AdvanceTime(TimeSpan.FromMinutes(90));
        vm.Tick();

        Assert.Contains("(unattributed)", vm.RecentActivities);
    }

    [Fact]
    public void Constructor_MalformedDayFile_QueuesErrorAndContinues()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-04.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-04",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| XX:00 | Fix bug | 0:48     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        _virtualNow = new DateTime(2026, 9, 4, 10, 0, 0);

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());

        string? errorTitle = null;
        string? errorMessage = null;
        vm.ErrorOccurred += (t, m) => { errorTitle = t; errorMessage = m; };
        vm.FlushPendingErrors();

        Assert.NotNull(errorTitle);
        Assert.Contains("Malformed", errorTitle);
        Assert.Contains("line 5", errorMessage);
    }

    [Fact]
    public void Constructor_CorruptStateFile_QueuesErrorAndContinues()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "state.json"), "{ invalid json }");

        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());

        string? errorTitle = null;
        vm.ErrorOccurred += (t, _) => { errorTitle = t; };
        vm.FlushPendingErrors();

        Assert.NotNull(errorTitle);
        Assert.Contains("Corrupt", errorTitle);
    }

    [Fact]
    public void ErrorOccurred_RaisedImmediately_WhenSubscribedBeforeError()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        _virtualNow = new DateTime(2026, 9, 4, 10, 0, 0);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());

        string? errorTitle = null;
        vm.ErrorOccurred += (t, _) => { errorTitle = t; };

        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-05.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-05",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| XX:00 | Fix bug | 0:48     |",
        ]);

        AdvanceTime(TimeSpan.FromDays(1));
        vm.Tick();

        Assert.NotNull(errorTitle);
        Assert.Contains("Malformed", errorTitle);
    }

    [Fact]
    public void Tick_ExternalFileChange_ReloadsEntries()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        _virtualNow = new DateTime(2026, 9, 4, 10, 0, 0);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Original", TimeSpan.FromMinutes(30)),
        ]);

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        Assert.Single(vm.Entries);
        Assert.Equal("Original", vm.Entries[0].Activity);

        Thread.Sleep(20);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Edited", TimeSpan.FromMinutes(45)),
        ]);

        vm.Tick();

        Assert.Single(vm.Entries);
        Assert.Equal("Edited", vm.Entries[0].Activity);
    }

    [Fact]
    public void Tick_OwnSaveDoesNotTriggerReload()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        _virtualNow = new DateTime(2026, 9, 4, 10, 0, 0);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Original", TimeSpan.FromMinutes(30)),
        ]);

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        AdvanceTime(TimeSpan.FromMinutes(15));
        vm.Tick();

        vm.Activity = "New task";
        vm.LogCommand.Execute(null);

        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("New task", vm.Entries[1].Activity);

        vm.Tick();

        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void Tick_NoFileChange_DoesNotReload()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        _virtualNow = new DateTime(2026, 9, 4, 10, 0, 0);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Original", TimeSpan.FromMinutes(30)),
        ]);

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var entriesBefore = vm.Entries.Count;

        vm.Tick();

        Assert.Equal(entriesBefore, vm.Entries.Count);
    }

    // Special day marking

    [Fact]
    public async Task MarkSpecialDay_EmptyDay_SavesEntryWithoutConfirmation()
    {
        var store = new FakeTimeLogStore();
        var vm = CreateViewModel(store: store);
        var date = new DateOnly(2026, 9, 5);
        var confirmCalls = 0;

        var marked = await vm.MarkSpecialDayAsync(date, "*HOLIDAY", TimeSpan.FromHours(8), _ => { confirmCalls++; return Task.FromResult(true); });

        Assert.True(marked);
        Assert.Equal(0, confirmCalls);
        var entry = Assert.Single(store.Load(date));
        Assert.Equal("*HOLIDAY", entry.Activity);
        Assert.Equal(new TimeOnly(0, 0), entry.Timestamp);
        Assert.Equal(TimeSpan.FromHours(8), entry.Duration);
    }

    [Fact]
    public async Task MarkSpecialDay_ExistingDay_Confirmed_Appends()
    {
        var store = new FakeTimeLogStore();
        var date = new DateOnly(2026, 9, 5);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30))]);
        var vm = CreateViewModel(store: store);
        var confirmCalls = 0;

        var marked = await vm.MarkSpecialDayAsync(date, "*VACATION", TimeSpan.FromHours(8), _ => { confirmCalls++; return Task.FromResult(true); });

        Assert.True(marked);
        Assert.Equal(1, confirmCalls);
        var entries = store.Load(date).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Work", entries[0].Activity);
        Assert.Equal("*VACATION", entries[1].Activity);
    }

    [Fact]
    public async Task MarkSpecialDay_ExistingDay_Declined_LeavesDayUntouched()
    {
        var store = new FakeTimeLogStore();
        var date = new DateOnly(2026, 9, 5);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30))]);
        var vm = CreateViewModel(store: store);

        var marked = await vm.MarkSpecialDayAsync(date, "*VACATION", TimeSpan.FromHours(8), _ => Task.FromResult(false));

        Assert.False(marked);
        Assert.Single(store.Load(date));
    }

    [Fact]
    public async Task MarkSpecialDay_ExistingDay_PassesEntryCountToConfirmation()
    {
        var store = new FakeTimeLogStore();
        var date = new DateOnly(2026, 9, 5);
        store.Save(date, [
            new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30)),
            new TimeEntry(new TimeOnly(10, 0), "More work", TimeSpan.FromMinutes(15)),
        ]);
        var vm = CreateViewModel(store: store);
        var askedCount = -1;

        await vm.MarkSpecialDayAsync(date, "*VACATION", TimeSpan.FromHours(8), count => { askedCount = count; return Task.FromResult(false); });

        Assert.Equal(2, askedCount);
    }

    [Fact]
    public async Task MarkSpecialDay_InvalidActivity_RaisesError_DoesNotSave()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        var confirmCalls = 0;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "a|b", TimeSpan.FromHours(8), _ => { confirmCalls++; return Task.FromResult(true); });

        Assert.False(marked);
        Assert.Equal("Invalid activity", title);
        Assert.Equal(0, confirmCalls);
        Assert.False(File.Exists(store.GetFilePath(date)));
    }

    [Fact]
    public async Task MarkSpecialDay_WhitespaceActivity_RaisesError_DoesNotSave()
    {
        var store = new FakeTimeLogStore();
        var vm = CreateViewModel(store: store);
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "   ", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.False(marked);
        Assert.Equal("Invalid activity", title);
        Assert.Empty(store.Load(date));
    }

    [Fact]
    public async Task MarkSpecialDay_ZeroDuration_RaisesError_DoesNotSave()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*SICK", TimeSpan.Zero, _ => Task.FromResult(true));

        Assert.False(marked);
        Assert.Equal("Invalid duration", title);
        Assert.False(File.Exists(store.GetFilePath(date)));
    }

    [Fact]
    public async Task MarkSpecialDay_MalformedFile_RaisesError_DoesNotClobber()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        Directory.CreateDirectory(_tempDir);
        var date = new DateOnly(2026, 9, 3);
        var path = store.GetFilePath(date);
        var original = "| XX:00 | Fix bug | 0:48     |\n";
        File.WriteAllText(path, original);

        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        string? title = null;
        var confirmCalls = 0;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*SICK", TimeSpan.FromHours(8), _ => { confirmCalls++; return Task.FromResult(true); });

        Assert.False(marked);
        Assert.Equal("Malformed day file", title);
        Assert.Equal(0, confirmCalls);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task MarkSpecialDay_CurrentDay_ReflectedImmediately()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var today = DateOnly.FromDateTime(_virtualNow);

        var marked = await vm.MarkSpecialDayAsync(today, "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.True(marked);
        var entry = Assert.Single(vm.Entries);
        Assert.Equal("*HOLIDAY", entry.Activity);
        Assert.Equal("8:00", vm.TotalDisplay);
    }

    [Fact]
    public async Task MarkSpecialDay_CurrentDayWithEntries_Confirmed_AppendsAndRefreshes()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var today = DateOnly.FromDateTime(_virtualNow);
        store.Save(today, [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30))]);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var confirmCalls = 0;

        var marked = await vm.MarkSpecialDayAsync(today, "*HOLIDAY", TimeSpan.FromHours(8), _ => { confirmCalls++; return Task.FromResult(true); });

        Assert.True(marked);
        Assert.Equal(1, confirmCalls);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("Work", vm.Entries[0].Activity);
        Assert.Equal("*HOLIDAY", vm.Entries[1].Activity);
        Assert.Equal("8:30", vm.TotalDisplay);
    }

    [Fact]
    public async Task MarkSpecialDay_TodayThenLog_BothEntriesPersist()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));
        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow, new FakeAlertSoundService());
        var today = DateOnly.FromDateTime(_virtualNow);

        var marked = await vm.MarkSpecialDayAsync(today, "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));
        Assert.True(marked);

        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();
        vm.Activity = "Fix bug";
        vm.LogCommand.Execute(null);

        var loaded = store.Load(today).ToList();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("*HOLIDAY", loaded[0].Activity);
        Assert.Equal("Fix bug", loaded[1].Activity);
    }

    [Fact]
    public async Task MarkSpecialDay_AddsNameToRecentActivities()
    {
        var store = new FakeTimeLogStore();
        var vm = CreateViewModel(store: store);

        await vm.MarkSpecialDayAsync(new DateOnly(2026, 9, 5), "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.Contains("*HOLIDAY", vm.RecentActivities);
    }

    [Fact]
    public async Task MarkSpecialDay_FileMutatedDuringConfirmation_AppendsToFreshContents()
    {
        var store = new FakeTimeLogStore();
        var date = new DateOnly(2026, 9, 5);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30))]);
        var vm = CreateViewModel(store: store);

        var marked = await vm.MarkSpecialDayAsync(date, "*VACATION", TimeSpan.FromHours(8), _ =>
        {
            store.Save(date, store.Load(date).Append(new TimeEntry(new TimeOnly(10, 0), "Late work", TimeSpan.FromHours(1))));
            return Task.FromResult(true);
        });

        Assert.True(marked);
        Assert.Equal(["Work", "Late work", "*VACATION"], store.Load(date).Select(e => e.Activity).ToList());
    }

    [Fact]
    public async Task MarkSpecialDay_FileBecomesMalformedDuringConfirmation_RaisesError_DoesNotSave()
    {
        var store = new FakeTimeLogStore();
        var date = new DateOnly(2026, 9, 5);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromMinutes(30))]);
        var vm = CreateViewModel(store: store);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*VACATION", TimeSpan.FromHours(8), _ =>
        {
            store.LoadError = new TimeLogFormatException("day.txt", 1, "bad line");
            return Task.FromResult(true);
        });

        Assert.False(marked);
        Assert.Equal("Malformed day file", title);
        store.LoadError = null;
        Assert.Equal(["Work"], store.Load(date).Select(e => e.Activity).ToList());
    }

    [Fact]
    public async Task MarkSpecialDay_LoadIoError_RaisesError()
    {
        var store = new FakeTimeLogStore { LoadError = new IOException("locked") };
        var vm = CreateViewModel(store: store);
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.False(marked);
        Assert.Equal("File I/O error", title);
    }

    [Fact]
    public async Task MarkSpecialDay_SaveIoError_RaisesError()
    {
        var store = new FakeTimeLogStore { SaveError = new IOException("locked") };
        var vm = CreateViewModel(store: store);
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.False(marked);
        Assert.Equal("File I/O error", title);
        Assert.Empty(store.Load(date));
    }

    [Fact]
    public async Task MarkSpecialDay_InvalidOperationOnSave_RaisesInvalidDuration()
    {
        var store = new FakeTimeLogStore { SaveError = new InvalidOperationException("too wide") };
        var vm = CreateViewModel(store: store);
        var date = new DateOnly(2026, 9, 5);
        string? title = null;
        vm.ErrorOccurred += (t, _) => title = t;

        var marked = await vm.MarkSpecialDayAsync(date, "*HOLIDAY", TimeSpan.FromHours(8), _ => Task.FromResult(true));

        Assert.False(marked);
        Assert.Equal("Invalid duration", title);
        Assert.Empty(store.Load(date));
    }

    // Weekly sum
    // _virtualNow is Friday 2026-09-04; the default Sunday-start week is 2026-08-30 to 2026-09-05.

    [Fact]
    public void WeeklySum_DisabledByDefault_NotVisible()
    {
        var vm = CreateViewModel();
        Assert.False(vm.WeeklySumVisible);
    }

    [Fact]
    public void WeeklySum_Enabled_DisplaysSumAndGoal()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 4), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(8))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true });
        Assert.True(vm.WeeklySumVisible);
        Assert.Equal("8:00 / 40:00", vm.WeeklySumDisplay);
        Assert.False(vm.WeeklySumGoalMet);
    }

    [Fact]
    public void WeeklySum_IncludesRunningClock()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 4), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(8))]);
        var vm = CreateViewModel(initialClock: TimeSpan.FromHours(2), store: store, weeklySum: new ConfigWeeklySum { Enabled = true });
        Assert.Equal("10:00 / 40:00", vm.WeeklySumDisplay);
    }

    [Fact]
    public void WeeklySum_GoalMet_WhenSumReachesGoal()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 3), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(40))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true });
        Assert.Equal("40:00 / 40:00", vm.WeeklySumDisplay);
        Assert.True(vm.WeeklySumGoalMet);
    }

    [Fact]
    public void WeeklySum_NoGoal_DisplaysBareTotal()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 4), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(41))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true, HoursPerWeek = 0 });
        Assert.Equal("41:00", vm.WeeklySumDisplay);
        Assert.False(vm.WeeklySumGoalMet);
    }

    [Fact]
    public void WeeklySum_HiddenGoal_StillMarksGoalMet()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 4), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(8))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true, HoursPerWeek = 8, ShowGoal = false });
        Assert.Equal("8:00", vm.WeeklySumDisplay);
        Assert.True(vm.WeeklySumGoalMet);
    }

    [Fact]
    public void WeeklySum_ExcludesDaysBeforeWeekStart()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 8, 30), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(8))]);
        store.Save(new DateOnly(2026, 8, 31), [new TimeEntry(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(4))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true, WeekStart = DayOfWeek.Monday });
        Assert.Equal("4:00 / 40:00", vm.WeeklySumDisplay);
    }

    [Fact]
    public void WeeklySum_IncludesFutureMarksInWeek()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 5), [new TimeEntry(new TimeOnly(9, 0), "*HOLIDAY", TimeSpan.FromHours(8))]);
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true });
        Assert.Equal("8:00 / 40:00", vm.WeeklySumDisplay);
    }

    [Fact]
    public void WeeklySum_LoadError_RaisesErrorOncePerFile()
    {
        var store = new FakeTimeLogStore { LoadError = new TimeLogFormatException("day.txt", 1, "bad line") };
        var errors = 0;
        var vm = CreateViewModel(store: store, weeklySum: new ConfigWeeklySum { Enabled = true });
        vm.ErrorOccurred += (_, _) => errors++;
        vm.FlushPendingErrors();
        Assert.Equal(8, errors);
        vm.Tick();
        vm.Tick();
        Assert.Equal(8, errors);
    }
}
