using System.Text;
using Chronology.Models;

namespace Chronology.Services;

public class LegacyEntryFormatter : IEntryFormatter
{
    private const int TimeWidth = 5;
    private const int ProjectWidth = 20;
    private const int ActivityWidth = 70;
    private const string Gap = "  ";
    private const string Gap2 = "   ";
    private const int IndentWidth = 30;

    public string Format(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        var sb = new StringBuilder();
        var entryList = entries.ToList();

        sb.AppendLine($"{"Time",-TimeWidth}{Gap}{"Project",-ProjectWidth}{Gap2}Activity Description ({date:yyyy-MM-dd})");
        AppendSeparator(sb);

        var total = TimeSpan.Zero;
        foreach (var entry in entryList)
        {
            total += entry.Duration;
            AppendDataRow(sb, entry.Duration, "[GENERAL]", entry.Activity);
        }

        AppendSeparator(sb);
        AppendDataRow(sb, total, "*TOTAL", "Total time spent on work related activities.");
        AppendSeparator(sb);

        return sb.ToString();
    }

    private static void AppendSeparator(StringBuilder sb)
    {
        sb.AppendLine($"{new string('-', TimeWidth)}{Gap}{new string('-', ProjectWidth)}{Gap2}{new string('-', ActivityWidth)}");
    }

    private static void AppendDataRow(StringBuilder sb, TimeSpan duration, string project, string activity)
    {
        var timeField = duration.TotalHours.ToString("F2").PadLeft(TimeWidth);
        var projectField = project.PadRight(ProjectWidth);
        var prefix = $"{timeField}{Gap}{projectField}{Gap2}";
        var indent = new string(' ', IndentWidth);

        var remaining = activity;
        var first = true;
        do
        {
            var chunk = remaining.Length <= ActivityWidth ? remaining : remaining[..ActivityWidth];
            if (first)
            {
                sb.AppendLine($"{prefix}{chunk}");
                first = false;
            }
            else
            {
                sb.AppendLine($"{indent}{chunk}");
            }
            remaining = remaining.Length <= ActivityWidth ? "" : remaining[ActivityWidth..];
        }
        while (remaining.Length > 0);

        if (first)
        {
            sb.AppendLine(prefix);
        }
    }
}
