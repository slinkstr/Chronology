using Chronology.Models;
using Chronology.Services;
using Xunit;

namespace Chronology.Tests;

public class LegacyEntryFormatterTests
{
    [Fact]
    public void Format_MatchesReferenceOutput()
    {
        var entries = new List<TimeEntry>
        {
            new(new TimeOnly(9, 0), "DEMO-123 code review.", TimeSpan.FromHours(0.09)),
            new(new TimeOnly(9, 0), "DEMO-4111 code review.", TimeSpan.FromHours(0.19)),
            new(new TimeOnly(9, 0), "DEMO-1111 code review.", TimeSpan.FromHours(0.34)),
            new(new TimeOnly(9, 0), "DEMO-2111 code review.", TimeSpan.FromHours(0.04)),
            new(new TimeOnly(9, 0), "DEMO-3111 code review.", TimeSpan.FromHours(0.12)),
            new(new TimeOnly(9, 0), "DEMO-1111 code review.", TimeSpan.FromHours(0.09)),
            new(new TimeOnly(9, 0), "DEMO-2111 code review.", TimeSpan.FromHours(0.09)),
            new(new TimeOnly(9, 0), "Alice meeting.", TimeSpan.FromHours(0.39)),
            new(new TimeOnly(9, 0), "DEMO-2111 code review.", TimeSpan.FromHours(0.14)),
            new(new TimeOnly(9, 0), "DEMO-3111 code review.", TimeSpan.FromHours(0.77)),
            new(new TimeOnly(9, 0), "DEMO-3111 code review.", TimeSpan.FromHours(0.29)),
            new(new TimeOnly(9, 0), "DEMO-7111 code review.", TimeSpan.FromHours(0.54)),
            new(new TimeOnly(9, 0), "DEMO-3111 code review.", TimeSpan.FromHours(0.42)),
            new(new TimeOnly(9, 0), "Bob meeting.", TimeSpan.FromHours(0.50)),
            new(new TimeOnly(9, 0), "Break.", TimeSpan.FromHours(0.50)),
            new(new TimeOnly(9, 0), "Server outage.", TimeSpan.FromHours(0.80)),
            new(new TimeOnly(9, 0), "DEMO-3111 code review.", TimeSpan.FromHours(0.20)),
            new(new TimeOnly(9, 0), "DEMO-7111 code review.", TimeSpan.FromHours(0.17)),
            new(new TimeOnly(9, 0), "DEMO-1111.", TimeSpan.FromHours(1.55)),
        };

        var formatter = new LegacyEntryFormatter();
        var output = formatter.Format(new DateOnly(2026, 9, 2), entries);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "Time   Project                Activity Description (2026-09-02)",
            "-----  --------------------   ----------------------------------------------------------------------",
            " 0.09  [GENERAL]              DEMO-123 code review.",
            " 0.19  [GENERAL]              DEMO-4111 code review.",
            " 0.34  [GENERAL]              DEMO-1111 code review.",
            " 0.04  [GENERAL]              DEMO-2111 code review.",
            " 0.12  [GENERAL]              DEMO-3111 code review.",
            " 0.09  [GENERAL]              DEMO-1111 code review.",
            " 0.09  [GENERAL]              DEMO-2111 code review.",
            " 0.39  [GENERAL]              Alice meeting.",
            " 0.14  [GENERAL]              DEMO-2111 code review.",
            " 0.77  [GENERAL]              DEMO-3111 code review.",
            " 0.29  [GENERAL]              DEMO-3111 code review.",
            " 0.54  [GENERAL]              DEMO-7111 code review.",
            " 0.42  [GENERAL]              DEMO-3111 code review.",
            " 0.50  [GENERAL]              Bob meeting.",
            " 0.50  [GENERAL]              Break.",
            " 0.80  [GENERAL]              Server outage.",
            " 0.20  [GENERAL]              DEMO-3111 code review.",
            " 0.17  [GENERAL]              DEMO-7111 code review.",
            " 1.55  [GENERAL]              DEMO-1111.",
            "-----  --------------------   ----------------------------------------------------------------------",
            " 7.23  *TOTAL                 Total time spent on work related activities.",
            "-----  --------------------   ----------------------------------------------------------------------",
        }) + Environment.NewLine;

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Format_LongActivity_WrapsAt70Chars()
    {
        var longActivity = new string('A', 70) + new string('B', 30);
        var entries = new List<TimeEntry>
        {
            new(new TimeOnly(9, 0), longActivity, TimeSpan.FromHours(1.00)),
        };

        var formatter = new LegacyEntryFormatter();
        var output = formatter.Format(new DateOnly(2026, 9, 2), entries);
        var lines = output.Split(Environment.NewLine);

        // Line 0: header, Line 1: separator, Line 2: first data row, Line 3: continuation
        Assert.Equal(70, lines[2][30..].Length);
        Assert.Equal(new string('B', 30), lines[3][30..]);
        Assert.Equal(30, lines[3][..30].Length);
    }

    [Fact]
    public void Format_SeparatorLine_Is100Chars()
    {
        var formatter = new LegacyEntryFormatter();
        var output = formatter.Format(new DateOnly(2026, 9, 2), []);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(100, lines[1].Length);
        Assert.Equal(100, lines[2].Length);
    }
}
