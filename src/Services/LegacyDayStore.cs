using System.Globalization;
using Chronology.Models;

namespace Chronology.Services;

public class LegacyDayStore : ITimeLogStore
{
    private const int TimeWidth = 5;
    private const int IndentWidth = 30;

    private readonly string _dataDirectory;
    private readonly IEntryFormatter _formatter;

    public LegacyDayStore(string dataDirectory)
        : this(dataDirectory, new LegacyEntryFormatter())
    {
    }

    public LegacyDayStore(string dataDirectory, IEntryFormatter formatter)
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
        Directory.CreateDirectory(_dataDirectory);
        var path = GetFilePath(date);
        File.WriteAllText(path, _formatter.Format(date, entries));
    }

    private string GetFilePath(DateOnly date) => Path.Combine(_dataDirectory, $"{date:yyyy-MM-dd}.log");

    public IEnumerable<DateOnly> GetAvailableDates()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_dataDirectory, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var date))
            {
                yield return date;
            }
        }
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
