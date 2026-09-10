using Chronology.Models;

namespace Chronology.Services;

public class ActivityHistory
{
    public const int MaxActivities = 50;

    private readonly ITimeLogStore  _store;
    private readonly Func<DateOnly> _today;
    private readonly List<string>   _activities = [];

    public ActivityHistory(ITimeLogStore store, Func<DateOnly> today)
    {
        _store = store;
        _today = today;
        Load();
    }

    public IReadOnlyList<string> Activities => _activities;

    public void Reload()
    {
        Load();
    }

    public void AddActivity(string activity)
    {
        var trimmed = activity.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }
        _activities.Remove(trimmed);
        _activities.Insert(0, trimmed);
        while (_activities.Count > MaxActivities)
        {
            _activities.RemoveAt(_activities.Count - 1);
        }
    }

    private void Load()
    {
        _activities.Clear();
        var today = _today();
        IEnumerable<DateOnly> dates;
        try
        {
            dates = _store.GetAvailableDates()
                .Where(d => d <= today)
                .OrderByDescending(d => d)
                .ToList();
        }
        catch (IOException)
        {
            return;
        }

        foreach (var date in dates)
        {
            IEnumerable<TimeEntry> entries;
            try
            {
                entries = _store.Load(date).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (TimeLogFormatException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var name = entry.Activity.Trim();
                if (!string.IsNullOrEmpty(name) && !_activities.Contains(name))
                {
                    _activities.Add(name);
                    if (_activities.Count >= MaxActivities)
                    {
                        return;
                    }
                }
            }
        }
    }
}
