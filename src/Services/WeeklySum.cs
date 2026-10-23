using Chronology.Services.TimeLogStores;

namespace Chronology.Services;

public class WeeklySum
{
    private readonly HashSet<DateOnly> _failedDates = [];
    private readonly ITimeLogStore     _store;
    private readonly Func<DateOnly>    _today;
    private readonly DayOfWeek         _weekStart;

    public WeeklySum(ITimeLogStore store, Func<DateOnly> today, DayOfWeek weekStart)
    {
        _store     = store;
        _today     = today;
        _weekStart = weekStart;
    }

    public TimeSpan Compute(Action<Exception>? onError = null)
    {
        var today = _today();
        var start = today.AddDays(-(((int)today.DayOfWeek - (int)_weekStart + 7) % 7));
        var total = TimeSpan.Zero;
        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            try
            {
                foreach (var entry in _store.Load(date))
                {
                    total += entry.Duration;
                }
                _failedDates.Remove(date);
            }
            catch (Exception ex) when (ex is TimeLogFormatException or IOException or UnauthorizedAccessException)
            {
                if (_failedDates.Add(date))
                {
                    onError?.Invoke(ex);
                }
            }
        }
        return total;
    }
}
