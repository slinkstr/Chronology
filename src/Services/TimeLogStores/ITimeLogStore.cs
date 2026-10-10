using Chronology.Models;

namespace Chronology.Services.TimeLogStores;

public interface ITimeLogStore
{
    TimeSpan               Precision { get; }
    IEnumerable<DateOnly>  GetAvailableDates();
    IEnumerable<TimeEntry> Load(DateOnly date);
    void                   Save(DateOnly date, IEnumerable<TimeEntry> entries);
    string?                Validate(string activity);
}
