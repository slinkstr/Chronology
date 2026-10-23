namespace Chronology.Services.Config;

public sealed class ConfigWeeklySum
{
    public bool      Enabled      { get; init; } = false;
    public double    HoursPerWeek { get; init; } = 40;
    public bool      ShowGoal     { get; init; } = true;
    public DayOfWeek WeekStart    { get; init; } = DayOfWeek.Sunday;
}
