using System.Collections.ObjectModel;
using System.Diagnostics;
using Chronology.Models;
using Chronology.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chronology.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITimeLogStore _store;
    private readonly AppStateStore _stateStore;
    private readonly ActivityHistory _activityHistory;
    private readonly IEntryFormatter _formatter;
    private readonly Func<DateTime> _now;
    private readonly string _dataDirectory;

    private TimeSpan _accumulatedClock;
    private DateTime _lastTickAt;
    private DateTime _lastStateSave;
    private DateOnly _currentDate;
    private bool _isTimeFieldFocused;
    private int _activityCursor = -1;
    private bool _isNavigating;

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
            new PlaintextDayStore(dataDirectory),
            new AppStateStore(dataDirectory),
            new PlaintextEntryFormatter(),
            () => DateTime.Now,
            dataDirectory)
    {
    }

    public MainViewModel(ITimeLogStore store, AppStateStore stateStore, IEntryFormatter formatter, Func<DateTime> now, string dataDirectory)
        : this(store, stateStore, new ActivityHistory(store, () => DateOnly.FromDateTime(now())), formatter, now, dataDirectory)
    {
    }

    public MainViewModel(ITimeLogStore store, AppStateStore stateStore, ActivityHistory activityHistory, IEntryFormatter formatter, Func<DateTime> now, string dataDirectory)
    {
        _store = store;
        _stateStore = stateStore;
        _activityHistory = activityHistory;
        _formatter = formatter;
        _now = now;
        _dataDirectory = dataDirectory;
        _currentDate = DateOnly.FromDateTime(_now());
        _lastTickAt = _now();
        _lastStateSave = _now();

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
            valueToLog = elapsed;
        }
        else if (TimeExpression.Trim() == FormatHMM(elapsed))
        {
            valueToLog = elapsed;
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

    [RelayCommand(CanExecute = nameof(CanGoBack))]
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

    [RelayCommand(CanExecute = nameof(CanGoForward))]
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

    [RelayCommand]
    private void OpenCurrentLogFile()
    {
        var path = Path.Combine(_dataDirectory, $"{_currentDate:yyyy-MM-dd}.txt");
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            OpenTimelogDirectory();
        }
    }

    [RelayCommand]
    private void OpenTimelogDirectory()
    {
        Directory.CreateDirectory(_dataDirectory);
        Process.Start(new ProcessStartInfo(_dataDirectory) { UseShellExecute = true });
    }

    public string GetTimelogText() => _formatter.Format(_currentDate, Entries);

    private void SetActivityFromCursor(IReadOnlyList<string> history)
    {
        _isNavigating = true;
        Activity = history[_activityCursor];
        _isNavigating = false;
        RefreshNavigationCommands();
    }

    private bool CanGoBack()
    {
        var count = _activityHistory.Activities.Count;
        return count > 0 && (_activityCursor == -1 || _activityCursor < count - 1);
    }

    private bool CanGoForward() => _activityCursor >= 0;

    private void RefreshNavigationCommands()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
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
}
