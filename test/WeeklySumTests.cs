using Chronology.Models;
using Chronology.Services;
using Chronology.Services.TimeLogStores;
using Xunit;

namespace Chronology.Tests;

public class WeeklySumTests
{
    private static readonly DateOnly Friday = new(2026, 9, 4);

    private static TimeEntry Entry(int hours) => new(new TimeOnly(9, 0), "Work", TimeSpan.FromHours(hours));

    [Fact]
    public void Compute_SumsAllDaysInWeek()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 8, 30), [Entry(8)]);
        store.Save(Friday,                    [Entry(6)]);
        var sum = new WeeklySum(store, () => Friday, DayOfWeek.Sunday);
        Assert.Equal(TimeSpan.FromHours(14), sum.Compute());
    }

    [Fact]
    public void Compute_WeekStartBoundary()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 8, 30), [Entry(8)]);
        store.Save(new DateOnly(2026, 8, 31), [Entry(4)]);
        var sum = new WeeklySum(store, () => Friday, DayOfWeek.Monday);
        Assert.Equal(TimeSpan.FromHours(4), sum.Compute());
    }

    [Fact]
    public void Compute_IncludesFutureDaysInWeek()
    {
        var store = new FakeTimeLogStore();
        store.Save(new DateOnly(2026, 9, 5), [Entry(8)]);
        var sum = new WeeklySum(store, () => Friday, DayOfWeek.Sunday);
        Assert.Equal(TimeSpan.FromHours(8), sum.Compute());
    }

    [Fact]
    public void Compute_LoadError_ReportsOncePerDateUntilHealed()
    {
        var store = new FakeTimeLogStore { LoadError = new TimeLogFormatException("day.txt", 1, "bad") };
        var sum = new WeeklySum(store, () => Friday, DayOfWeek.Sunday);
        var errors = 0;

        sum.Compute(_ => errors++);
        Assert.Equal(7, errors);

        sum.Compute(_ => errors++);
        Assert.Equal(7, errors);

        store.LoadError = null;
        store.Save(Friday, [Entry(1)]);
        Assert.Equal(TimeSpan.FromHours(1), sum.Compute(_ => errors++));
        Assert.Equal(7, errors);

        store.LoadError = new IOException("locked");
        sum.Compute(_ => errors++);
        Assert.Equal(14, errors);
    }
}
