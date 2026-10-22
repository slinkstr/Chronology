using Chronology.Models;
using Chronology.Services.TimeLogStores;

namespace Chronology.Tests;

internal sealed class FakeTimeLogStore : ITimeLogStore
{
    private readonly Dictionary<DateOnly, List<TimeEntry>> _days = [];

    public Exception? LoadError { get; set; }
    public TimeSpan   Precision => TimeSpan.Zero;
    public Exception? SaveError { get; set; }

    public IEnumerable<DateOnly> GetAvailableDates()
    {
        return _days.Keys;
    }

    public IEnumerable<TimeEntry> Load(DateOnly date)
    {
        if (LoadError is not null)
        {
            throw LoadError;
        }
        return _days.TryGetValue(date, out var entries) ? entries.ToList() : [];
    }

    public void Save(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        if (SaveError is not null)
        {
            throw SaveError;
        }
        _days[date] = entries.ToList();
    }

    public string? Validate(string activity) => null;
}
