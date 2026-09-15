using Chronology.Models;
using Chronology.Services;
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

    private MainViewModel CreateViewModel(TimeSpan initialClock = default)
    {
        var store = new DummyTimeLogStore();
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        if (initialClock != default)
        {
            stateStore.Save(new AppState { Clock = initialClock });
        }

        return new MainViewModel(store, stateStore, history, () => _virtualNow);
    }

    private MainViewModel CreateViewModelWithRealStore(TimeSpan initialClock = default)
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        if (initialClock != default)
        {
            stateStore.Save(new AppState { Clock = initialClock });
        }

        return new MainViewModel(store, stateStore, history, () => _virtualNow);
    }

    private void AdvanceTime(TimeSpan delta) => _virtualNow += delta;

    // Unit tests (DummyTimeLogStore)

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
    public void LogCommand_EmptyActivity_CannotExecute()
    {
        var vm = CreateViewModel();
        Assert.False(vm.LogCommand.CanExecute(null));
    }

    [Fact]
    public void LogCommand_WithActivity_CanExecute()
    {
        var vm = CreateViewModel();
        vm.Activity = "Fix bug";
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
    public void OnTimeFieldLostFocus_ResumesTicking()
    {
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(5));
        vm.OnTimeFieldGotFocus();
        vm.TimeExpression = "editing";
        AdvanceTime(TimeSpan.FromMinutes(3));
        vm.Tick();
        Assert.Equal("editing", vm.TimeExpression);

        vm.OnTimeFieldLostFocus();
        Assert.Equal("0:08", vm.TimeExpression);
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

    // Integration tests (PlaintextTimeLogStore)

    [Fact]
    public void Log_PersistsEntryToStore()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm1 = new MainViewModel(store, stateStore, history, () => _virtualNow);
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm1.Tick();
        vm1.SaveState();

        var vm2 = new MainViewModel(store, stateStore, history, () => _virtualNow);
        Assert.Equal(TimeSpan.FromMinutes(30), vm2.CurrentClock);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);

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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);

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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);

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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);

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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
        AdvanceTime(TimeSpan.FromMinutes(15));

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

        var vm = new MainViewModel(store, stateStore, history, () => _virtualNow);
        var entriesBefore = vm.Entries.Count;

        vm.Tick();

        Assert.Equal(entriesBefore, vm.Entries.Count);
    }
}
