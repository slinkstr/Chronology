using Chronology.Models;

namespace Chronology.Services.TimeLogStores;

public class DummyTimeLogStore : ITimeLogStore
{
    public IEnumerable<TimeEntry> Load(DateOnly date) => [];
    public void                   Save(DateOnly date, IEnumerable<TimeEntry> entries) { }
    public IEnumerable<DateOnly>  GetAvailableDates() => [];
}
