using System.Collections.ObjectModel;
using System.Diagnostics;
using Chronology.Models;
using Chronology.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chronology.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAlertSoundService _alertSound;
    private readonly Func<DateTime>     _now;
    private readonly List<(string Title, string Message)> _pendingErrors = [];
    private readonly AppStateStore      _stateStore;

    private int              _activityCursor = -1;
    private ActivityHistory? _activityHistory;
    private TimeSpan         _accumulatedClock;
    private DateOnly         _currentDate;
    private bool             _isNavigating;
    private bool             _isTimeFieldFocused;
    private DateTime         _lastInteraction;
    private DateTime         _lastKnownWriteTime;
    private DateTime         _lastStateSave;
    private DateTime         _lastTickAt;
    private ConfigReminder   _reminder;
    private ITimeLogStore?   _store;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogCommand))]
    private string _activity = "";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private string _timeExpression = "";

    [ObservableProperty]
    private string _totalDisplay = "0:00";

    public TimeSpan                      CurrentClock     => _accumulatedClock + (_now() - _lastTickAt);
    public List<TimeEntry>               Entries          { get; } = [];
    public bool                          HasFileBackend   => _store is IFileBackedStore;
    public ObservableCollection<string>  RecentActivities { get; } = [];

    public event Action<string, string>? ErrorOccurred;

    public MainViewModel()
        : this(Config.Load(), new AlertSoundService())
    {
    }

    internal MainViewModel(ITimeLogStore? store, AppStateStore stateStore, ActivityHistory? activityHistory, Func<DateTime> now,
        IAlertSoundService alertSound, ConfigReminder? reminder = null)
    {
        _alertSound      = alertSound;
        _store           = store;
        _stateStore      = stateStore;
        _reminder        = reminder ?? new ConfigReminder();
        _activityHistory = activityHistory ?? (store != null ? new ActivityHistory(store, () => DateOnly.FromDateTime(now())) : null);
        _now             = now;
        _currentDate     = DateOnly.FromDateTime(_now());
        _lastInteraction = _now();
        _lastTickAt      = _now();
        _lastStateSave   = _now();

        LoadDay();
        LoadState();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        RefreshNavigationCommands();
        InitializeFilePoll();
    }

    private MainViewModel(Config config, IAlertSoundService alertSound)
        : this(config.Store, new AppStateStore(Directory.GetCurrentDirectory()), null, () => DateTime.Now,
            alertSound, config.Reminder)
    {
    }

    public void FlushPendingErrors()
    {
        foreach (var (title, message) in _pendingErrors)
        {
            ErrorOccurred?.Invoke(title, message);
        }
        _pendingErrors.Clear();
    }

    public string GetTimelogText()
    {
        return (_store as IFileBackedStore)?.Contents(_currentDate) ?? "";
    }

    public void NotifyInteraction()
    {
        _lastInteraction = _now();
    }

    public void OnTimeFieldGotFocus()
    {
        _isTimeFieldFocused = true;
    }

    public void OnTimeFieldLostFocus()
    {
        _isTimeFieldFocused = false;
        RefreshTimeDisplay();
    }

    public void Tick()
    {
        CheckDayRollover();
        PollDayFile();
        CheckReminder();

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

    internal void SaveState()
    {
        var state = new AppState
        {
            Clock = CurrentClock,
            Activity = Activity,
        };
        try
        {
            _stateStore.Save(state);
        }
        catch (IOException ex)
        {
            RaiseError("File I/O error", $"Failed to save state: {ex.Message}");
        }
    }

    partial void OnActivityChanged(string value)
    {
        if (!_isNavigating)
        {
            _activityCursor = -1;
        }
        RefreshNavigationCommands();
    }

    private static string FormatHMM(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}";
    }

    private static TimeSpan Quantize(TimeSpan ts, TimeSpan precision)
    {
        if (precision == TimeSpan.Zero)
        {
            return ts;
        }
        return TimeSpan.FromTicks(ts.Ticks - ts.Ticks % precision.Ticks);
    }

    private bool CanLog() => !string.IsNullOrWhiteSpace(Activity);

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
            if (_store is null)
            {
                RaiseError("No store", "No time log store is configured.");
            }
            else
            {
                try
                {
                    _store.Save(_currentDate, Entries);
                    InitializeFilePoll();
                }
                catch (IOException ex)
                {
                    RaiseError("File I/O error", $"Failed to save previous day: {ex.Message}");
                }
            }
        }

        _accumulatedClock = TimeSpan.Zero;
        _lastTickAt = _now();
        _currentDate = today;

        LoadDay();
        _activityHistory?.Reload();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        _activityCursor = -1;
        RefreshNavigationCommands();
        SaveState();
        _lastStateSave = _now();
        InitializeFilePoll();
    }

    private void CheckReminder()
    {
        var now = _now();
        if (!_reminder.Enabled || now - _lastInteraction < _reminder.Interval)
        {
            return;
        }

        _lastInteraction = now;
        if (!_alertSound.Play(_reminder.SoundFile))
        {
            RaiseError("Reminder sound", "Failed to play the reminder sound.");
        }
    }

    private DateTime GetDayFileWriteTime()
    {
        if (_store is not IFileBackedStore fileStore)
        {
            return DateTime.MinValue;
        }
        var path = fileStore.GetFilePath(_currentDate);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private void InitializeFilePoll()
    {
        _lastKnownWriteTime = GetDayFileWriteTime();
    }

    private void LoadDay()
    {
        Entries.Clear();
        if (_store is null)
        {
            return;
        }
        try
        {
            foreach (var entry in _store.Load(_currentDate))
            {
                Entries.Add(entry);
            }
        }
        catch (TimeLogFormatException ex)
        {
            Entries.Clear();
            RaiseError("Malformed day file", ex.Message);
        }
        catch (IOException ex)
        {
            Entries.Clear();
            RaiseError("File I/O error", $"Failed to read day file: {ex.Message}");
        }
    }

    private void LoadState()
    {
        try
        {
            var state = _stateStore.Load();
            _accumulatedClock = state.Clock;
            Activity = state.Activity;
        }
        catch (IOException ex)
        {
            RaiseError("File I/O error", $"Failed to read state: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            RaiseError("Corrupt state file", $"Failed to parse state.json: {ex.Message}");
        }
    }

    private void PollDayFile()
    {
        if (_store is not IFileBackedStore)
        {
            return;
        }

        var current = GetDayFileWriteTime();
        if (current == _lastKnownWriteTime)
        {
            return;
        }

        _lastKnownWriteTime = current;
        LoadDay();
        UpdateTotalDisplay();
    }

    private void RaiseError(string title, string message)
    {
        if (ErrorOccurred != null)
        {
            ErrorOccurred.Invoke(title, message);
        }
        else
        {
            _pendingErrors.Add((title, message));
        }
    }

    private void RefreshNavigationCommands()
    {
        var count = _activityHistory?.Activities.Count ?? 0;
        CanGoBack = count > 0 && (_activityCursor == -1 || _activityCursor < count - 1);
        CanGoForward = _activityCursor >= 0;
    }

    private void RefreshRecentActivities()
    {
        RecentActivities.Clear();
        if (_activityHistory is null)
        {
            return;
        }
        foreach (var a in _activityHistory.Activities)
        {
            RecentActivities.Add(a);
        }
    }

    private void RefreshTimeDisplay()
    {
        TimeExpression = FormatHMM(CurrentClock);
    }

    private void SetActivityFromCursor(IReadOnlyList<string> history)
    {
        _isNavigating = true;
        Activity = history[_activityCursor];
        _isNavigating = false;
        RefreshNavigationCommands();
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

    [RelayCommand]
    private void EditConfig()
    {
        Process.Start(new ProcessStartInfo(Config.FilePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void GoBack()
    {
        var history = _activityHistory?.Activities;
        if (history is null || history.Count == 0)
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
        var history = _activityHistory?.Activities;
        if (history is null || _activityCursor == -1)
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

    [RelayCommand(CanExecute = nameof(CanLog))]
    private void Log()
    {
        Error = "";
        var elapsed = CurrentClock;
        TimeSpan valueToLog;

        if (string.IsNullOrWhiteSpace(TimeExpression))
        {
            valueToLog = Quantize(elapsed, _store?.Precision ?? TimeSpan.Zero);
        }
        else if (TimeExpression.Trim() == FormatHMM(elapsed))
        {
            valueToLog = Quantize(elapsed, _store?.Precision ?? TimeSpan.Zero);
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
        if (_store is null)
        {
            RaiseError("No store", "No time log store is configured.");
        }
        else
        {
            try
            {
                _store.Save(_currentDate, Entries);
                InitializeFilePoll();
            }
            catch (IOException ex)
            {
                RaiseError("File I/O error", $"Failed to save day file: {ex.Message}");
            }
        }

        _accumulatedClock = valueToLog >= elapsed ? TimeSpan.Zero : elapsed - valueToLog;
        _lastTickAt = _now();

        _activityHistory?.AddActivity(Activity.Trim());
        RefreshRecentActivities();

        Activity = "";
        _activityCursor = -1;
        RefreshNavigationCommands();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        SaveState();
    }

    [RelayCommand]
    private void OpenCurrentLogFile()
    {
        try
        {
            (_store as IFileBackedStore)?.OpenCurrentDayFile(_currentDate);
        }
        catch (Exception ex)
        {
            RaiseError("Open file error", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenTimelogDirectory()
    {
        try
        {
            (_store as IFileBackedStore)?.OpenDataDirectory();
        }
        catch (Exception ex)
        {
            RaiseError("Open directory error", ex.Message);
        }
    }

    [RelayCommand]
    private void Reload()
    {
        Config config;
        try
        {
            config = Config.Load();
        }
        catch (ConfigException ex)
        {
            RaiseError("Config error", $"Failed to reload config: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            RaiseError("Config error", $"Unexpected error reloading config: {ex.Message}");
            return;
        }

        _store = config.Store;
        _reminder = config.Reminder;
        _activityHistory = _store != null
            ? new ActivityHistory(_store, () => DateOnly.FromDateTime(_now()))
            : null;

        LoadDay();
        _activityHistory?.Reload();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        RefreshNavigationCommands();
        InitializeFilePoll();
        OnPropertyChanged(nameof(HasFileBackend));
    }

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
}
