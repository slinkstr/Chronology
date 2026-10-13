using System.Text.Json.Serialization;

namespace Chronology.Services.TimeLogStores;

[JsonConverter(typeof(JsonStringEnumConverter<DurationFormat>))]
public enum DurationFormat
{
    Hhmm,
    Fractional,
}
