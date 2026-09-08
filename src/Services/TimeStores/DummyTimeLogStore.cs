using Chronology.Models;

namespace Chronology.Services.TimeStores;

public class DummyTimeLogStore : ITimeLogStore
{
    public IEnumerable<TimeEntry> Load(DateOnly date) => [];
    public void Save(DateOnly date, IEnumerable<TimeEntry> entries) { }
    public IEnumerable<DateOnly> GetAvailableDates() => [];
}
