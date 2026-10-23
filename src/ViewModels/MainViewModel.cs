using System.Collections.ObjectModel;
using System.Diagnostics;
using Chronology.Models;
using Chronology.Services;
using Chronology.Services.Config;
using Chronology.Services.TimeLogStores;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chronology.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAlertSoundService _alertSound;
    private readonly Func<DateTime>     _now;
    private readonly List<(string Title, string Message)> _pendingErrors = [];
    private readonly AppStateStore      _stateStore;

    private int                      _activityCursor = -1;
    private ActivityHistory?         _activityHistory;
    private TimeSpan                 _accumulatedClock;
    private DateOnly                 _currentDate;
    private bool                     _isNavigating;
    private bool                     _isTimeFieldFocused;
    private DateTime                 _lastInteraction;
    private DateTime                 _lastKnownWriteTime;
    private DateTime                 _lastStateSave;
    private DateTime                 _lastTickAt;
    private ConfigReminder           _reminder;
    private CancellationTokenSource? _reminderSoundCts;
    private ITimeLogStore            _store;
    private DateTime?                _timeFieldBlurredAt;
    private WeeklySum                _weeklySum;
    private ConfigWeeklySum          _weeklySumConfig;

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
    [NotifyCanExecuteChangedFor(nameof(LogCommand))]
    private string _timeExpression = "";

    [ObservableProperty]
    private string _totalDisplay = "0:00";

    [ObservableProperty]
    private string _weeklySumDisplay = "0:00";

    [ObservableProperty]
    private bool _weeklySumGoalMet;

    [ObservableProperty]
    private bool _weeklySumVisible;

    public TimeSpan                      CurrentClock     => _accumulatedClock + (_now() - _lastTickAt);
    public List<TimeEntry>               Entries          { get; } = [];
    public bool                          HasFileBackend   => _store is IFileBackedStore;
    public ObservableCollection<string>  RecentActivities { get; } = [];
    public DateOnly                      Today            => _currentDate;

    public event Action<string, string>? ErrorOccurred;

    public MainViewModel()
        : this(Config.Load(), new AlertSoundService())
    {
    }

    internal MainViewModel(ITimeLogStore store, AppStateStore stateStore, ActivityHistory? activityHistory, Func<DateTime> now, IAlertSoundService alertSound, ConfigReminder? reminder = null, ConfigWeeklySum? weeklySum = null)
    {
        _alertSound      = alertSound;
        _store           = store;
        _stateStore      = stateStore;
        _reminder        = reminder ?? new ConfigReminder();
        _weeklySumConfig = weeklySum ?? new ConfigWeeklySum();
        _activityHistory = activityHistory ?? new ActivityHistory(store, () => DateOnly.FromDateTime(now()));
        _now             = now;
        _currentDate     = DateOnly.FromDateTime(_now());
        _lastInteraction = _now();
        _lastTickAt      = _now();
        _lastStateSave   = _now();
        _weeklySum       = new WeeklySum(store, () => DateOnly.FromDateTime(now()), _weeklySumConfig.WeekStart);

        WeeklySumVisible = _weeklySumConfig.Enabled;

        LoadDay();
        LoadState();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        UpdateWeeklySum();
        RefreshNavigationCommands();
        InitializeFilePoll();
    }

    private MainViewModel(Config config, IAlertSoundService alertSound)
        : this(config.Store, new AppStateStore(Directory.GetCurrentDirectory()), null, () => DateTime.Now,
            alertSound, config.Reminder, config.WeeklySum)
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

    public async Task<bool> MarkSpecialDayAsync(DateOnly date, string activity, TimeSpan duration, Func<int, Task<bool>> confirmAppend)
    {
        var name = activity.Trim();
        if (string.IsNullOrEmpty(name))
        {
            RaiseError("Invalid activity", "Activity cannot be empty.");
            return false;
        }
        if (_store.Validate(name) is { } validationError)
        {
            RaiseError("Invalid activity", validationError);
            return false;
        }
        if (duration <= TimeSpan.Zero)
        {
            RaiseError("Invalid duration", "Duration must be greater than zero.");
            return false;
        }

        if (!TryLoadDayEntries(date, out var existing))
        {
            return false;
        }
        if (existing.Count > 0 && !await confirmAppend(existing.Count))
        {
            return false;
        }

        // The file may have changed while the confirmation dialog was open
        if (!TryLoadDayEntries(date, out existing))
        {
            return false;
        }
        existing.Add(new TimeEntry(TimeOnly.MinValue, name, duration));
        if (!TrySaveDay(date, existing, "Failed to save day file"))
        {
            return false;
        }

        if (date == _currentDate)
        {
            LoadDay();
            UpdateTotalDisplay();
            InitializeFilePoll();
        }
        _activityHistory?.AddActivity(name);
        RefreshRecentActivities();
        return true;
    }

    public void NotifyClosing()
    {
        _reminderSoundCts?.Cancel();
    }

    public void NotifyInteraction()
    {
        _lastInteraction = _now();
        _reminderSoundCts?.Cancel();
    }

    public void OnTimeFieldGotFocus()
    {
        _isTimeFieldFocused = true;
        _timeFieldBlurredAt = null;
    }

    public void OnTimeFieldLostFocus()
    {
        _isTimeFieldFocused = false;
        _timeFieldBlurredAt = _now();
    }

    public void Tick()
    {
        CheckDayRollover();
        PollDayFile();
        CheckReminder();

        var inBlurGrace = _timeFieldBlurredAt is DateTime blurredAt && _now() - blurredAt < TimeSpan.FromSeconds(2);
        if (!_isTimeFieldFocused && !inBlurGrace)
        {
            RefreshTimeDisplay();
        }
        UpdateTotalDisplay();
        UpdateWeeklySum();
        LogCommand.NotifyCanExecuteChanged();

        if (_now() - _lastStateSave >= TimeSpan.FromMinutes(1))
        {
            SaveState();
            _lastStateSave = _now();
        }
    }

    public string? ValidateActivity(string activity)
    {
        return _store.Validate(activity);
    }

    internal void SaveState()
    {
        var state = new AppState
        {
            Clock = CurrentClock,
            ClockDate = _currentDate,
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
        Error = _store.Validate(value) ?? "";
        RefreshNavigationCommands();
    }

    private static TimeSpan Quantize(TimeSpan ts, TimeSpan precision)
    {
        if (precision == TimeSpan.Zero)
        {
            return ts;
        }
        return TimeSpan.FromTicks(ts.Ticks - ts.Ticks % precision.Ticks);
    }

    private bool CanLog()
    {
        if (string.IsNullOrWhiteSpace(Activity) || _store.Validate(Activity) is not null)
        {
            return false;
        }
        var resolved = ResolveLogValue(CurrentClock);
        return resolved is null || resolved > TimeSpan.Zero;
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
            if (TrySaveDay(_currentDate, Entries, "Failed to save previous day"))
            {
                InitializeFilePoll();
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
        _reminderSoundCts?.Cancel();
        _reminderSoundCts = new CancellationTokenSource();
        _ = PlayReminderSoundAsync(_reminderSoundCts.Token);
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
        if (TryLoadDayEntries(_currentDate, out var entries))
        {
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
    }

    private void LoadState()
    {
        try
        {
            var state = _stateStore.Load();
            _accumulatedClock = state.ClockDate == _currentDate ? state.Clock : TimeSpan.Zero;
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

    private async Task PlayReminderSoundAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _alertSound.PlayAsync(_reminder.SoundFile, _reminder.Count, cancellationToken))
            {
                RaiseError("Reminder sound", "Failed to play the reminder sound.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RaiseError("Reminder sound", $"Failed to play the reminder sound: {ex.Message}");
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
        TimeExpression = DurationText.Format(CurrentClock, DurationFormat.Hhmm);
    }

    private TimeSpan? ResolveLogValue(TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(TimeExpression) || TimeExpression.Trim() == DurationText.Format(elapsed, DurationFormat.Hhmm))
        {
            return Quantize(elapsed, _store.Precision);
        }
        return TimeExpressionParser.TryParse(TimeExpression, out var parsed) ? parsed : null;
    }

    private void SetActivityFromCursor(IReadOnlyList<string> history)
    {
        _isNavigating = true;
        Activity = history[_activityCursor];
        _isNavigating = false;
        RefreshNavigationCommands();
    }

    private bool TryLoadDayEntries(DateOnly date, out List<TimeEntry> entries)
    {
        try
        {
            entries = _store.Load(date).ToList();
            return true;
        }
        catch (TimeLogFormatException ex)
        {
            RaiseError("Malformed day file", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RaiseError("File I/O error", $"Failed to read day file: {ex.Message}");
        }
        entries = [];
        return false;
    }

    private bool TrySaveDay(DateOnly date, IEnumerable<TimeEntry> entries, string ioError)
    {
        try
        {
            _store.Save(date, entries);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RaiseError("File I/O error", $"{ioError}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            RaiseError("Invalid duration", ex.Message);
        }
        return false;
    }

    private void UpdateTotalDisplay()
    {
        var logged = TimeSpan.Zero;
        foreach (var e in Entries)
        {
            logged += e.Duration;
        }
        var total = logged + CurrentClock;
        TotalDisplay = DurationText.Format(total, DurationFormat.Hhmm);
    }

    private void UpdateWeeklySum()
    {
        if (!_weeklySumConfig.Enabled)
        {
            return;
        }
        var goal = TimeSpan.FromHours(_weeklySumConfig.GoalHours);
        var sum = _weeklySum.Compute(OnWeeklySumLoadError) + CurrentClock;
        WeeklySumDisplay = _weeklySumConfig.GoalShow
            ? $"{DurationText.Format(sum, DurationFormat.Hhmm)} / {DurationText.Format(goal, DurationFormat.Hhmm)}"
            : DurationText.Format(sum, DurationFormat.Hhmm);
        WeeklySumGoalMet = _weeklySumConfig.GoalHighlightShow && sum >= goal;
    }

    private void OnWeeklySumLoadError(Exception ex)
    {
        if (ex is TimeLogFormatException)
        {
            RaiseError("Malformed day file", ex.Message);
        }
        else
        {
            RaiseError("File I/O error", $"Failed to read day file: {ex.Message}");
        }
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
        var validationError = _store.Validate(Activity);
        if (validationError is not null)
        {
            Error = validationError;
            return;
        }

        var elapsed = CurrentClock;
        var resolved = ResolveLogValue(elapsed);
        if (resolved is null)
        {
            Error = "Invalid time expression";
            return;
        }
        var valueToLog = resolved.Value;

        if (valueToLog <= TimeSpan.Zero)
        {
            Error = "Duration must be greater than zero.";
            return;
        }

        var entry = new TimeEntry(TimeOnly.FromDateTime(_now()), Activity.Trim(), valueToLog);
        Entries.Add(entry);
        if (TrySaveDay(_currentDate, Entries, "Failed to save day file"))
        {
            InitializeFilePoll();
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
        _weeklySumConfig = config.WeeklySum;
        _activityHistory = new ActivityHistory(_store, () => DateOnly.FromDateTime(_now()));
        _weeklySum = new WeeklySum(_store, () => DateOnly.FromDateTime(_now()), _weeklySumConfig.WeekStart);

        WeeklySumVisible = _weeklySumConfig.Enabled;

        LoadDay();
        _activityHistory?.Reload();
        RefreshRecentActivities();
        RefreshTimeDisplay();
        UpdateTotalDisplay();
        UpdateWeeklySum();
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
