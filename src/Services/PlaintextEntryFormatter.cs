using System.Text;
using Chronology.Models;

namespace Chronology.Services;

public class PlaintextEntryFormatter : IEntryFormatter
{
    public string Format(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Chronology log - {date:yyyy-MM-dd}");
        foreach (var entry in entries)
        {
            sb.AppendLine($"{entry.Timestamp:HH:mm} | {entry.Activity,-15} | {FormatHMM(entry.Duration)}");
        }
        return sb.ToString();
    }

    internal static string FormatHMM(TimeSpan ts) => $"{(int)ts.TotalHours}:{ts.Minutes:D2}";
}
