using Chronology.Models;
using Chronology.Services.TimeLogStores;

namespace Chronology.Tests;

internal sealed class FakeTimeLogStore : ITimeLogStore
{
    public TimeSpan Precision => TimeSpan.Zero;

    public IEnumerable<DateOnly> GetAvailableDates()
    {
        return [];
    }

    public IEnumerable<TimeEntry> Load(DateOnly date)
    {
        return [];
    }

    public void Save(DateOnly date, IEnumerable<TimeEntry> entries)
    {
    }
}
