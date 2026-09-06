using System.Globalization;
using Chronology.Models;

namespace Chronology.Services;

public class PlaintextDayStore : ITimeLogStore
{
    private readonly string _dataDirectory;
    private readonly IEntryFormatter _formatter;

    public PlaintextDayStore(string dataDirectory)
        : this(dataDirectory, new PlaintextEntryFormatter())
    {
    }

    public PlaintextDayStore(string dataDirectory, IEntryFormatter formatter)
    {
        _dataDirectory = dataDirectory;
        _formatter = formatter;
    }

    public IEnumerable<TimeEntry> Load(DateOnly date)
    {
        var path = GetFilePath(date);
        if (!File.Exists(path))
        {
            yield break;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', 3);
            if (parts.Length != 3)
            {
                continue;
            }

            var timestampStr = parts[0].Trim();
            var activity = parts[1].Trim();
            var durationStr = parts[2].Trim();

            if (!TimeOnly.TryParse(timestampStr, CultureInfo.InvariantCulture, out var timestamp))
            {
                continue;
            }
            if (!TryParseHMM(durationStr, out var duration))
            {
                continue;
            }

            yield return new TimeEntry(timestamp, activity, duration);
        }
    }

    public void Save(DateOnly date, IEnumerable<TimeEntry> entries)
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = GetFilePath(date);
        File.WriteAllText(path, _formatter.Format(date, entries));
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
