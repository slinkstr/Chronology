using System.Diagnostics;
using System.Globalization;
using System.Text;
using Chronology.Models;

namespace Chronology.Services.TimeLogStores;

public class PlaintextTimeLogStore : IFileBackedStore
{
    private const int TimeWidth     = 5;
    private const int ActivityWidth = 80;
    private const int DurationWidth = 8;

    private readonly string _dataDirectory;

    public TimeSpan Precision => TimeSpan.FromMinutes(1);

    public PlaintextTimeLogStore(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public IEnumerable<TimeEntry> Load(DateOnly date)
    {
        var path = GetFilePath(date);
        if (!File.Exists(path))
        {
            yield break;
        }

        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|') || trimmed.Length < 2)
            {
                continue;
            }

            var parts = trimmed[1..^1].Split('|', 3);
            if (parts.Length != 3)
            {
                throw new TimeLogFormatException(path, lineNumber, "expected 3 columns");
            }

            var timestampStr = parts[0].Trim();
            var activity     = parts[1].Trim();
            var durationStr  = parts[2].Trim();

            if (IsSeparator(parts))
            {
                continue;
            }
            if (IsHeader(timestampStr))
            {
                continue;
            }

            if (!TimeOnly.TryParse(timestampStr, CultureInfo.InvariantCulture, out var timestamp))
            {
                throw new TimeLogFormatException(path, lineNumber, $"invalid timestamp '{timestampStr}'");
            }
            if (!TryParseHMM(durationStr, out var duration))
            {
                throw new TimeLogFormatException(path, lineNumber, $"invalid duration '{durationStr}'");
            }

            yield return new TimeEntry(timestamp, activity, duration);
        }
    }

    private static bool IsHeader(string firstCell) =>
        firstCell.Equals("Time", StringComparison.OrdinalIgnoreCase);

    private static bool IsSeparator(string[] cells) =>
        cells.All(c => c.Trim().All(ch => ch == '-'));

    public void Save(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = GetFilePath(date);
        File.WriteAllText(path, Format(date, entries));
    }

    private string GetFilePath(DateOnly date) => Path.Combine(_dataDirectory, $"{date:yyyy-MM-dd}.txt");

    public IEnumerable<DateOnly> GetAvailableDates()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_dataDirectory, "*.txt"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var date))
            {
                yield return date;
            }
        }
    }

    public void OpenCurrentDayFile(DateOnly date)
    {
        var path = GetFilePath(date);
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            OpenDataDirectory();
        }
    }

    public void OpenDataDirectory()
    {
        Directory.CreateDirectory(_dataDirectory);
        Process.Start(new ProcessStartInfo(_dataDirectory) { UseShellExecute = true });
    }

    public string Contents(DateOnly date)
    {
        var path = GetFilePath(date);
        return File.Exists(path) ? File.ReadAllText(path) : Format(date, []);
    }

    internal string Format(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Chronology log - {date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"| {"Time",-TimeWidth} | {"Activity",-ActivityWidth} | {"Duration",-DurationWidth} |");
        sb.AppendLine($"| {new string('-', TimeWidth)} | {new string('-', ActivityWidth)} | {new string('-', DurationWidth)} |");
        foreach (var entry in entries)
        {
            sb.AppendLine($"| {entry.Timestamp:HH:mm} | {entry.Activity,-ActivityWidth} | {FormatHMM(entry.Duration),-DurationWidth} |");
        }
        return sb.ToString();
    }

    private static string FormatHMM(TimeSpan ts) => $"{(int)ts.TotalHours}:{ts.Minutes:D2}";

    internal static bool TryParseHMM(string text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var span = text.AsSpan().Trim();
        var colon = span.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        if (!int.TryParse(span[..colon], out var hours))
        {
            return false;
        }
        if (!int.TryParse(span[(colon + 1)..], out var minutes))
        {
            return false;
        }
        if (hours < 0 || minutes < 0 || minutes >= 60)
        {
            return false;
        }

        result = new TimeSpan(hours, minutes, 0);
        return true;
    }
}
