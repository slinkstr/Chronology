using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeLogStores;
using Xunit;

namespace Chronology.Tests;

public class PlaintextTimeLogStoreTests : IDisposable
{
    private readonly string _tempDir;

    public PlaintextTimeLogStoreTests()
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
        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.Empty(store.Load(new DateOnly(2026, 9, 4)));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEntries()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
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
        var store = new PlaintextTimeLogStore(_tempDir);
        var date = new DateOnly(2026, 9, 4);
        store.Save(date, [new TimeEntry(new TimeOnly(9, 0), "Test", TimeSpan.FromMinutes(15))]);

        var path = Path.Combine(_tempDir, "2026-09-04.txt");
        var lines = File.ReadAllLines(path);
        Assert.StartsWith("# Chronology log - 2026-09-04", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains("| Time", lines[2]);
        Assert.Contains("| Test", lines[4]);
    }

    [Fact]
    public void Load_SkipsCommentsAndBlankLines()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-05.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-05",
            "",
            "| Time  | Activity                                                                      | Duration |",
            "| ----- | ----------------------------------------------------------------------------- | -------- |",
            "| 09:00 | Fix bug                                                                       | 0:48     |",
            "  ",
            "| 10:15 | Review                                                                        | 0:30     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        var loaded = store.Load(new DateOnly(2026, 9, 5)).ToList();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Fix bug", loaded[0].Activity);
        Assert.Equal("Review", loaded[1].Activity);
    }

    [Fact]
    public void Load_MalformedTimestamp_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-06.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-06",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| XX:00 | Fix bug | 0:48     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        var ex = Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 6)).ToList());
        Assert.Contains("line 5", ex.Message);
        Assert.Contains("invalid timestamp", ex.Message);
    }

    [Fact]
    public void Load_MalformedDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-07.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-07",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| 09:00 | Fix bug | 0:XX     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        var ex = Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 7)).ToList());
        Assert.Contains("line 5", ex.Message);
        Assert.Contains("invalid duration", ex.Message);
    }

    [Fact]
    public void Load_DecimalDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-08.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-08",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| 09:00 | Fix bug  | 1.5      |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 8)).ToList());
    }

    [Fact]
    public void Load_NegativeDecimalDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-08.txt");
        File.WriteAllLines(path, [
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| 09:00 | Fix bug  | -1.5     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 8)).ToList());
    }

    [Fact]
    public void Load_NonFiniteDuration_ThrowsTimeLogFormatException()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-08.txt");
        File.WriteAllLines(path, [
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "| 09:00 | Fix bug  | NaN      |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.Throws<TimeLogFormatException>(() => store.Load(new DateOnly(2026, 9, 8)).ToList());
    }

    [Fact]
    public void Load_NonTableLine_SkipsWithoutThrowing()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "2026-09-08.txt");
        File.WriteAllLines(path, [
            "# Chronology log - 2026-09-08",
            "",
            "| Time  | Activity | Duration |",
            "| ----- | -------- | -------- |",
            "Some user note here",
            "| 09:00 | Fix bug | 0:48     |",
        ]);

        var store = new PlaintextTimeLogStore(_tempDir);
        var loaded = store.Load(new DateOnly(2026, 9, 8)).ToList();

        Assert.Single(loaded);
        Assert.Equal("Fix bug", loaded[0].Activity);
    }

    [Fact]
    public void Validate_NormalActivity_ReturnsNull()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.Null(store.Validate("Fix bug"));
    }

    [Fact]
    public void Validate_PipeInActivity_ReturnsError()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.NotNull(store.Validate("Foo | bar"));
    }

    [Fact]
    public void Validate_NewlineInActivity_ReturnsError()
    {
        var store = new PlaintextTimeLogStore(_tempDir);
        Assert.NotNull(store.Validate("Foo\nbar"));
    }
}
