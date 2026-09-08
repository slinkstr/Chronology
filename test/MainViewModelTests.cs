using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeStores;
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
        var store = new PlaintextDayStore(_tempDir);
        var stateStore = new AppStateStore(_tempDir);
        var history = new ActivityHistory(store, () => DateOnly.FromDateTime(_virtualNow));

        if (initialClock != default)
        {
            stateStore.Save(new AppState { Clock = initialClock });
        }

        return new MainViewModel(store, stateStore, history, () => _virtualNow);
    }

    private void AdvanceTime(TimeSpan delta) => _virtualNow += delta;

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
    public void Log_PersistsEntryToStore()
    {
        var store = new PlaintextDayStore(_tempDir);
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
    public void StatePersistence_ClockRestoredOnReopen()
    {
        var store = new PlaintextDayStore(_tempDir);
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

    // Activity navigation tests

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

    // ActivityHistory reads from day files

    [Fact]
    public void ActivityHistory_LoadsFromDayFiles()
    {
        var store = new PlaintextDayStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 3), [
            new TimeEntry(new TimeOnly(9, 0), "Old task", TimeSpan.FromMinutes(30)),
            new TimeEntry(new TimeOnly(10, 0), "Another task", TimeSpan.FromMinutes(15)),
        ]);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Recent task", TimeSpan.FromMinutes(45)),
        ]);

        var history = new ActivityHistory(store, () => new DateOnly(2026, 9, 4));

        // Most recent day first
        Assert.Equal("Recent task", history.Activities[0]);
        Assert.Equal("Old task", history.Activities[1]);
        Assert.Equal("Another task", history.Activities[2]);
    }

    [Fact]
    public void ActivityHistory_DeduplicatesAcrossDays()
    {
        var store = new PlaintextDayStore(_tempDir);
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
        var store = new PlaintextDayStore(_tempDir);
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
        var store = new PlaintextDayStore(_tempDir);
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

    // GetTimelogText

    [Fact]
    public void GetTimelogText_ReturnsFormattedEntries()
    {
        var store = new PlaintextDayStore(_tempDir);
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
        var vm = CreateViewModel();
        AdvanceTime(TimeSpan.FromMinutes(30));
        vm.Tick();

        vm.Activity = "Test task";
        vm.LogCommand.Execute(null);

        var text = vm.GetTimelogText();
        Assert.Contains("Test task", text);
        Assert.Contains("# Chronology log - 2026-09-04", text);
    }
}
