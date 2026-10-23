namespace Chronology.Services.Config;

public sealed class ConfigWeeklySum
{
    public bool      Enabled           { get; init; } = false;
    public bool      GoalHighlightShow { get; init; } = true;
    public double    GoalHours         { get; init; } = 40;
    public bool      GoalShow          { get; init; } = true;
    public DayOfWeek WeekStart         { get; init; } = DayOfWeek.Sunday;
}
