using Chronology.Models;

namespace Chronology.Services;

public interface IEntryFormatter
{
    string Format(DateOnly date, IEnumerable<TimeEntry> entries);
}
