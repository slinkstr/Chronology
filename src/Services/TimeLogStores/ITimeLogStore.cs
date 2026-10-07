using Chronology.Models;

namespace Chronology.Services.TimeLogStores;

public interface ITimeLogStore
{
    TimeSpan               Precision { get; }
    IEnumerable<TimeEntry> Load(DateOnly date);
    void                   Save(DateOnly date, IEnumerable<TimeEntry> entries);
    IEnumerable<DateOnly>  GetAvailableDates();
}
