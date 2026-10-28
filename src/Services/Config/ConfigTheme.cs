using System.Text.Json.Serialization;
using Chronology.Services.Theming;

namespace Chronology.Services.Config;

public sealed class ConfigTheme
{
    public ConfigThemeVariant Variant      { get; init; } = ConfigThemeVariant.Default;
    public string?            OverrideFile { get; init; } = null;

    [JsonIgnore]
    internal ThemeOverrides.ParsedFile? Overrides { get; init; }
}
