using System.Diagnostics;
using System.Globalization;
using System.Text;
using Chronology.Models;

namespace Chronology.Services.TimeLogStores;

public class LegacyTimeLogStore : IFileBackedStore
{
    public string DataDirectory { get; }

    private const int    TimeWidth     = 5;
    private const int    ProjectWidth  = 20;
    private const int    ActivityWidth = 70;
    private const string Gap           = "  ";
    private const string Gap2          = "   ";
    private const int    IndentWidth   = 30;
    private const string FilePrefix    = "TimeLog.";

    public LegacyTimeLogStore(string dataDirectory = "./timelog")
    {
        DataDirectory = dataDirectory;
    }

    public TimeSpan Precision => TimeSpan.FromHours(0.01);

    public IEnumerable<TimeEntry> Load(DateOnly date)
    {
        var path = GetFilePath(date);
        if (!File.Exists(path))
        {
            yield break;
        }

        var lines = File.ReadAllLines(path);
        string? currentActivity = null;
        TimeSpan? currentDuration = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('-'))
            {
                continue;
            }

            if (line.StartsWith("Time ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length >= TimeWidth && TryParseTimeField(line[..TimeWidth], out var hours))
            {
                if (currentDuration.HasValue && currentActivity != null)
                {
                    yield return new TimeEntry(new TimeOnly(0, 0), currentActivity, currentDuration.Value);
                }

                currentDuration = null;
                currentActivity = null;

                if (line.Length >= 27 && line[7..27].Trim() == "*TOTAL")
                {
                    continue;
                }

                currentDuration = TimeSpan.FromHours(hours);
                currentActivity = line.Length > IndentWidth ? line[IndentWidth..].TrimEnd() : "";
            }
            else if (currentActivity != null && line.Length > IndentWidth)
            {
                currentActivity += line[IndentWidth..].TrimEnd();
            }
        }

        if (currentDuration.HasValue && currentActivity != null)
        {
            yield return new TimeEntry(new TimeOnly(0, 0), currentActivity, currentDuration.Value);
        }
    }

    public void Save(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = GetFilePath(date);
        File.WriteAllText(path, Format(date, entries));
    }

    public IEnumerable<DateOnly> GetAvailableDates()
    {
        if (!Directory.Exists(DataDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(DataDirectory, $"{FilePrefix}*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            var datePart = name[FilePrefix.Length..];
            if (DateOnly.TryParseExact(datePart, "yyyyMMdd", out var date))
            {
                yield return date;
            }
        }
    }

    public string Contents(DateOnly date)
    {
        var path = GetFilePath(date);
        return File.Exists(path) ? File.ReadAllText(path) : Format(date, []);
    }

    public string GetFilePath(DateOnly date)
    {
        return Path.Combine(DataDirectory, $"{FilePrefix}{date:yyyyMMdd}.txt");
    }

    public void OpenCurrentDayFile(DateOnly date)
    {
        var path = GetFilePath(date);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
        }
        else
        {
            OpenDataDirectory();
        }
    }

    public void OpenDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Process.Start(new ProcessStartInfo(Path.GetFullPath(DataDirectory)) { UseShellExecute = true });
    }

    internal static string Format(DateOnly date, IEnumerable<TimeEntry> entries)
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
            var chunk = TakeChunk(remaining, ActivityWidth);
            if (first)
            {
                sb.AppendLine($"{prefix}{chunk}");
                first = false;
            }
            else
            {
                sb.AppendLine($"{indent}{chunk}");
            }
            remaining = remaining[chunk.Length..].TrimStart();
        }
        while (remaining.Length > 0);

        if (first)
        {
            sb.AppendLine(prefix);
        }
    }

    private static string TakeChunk(string text, int width)
    {
        if (text.Length <= width)
        {
            return text;
        }

        var breakAt = text.LastIndexOf(' ', width - 1, width);
        return breakAt > 0 ? text[..breakAt] : text[..width];
    }

    private static bool TryParseTimeField(string field, out double hours)
    {
        hours = 0;
        var trimmed = field.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }
        return double.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out hours);
    }
}
