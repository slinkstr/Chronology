using Chronology.Models;
using Chronology.Services;
using Xunit;

namespace Chronology.Tests;

public class PlaintextDayStoreTests : IDisposable
{
    private readonly string _tempDir;

    public PlaintextDayStoreTests()
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
    public void Load_NonexistentFile_ReturnsEmpty()
    {
        var store = new PlaintextDayStore(_tempDir);
        Assert.Empty(store.Load(new DateOnly(2026, 9, 4)));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEntries()
    {
        var store = new PlaintextDayStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        var entries = new List<TimeEntry>
        {
            new(new TimeOnly(9, 0), "Fix login bug", TimeSpan.FromMinutes(48)),
            new(new TimeOnly(10, 15), "Code review", TimeSpan.FromMinutes(30)),
        };

        store.Save(date, entries);
        var loaded = store.Load(date).ToList();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(entries[0], loaded[0]);
        Assert.Equal(entries[1], loaded[1]);
    }

    [Fact]
    public void Save_IncludesBannerLine()
    {
        var store = new PlaintextDayStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Test", TimeSpan.FromMinutes(15))]);

        var path = Path.Combine(_tempDir, "2026-09-04.txt");
        var lines = File.ReadAllLines(path);
        Assert.StartsWith("# Chronology log - 2026-09-04", lines[0]);
        Assert.Contains("| Test", lines[1]);
    }

    [Fact]
    public void Load_SkipsCommentsAndBlankLines()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-05.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-05",
            "",
            "09:00 | Fix bug | 0:48",
            "  ",
            "10:15 | Review | 0:30",
        ]);

        var store = new PlaintextDayStore(_tempDir);
        var loaded = store.Load(new DateOnly(2026, 9, 5)).ToList();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Fix bug", loaded[0].Activity);
        Assert.Equal("Review", loaded[1].Activity);
    }
}
