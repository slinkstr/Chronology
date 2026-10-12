using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeLogStores;
using Xunit;

namespace Chronology.Tests;

public class LegacyTimeLogStoreTests : IDisposable
{
    private readonly string _tempDir;

    public LegacyTimeLogStoreTests()
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
        var store = new LegacyTimeLogStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Code review", TimeSpan.FromMinutes(30))]);

        var path = Path.Combine(_tempDir, "TimeLog.20260904.txt");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEntries()
    {
        var store = new LegacyTimeLogStore(_tempDir);
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
    public void SaveLoad_RoundTrip_PreservesWrappedActivity()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        var activity = "DEMO-1111 fixing that one really annoying bug where you input a time but instead of adding it subtracts.";

        store.Save(date, [new TimeEntry(new TimeOnly(0, 0), activity, TimeSpan.FromMinutes(30))]);
        var loaded = store.Load(date).ToList();

        Assert.Single(loaded);
        Assert.Equal(activity, loaded[0].Activity);
    }

    [Fact]
    public void GetAvailableDates_ParsesTimeLogFilenames()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.20260904.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "TimeLog.20260905.txt"), "");

        var store = new LegacyTimeLogStore(_tempDir);
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

        var store = new LegacyTimeLogStore(_tempDir);
        var dates = store.GetAvailableDates().ToList();

        Assert.Single(dates);
        Assert.Equal(new DateOnly(2026, 9, 4), dates[0]);
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsEmpty()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        Assert.Empty(store.Load(new DateOnly(2026, 9, 4)));
    }

    [Fact]
    public void Format_MatchesReferenceOutput()
    {
        var entries = new List<TimeEntry>
        {
            new(new TimeOnly(9, 0), "DEMO-123 code review.", TimeSpan.FromHours(0.15)),
            new(new TimeOnly(9, 0), "Alice meeting.", TimeSpan.FromHours(0.65)),
            new(new TimeOnly(9, 0), "DEMO-2111 code review.", TimeSpan.FromHours(0.24)),
            new(new TimeOnly(9, 0), "Bob meeting.", TimeSpan.FromHours(0.50)),
            new(new TimeOnly(9, 0), "Break.", TimeSpan.FromHours(1.00)),
            new(new TimeOnly(9, 0), "Server outage.", TimeSpan.FromHours(0.82)),
            new(new TimeOnly(9, 0), "DEMO-3111 debugging.", TimeSpan.FromHours(1.87)),
            new(new TimeOnly(9, 0), "DEMO-7111 planning.", TimeSpan.FromHours(0.24)),
            new(new TimeOnly(9, 0), "DEMO-1111 fixing that one really annoying bug where you input a time but instead of adding it subtracts.", TimeSpan.FromHours(0.27)),
        };

        var output = LegacyTimeLogStore.Format(new DateOnly(2026, 9, 8), entries);

        var expected = string.Join(Environment.NewLine, new[]
        {
            "Time   Project                Activity Description (2026-09-08)",
            "-----  --------------------   ----------------------------------------------------------------------",
            " 0.15  [GENERAL]              DEMO-123 code review.",
            " 0.65  [GENERAL]              Alice meeting.",
            " 0.24  [GENERAL]              DEMO-2111 code review.",
            " 0.50  [GENERAL]              Bob meeting.",
            " 1.00  [GENERAL]              Break.",
            " 0.82  [GENERAL]              Server outage.",
            " 1.87  [GENERAL]              DEMO-3111 debugging.",
            " 0.24  [GENERAL]              DEMO-7111 planning.",
            " 0.27  [GENERAL]              DEMO-1111 fixing that one really annoying bug where you input a time",
            "                              but instead of adding it subtracts.",
            "-----  --------------------   ----------------------------------------------------------------------",
            " 5.74  *TOTAL                 Total time spent on work related activities.",
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

        var output = LegacyTimeLogStore.Format(new DateOnly(2026, 9, 2), entries);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(70, lines[2][30..].Length);
        Assert.Equal(new string('B', 30), lines[3][30..]);
        Assert.Equal(30, lines[3][..30].Length);
    }

    [Fact]
    public void Format_SeparatorLine_Is100Chars()
    {
        var output = LegacyTimeLogStore.Format(new DateOnly(2026, 9, 2), []);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(100, lines[1].Length);
        Assert.Equal(100, lines[2].Length);
    }

    [Fact]
    public void Contents_NoFile_ReturnsFormattedEmpty()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        var contents = store.Contents(new DateOnly(2026, 9, 4));

        Assert.Contains("Activity Description (2026-09-04)", contents);
        Assert.Contains("*TOTAL", contents);
    }

    [Fact]
    public void Contents_AfterSave_ReturnsFileContents()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        store.Save(new DateOnly(2026, 9, 4), [
            new TimeEntry(new TimeOnly(9, 0), "Code review", TimeSpan.FromMinutes(30)),
        ]);

        var contents = store.Contents(new DateOnly(2026, 9, 4));
        Assert.Contains("Code review", contents);
    }

    [Fact]
    public void Load_InvalidDurationField_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "TimeLog.20260905.txt");
        File.WriteAllLines(path, [
            "Time   Project                Activity Description (2026-09-05)",
            "-----  --------------------   ----------------------------------------------------------------------",
            "abcde  [GENERAL]              Bogus row",
        ]);

        var store = new LegacyTimeLogStore(_tempDir);
        var ex = Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 5)).ToList());
        Assert.Contains("line 3", ex.Message);
        Assert.Contains("invalid duration", ex.Message);
    }

    [Fact]
    public void Load_NegativeDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "TimeLog.20260905.txt");
        File.WriteAllLines(path, [
            "Time   Project                Activity Description (2026-09-05)",
            "-----  --------------------   ----------------------------------------------------------------------",
            "-1.50  [GENERAL]              Bad row",
        ]);

        var store = new LegacyTimeLogStore(_tempDir);
        Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 5)).ToList());
    }

    [Fact]
    public void Load_NonFiniteDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "TimeLog.20260905.txt");
        File.WriteAllLines(path, [
            "Time   Project                Activity Description (2026-09-05)",
            "-----  --------------------   ----------------------------------------------------------------------",
            "  NaN  [GENERAL]              Bad row",
        ]);

        var store = new LegacyTimeLogStore(_tempDir);
        Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 5)).ToList());
    }

    [Fact]
    public void Validate_PipeInActivity_ReturnsNull()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        Assert.Null(store.Validate("Foo | bar"));
    }

    [Fact]
    public void Validate_NewlineInActivity_ReturnsError()
    {
        var store = new LegacyTimeLogStore(_tempDir);
        Assert.NotNull(store.Validate("Foo\nbar"));
    }
}
