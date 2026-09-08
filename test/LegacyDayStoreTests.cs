using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeStores;
using Xunit;

namespace Chronology.Tests;

public class LegacyDayStoreTests : IDisposable
{
    private readonly string _tempDir;

    public LegacyDayStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChronologyTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Save_UsesTimeLogFilenameScheme()
    {
        var store = new LegacyDayStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Code review", TimeSpan.FromMinutes(30))]);

        var path = Path.Combine(_tempDir, "TimeLog.20260904.txt");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEntries()
    {
        var store = new LegacyDayStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        var entries = new List<TimeEntry>
        {
            new(new TimeOnly(0, 0), "Code review", TimeSpan.FromMinutes(30)),
            new(new TimeOnly(0, 0), "Fix bug", TimeSpan.FromMinutes(48)),
        };

        store.Save(date, entries);
        var loaded = store.Load(date).ToList();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Code review", loaded[0].Activity);
        Assert.Equal(TimeSpan.FromMinutes(30), loaded[0].Duration);
        Assert.Equal("Fix bug", loaded[1].Activity);
        Assert.Equal(TimeSpan.FromMinutes(48), loaded[1].Duration);
    }

    [Fact]
    public void GetAvailableDates_ParsesTimeLogFilenames()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.20260904.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.20260905.txt"), "");

        var store = new LegacyDayStore(_tempDir);
        var dates = store.GetAvailableDates().OrderBy(d => d).ToList();

        Assert.Equal(2, dates.Count);
        Assert.Equal(new DateOnly(2026, 9, 4), dates[0]);
        Assert.Equal(new DateOnly(2026, 9, 5), dates[1]);
    }

    [Fact]
    public void GetAvailableDates_IgnoresNonTimeLogFiles()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.20260904.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "2026-09-04.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.notadate.txt"), "");

        var store = new LegacyDayStore(_tempDir);
        var dates = store.GetAvailableDates().ToList();

        Assert.Single(dates);
        Assert.Equal(new DateOnly(2026, 9, 4), dates[0]);
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsEmpty()
    {
        var store = new LegacyDayStore(_tempDir);
        Assert.Empty(store.Load(new DateOnly(2026, 9, 4)));
    }

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

        var store = new LegacyDayStore(_tempDir);
        var output = store.Format(new DateOnly(2026, 9, 2), entries);

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

        var store = new LegacyDayStore(_tempDir);
        var output = store.Format(new DateOnly(2026, 9, 2), entries);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(70, lines[2][30..].Length);
        Assert.Equal(new string('B', 30), lines[3][30..]);
        Assert.Equal(30, lines[3][..30].Length);
    }

    [Fact]
    public void Format_SeparatorLine_Is100Chars()
    {
        var store = new LegacyDayStore(_tempDir);
        var output = store.Format(new DateOnly(2026, 9, 2), []);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(100, lines[1].Length);
        Assert.Equal(100, lines[2].Length);
    }

    [Fact]
    public void Contents_NoFile_ReturnsFormattedEmpty()
    {
        var store = new LegacyDayStore(_tempDir);
        var contents = store.Contents(new DateOnly(2026, 9, 4));

        Assert.Contains("Activity Description (2026-09-04)", contents);
        Assert.Contains("*TOTAL", contents);
    }

    [Fact]
    public void Contents_AfterSave_ReturnsFileContents()
    {
        var store = new LegacyDayStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Code review", TimeSpan.FromMinutes(30)),
        ]);

        var contents = store.Contents(new DateOnly(2026, 9, 4));
        Assert.Contains("Code review", contents);
    }
}
