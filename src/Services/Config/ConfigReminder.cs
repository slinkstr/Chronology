using System.Text.Json.Serialization;

namespace Chronology.Services.Config;

public sealed class ConfigReminder
{
    public bool    Enabled         { get; init; } = false;
    public int     Count           { get; init; } = 1;
    public double  IntervalMinutes { get; init; } = 30;
    public string? SoundFile       { get; init; } = null;

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);
}
