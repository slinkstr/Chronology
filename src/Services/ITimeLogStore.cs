using Chronology.Models;

namespace Chronology.Services;

public interface ITimeLogStore
{
    IEnumerable<TimeEntry> Load(DateOnly date);
    void Save(DateOnly date, IEnumerable<TimeEntry> entries);
    IEnumerable<DateOnly> GetAvailableDates();
}
