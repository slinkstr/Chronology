using System.Collections.ObjectModel;
using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeLogStores;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chronology.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITimeLogStore   _store;
    private readonly AppStateStore   _stateStore;
    private readonly ActivityHistory _activityHistory;
    private readonly Func<DateTime>  _now;

    private TimeSpan _accumulatedClock;
    private DateTime _lastTickAt;
    private DateTime _lastStateSave;
    private DateOnly _currentDate;
    private bool _isTimeFieldFocused;
    private bool _isNavigating;
    private int _activityCursor = -1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogCommand))]
    private string _activity = "";

    [ObservableProperty]
    private string _timeExpression = "";

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private string _totalDisplay = "0:00";

    public List<TimeEntry> Entries { get; } = [];

    public ObservableCollection<string> RecentActivities { get; } = [];

    public MainViewModel()
        : this(GetDefaultDataDirectory())
    {
    }

    public MainViewModel(string dataDirectory)
        : this(
            new PlaintextTimeLogStore(dataDirectory),
            new AppStateStore(dataDirectory),
            () => DateTime.Now)
    {
    }

    public MainViewModel(ITimeLogStore store, AppStateStore stateStore, Func<DateTime> now)
        : this(store, stateStore, new ActivityHistory(store, () => DateOnly.FromDateTime(now())), now)
    {
    }

    public MainViewModel(ITimeLogStore store, AppStateStore stateStore, ActivityHistory activityHistory, Func<DateTime> now)
    {
        _store           = store;
        _stateStore      = stateStore;
        _activityHistory = activityHistory;
        _now             = now;
        _currentDate     = DateOnly.FromDateTime(_now());
        _lastTickAt      = _now();
        _lastStateSave   = _now();

        LoadDay();
        LoadState();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        RefreshNavigationCommands();
    }

    partial void OnActivityChanged(string value)
    {
        if (!_isNavigating)
        {
            _activityCursor = -1;
        }
        RefreshNavigationCommands();
    }

    private static string GetDefaultDataDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "timelog");

    public TimeSpan CurrentClock => _accumulatedClock + (_now() - _lastTickAt);

    public void Tick()
    {
        CheckDayRollover();

        if (!_isTimeFieldFocused)
        {
            RefreshTimeDisplay();
        }
        UpdateTotalDisplay();

        if (_now() - _lastStateSave >= TimeSpan.FromMinutes(1))
        {
            SaveState();
            _lastStateSave = _now();
        }
    }

    private void CheckDayRollover()
    {
        var today = DateOnly.FromDateTime(_now());
        if (today <= _currentDate)
        {
            return;
        }

        var elapsed = CurrentClock;
        if (elapsed > TimeSpan.Zero)
        {
            var activity = string.IsNullOrWhiteSpace(Activity) ? "(unattributed)" : Activity.Trim();
            Entries.Add(new TimeEntry(TimeOnly.FromDateTime(_now()), activity, elapsed));
            _store.Save(_currentDate, Entries);
        }

        _accumulatedClock = TimeSpan.Zero;
        _lastTickAt = _now();
        _currentDate = today;

        LoadDay();
        _activityHistory.Reload();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        _activityCursor = -1;
        RefreshNavigationCommands();
        SaveState();
        _lastStateSave = _now();
    }

    public void OnTimeFieldGotFocus() => _isTimeFieldFocused = true;

    public void OnTimeFieldLostFocus()
    {
        _isTimeFieldFocused = false;
        RefreshTimeDisplay();
    }

    [RelayCommand(CanExecute = nameof(CanLog))]
    private void Log()
    {
        Error = "";
        var elapsed = CurrentClock;
        TimeSpan valueToLog;

        if (string.IsNullOrWhiteSpace(TimeExpression))
        {
            valueToLog = Quantize(elapsed, _store.Precision);
        }
        else if (TimeExpression.Trim() == FormatHMM(elapsed))
        {
            valueToLog = Quantize(elapsed, _store.Precision);
        }
        else
        {
            if (!TimeExpressionParser.TryParse(TimeExpression, out var parsed))
            {
                Error = "Invalid time expression";
                return;
            }
            valueToLog = parsed;
        }

        var entry = new TimeEntry(TimeOnly.FromDateTime(_now()), Activity.Trim(), valueToLog);
        Entries.Add(entry);
        _store.Save(_currentDate, Entries);

        _accumulatedClock = valueToLog >= elapsed ? TimeSpan.Zero : elapsed - valueToLog;
        _lastTickAt = _now();

        _activityHistory.AddActivity(Activity.Trim());
        RefreshRecentActivities();

        Activity = "";
        _activityCursor = -1;
        RefreshNavigationCommands();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        SaveState();
    }

    private bool CanLog() => !string.IsNullOrWhiteSpace(Activity);

    [RelayCommand]
    private void Reset()
    {
        _accumulatedClock = TimeSpan.Zero;
        _lastTickAt = _now();
        Error = "";
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        SaveState();
    }

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [RelayCommand]
    private void GoBack()
    {
        var history = _activityHistory.Activities;
        if (history.Count == 0)
        {
            return;
        }

        if (_activityCursor == -1)
        {
            _activityCursor = 0;
        }
        else if (_activityCursor < history.Count - 1)
        {
            _activityCursor++;
        }

        SetActivityFromCursor(history);
    }

    [RelayCommand]
    private void GoForward()
    {
        var history = _activityHistory.Activities;
        if (_activityCursor == -1)
        {
            return;
        }

        if (_activityCursor > 0)
        {
            _activityCursor--;
            SetActivityFromCursor(history);
        }
        else
        {
            _activityCursor = -1;
            _isNavigating = true;
            Activity = "";
            _isNavigating = false;
        }
        RefreshNavigationCommands();
    }

    public bool HasFileBackend => _store is IFileBackedStore;

    [RelayCommand]
    private void OpenCurrentLogFile()
    {
        (_store as IFileBackedStore)?.OpenCurrentDayFile(_currentDate);
    }

    [RelayCommand]
    private void OpenTimelogDirectory()
    {
        (_store as IFileBackedStore)?.OpenDataDirectory();
    }

    public string GetTimelogText() => (_store as IFileBackedStore)?.Contents(_currentDate) ?? "";

    private void SetActivityFromCursor(IReadOnlyList<string> history)
    {
        _isNavigating = true;
        Activity = history[_activityCursor];
        _isNavigating = false;
        RefreshNavigationCommands();
    }

    private void RefreshNavigationCommands()
    {
        var count = _activityHistory.Activities.Count;
        CanGoBack = count > 0 && (_activityCursor == -1 || _activityCursor < count - 1);
        CanGoForward = _activityCursor >= 0;
    }

    private void LoadDay()
    {
        Entries.Clear();
        foreach (var entry in _store.Load(_currentDate))
        {
            Entries.Add(entry);
        }
    }

    private void LoadState()
    {
        var state = _stateStore.Load();
        _accumulatedClock = state.Clock;
        Activity = state.Activity;
    }

    private void RefreshRecentActivities()
    {
        RecentActivities.Clear();
        foreach (var a in _activityHistory.Activities)
        {
            RecentActivities.Add(a);
        }
    }

    private void RefreshTimeDisplay()
    {
        TimeExpression = FormatHMM(CurrentClock);
    }

    private void UpdateTotalDisplay()
    {
        var logged = TimeSpan.Zero;
        foreach (var e in Entries)
        {
            logged += e.Duration;
        }
        var total = logged + CurrentClock;
        TotalDisplay = $"{(int)total.TotalHours}:{total.Minutes:D2}";
    }

    internal void SaveState()
    {
        var state = new AppState
        {
            Clock = CurrentClock,
            Activity = Activity,
        };
        _stateStore.Save(state);
    }

    private static string FormatHMM(TimeSpan ts) => $"{(int)ts.TotalHours}:{ts.Minutes:D2}";

    private static TimeSpan Quantize(TimeSpan ts, TimeSpan precision)
    {
        if (precision == TimeSpan.Zero)
        {
            return ts;
        }
        return TimeSpan.FromTicks(ts.Ticks - ts.Ticks % precision.Ticks);
    }
}
