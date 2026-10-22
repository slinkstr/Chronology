using System.Globalization;

namespace Chronology.Services.TimeLogStores;

internal static class DurationText
{
    public static string Format(TimeSpan duration, DurationFormat format)
    {
        return format switch
        {
            DurationFormat.Hhmm       => $"{(int)duration.TotalHours}:{duration.Minutes:D2}",
            DurationFormat.Fractional => duration.TotalHours.ToString("F2", CultureInfo.InvariantCulture),
            _                         => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public static bool TryParse(ReadOnlySpan<char> text, out TimeSpan result)
    {
        var span = text.Trim();
        return span.IndexOf(':') >= 0 ? TryParseHhmm(span, out result) : TryParseHours(span, out result);
    }

    public static bool TryParseHhmm(ReadOnlySpan<char> text, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var span = text.Trim();
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

        var totalMinutes = (double)hours * 60 + minutes;
        if (totalMinutes > TimeSpan.MaxValue.TotalMinutes)
        {
            return false;
        }
        result = TimeSpan.FromMinutes(totalMinutes);
        return true;
    }

    private static bool TryParseHours(ReadOnlySpan<char> span, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            || !double.IsFinite(hours)
            || hours < 0
            || hours >= TimeSpan.MaxValue.TotalHours)
        {
            return false;
        }

        result = TimeSpan.FromHours(hours);
        return true;
    }
}
