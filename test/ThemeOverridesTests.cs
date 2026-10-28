using Avalonia.Media;
using Avalonia.Media.Immutable;
using Chronology.Services.Config;
using Chronology.Services.Theming;
using Xunit;

namespace Chronology.Tests;

public class ThemeOverridesTests : IDisposable
{
    private readonly string _tempDir;

    public ThemeOverridesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChronologyThemeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string WriteTheme(string json)
    {
        var path = Path.Combine(_tempDir, "theme.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_ParsesAllSections()
    {
        var path = WriteTheme("""
            {
              "shared": { "SystemAccentColor": "#B69CFF" },
              "dark":   { "ErrorColor": "#FF7A93" },
              "light":  { "GoalMetColor": "#7EE2A8" }
            }
            """);

        var parsed = ThemeOverrides.Load(path);

        Assert.Equal("#B69CFF", parsed.Shared!["SystemAccentColor"]);
        Assert.Equal("#FF7A93", parsed.Dark!["ErrorColor"]);
        Assert.Equal("#7EE2A8", parsed.Light!["GoalMetColor"]);
    }

    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        var path = WriteTheme("""
            {
              // user note
              "dark": {
                "ErrorColor": "#FF7A93", // inline note
              },
            }
            """);

        var parsed = ThemeOverrides.Load(path);

        Assert.Equal("#FF7A93", parsed.Dark!["ErrorColor"]);
    }

    [Fact]
    public void Load_NonObjectRoot_Throws()
    {
        var path = WriteTheme("[1, 2, 3]");

        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.Load(path));
        Assert.Contains("must contain a JSON object", ex.Message);
    }

    [Fact]
    public void Load_NonObjectSection_Throws()
    {
        var path = WriteTheme("""{ "dark": "#FF7A93" }""");

        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.Load(path));
        Assert.Contains("must be a JSON object", ex.Message);
    }

    [Fact]
    public void Load_NonScalarLeaf_Throws()
    {
        var path = WriteTheme("""{ "dark": { "ErrorColor": { "r": 255 } } }""");

        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.Load(path));
        Assert.Contains("must be a string or number", ex.Message);
    }

    [Fact]
    public void Load_NumericLeaf_ParsesAsNumber()
    {
        var path = WriteTheme("""{ "dark": { "FontSize": 13.5 } }""");

        var parsed = ThemeOverrides.Load(path);

        Assert.Equal(13.5, parsed.Dark!["FontSize"]);
    }

    [Fact]
    public void BuildEntries_ColorResource_AssignsColor()
    {
        var built = ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["ErrorColor"] = "#FF7A93" },
            _ => Colors.White);

        Assert.Equal(Color.Parse("#FF7A93"), Assert.IsType<Color>(built["ErrorColor"]));
    }

    [Fact]
    public void BuildEntries_SolidColorBrushResource_AssignsSolidColorBrush()
    {
        var built = ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["SystemRegionBrush"] = "#17111F" },
            _ => new SolidColorBrush(Colors.White));

        var brush = Assert.IsType<SolidColorBrush>(built["SystemRegionBrush"]);
        Assert.IsNotType<ImmutableSolidColorBrush>(brush);
        Assert.Equal(Color.Parse("#17111F"), brush.Color);
    }

    [Fact]
    public void BuildEntries_OtherBrushResource_WrapsInImmutableBrush()
    {
        var built = ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["SomeBrush"] = "#B69CFF" },
            _ => new ImmutableSolidColorBrush(Colors.White));

        var brush = Assert.IsType<ImmutableSolidColorBrush>(built["SomeBrush"]);
        Assert.Equal(Color.Parse("#B69CFF"), brush.Color);
    }

    [Fact]
    public void BuildEntries_FontFamilyResource_ParsesFamily()
    {
        var built = ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["MonospaceFontFamily"] = "JetBrains Mono" },
            _ => new FontFamily("Inter"));

        Assert.Equal("JetBrains Mono", Assert.IsType<FontFamily>(built["MonospaceFontFamily"]).Name);
    }

    [Fact]
    public void BuildEntries_NumericResource_AssignsNumber()
    {
        var built = ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["FontSize"] = 13.5 },
            _ => 12.0);

        Assert.Equal(13.5, Assert.IsType<double>(built["FontSize"]));
    }

    [Fact]
    public void BuildEntries_UnknownKey_Throws()
    {
        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["NoSuchColor"] = "#FF7A93" },
            _ => null));

        Assert.Contains("does not match any known resource", ex.Message);
    }

    [Fact]
    public void BuildEntries_ColorTypeMismatch_Throws()
    {
        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["ErrorColor"] = 42.0 },
            _ => Colors.White));

        Assert.Contains("does not match resource type", ex.Message);
    }

    [Fact]
    public void BuildEntries_BadColorString_Throws()
    {
        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.BuildEntries(
            new Dictionary<string, object> { ["ErrorColor"] = "not-a-color" },
            _ => Colors.White));

        Assert.Contains("invalid color", ex.Message);
    }
}
