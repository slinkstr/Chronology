namespace Chronology.Models;

public record TimeEntry(TimeOnly Timestamp, string Activity, TimeSpan Duration);
