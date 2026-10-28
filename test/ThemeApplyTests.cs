using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Chronology.Services.Config;
using Chronology.Services.Theming;
using Xunit;

namespace Chronology.Tests;

public class ThemeApplyTests : IDisposable
{
    private readonly string _tempDir;

    public ThemeApplyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChronologyThemeApply_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static Application CreateApp()
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            ["ErrorColor"]          = Colors.OrangeRed,
            ["GoalMetColor"]        = Colors.LimeGreen,
            ["WindowFontFamily"]    = new FontFamily("Inter"),
            ["MonospaceFontFamily"] = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            ["SystemAccentColor"]   = Color.Parse("#FF0078D4"),
        });
        return app;
    }

    private static object? Resolve(IResourceHost app, string key, ThemeVariant variant)
    {
        return app.TryGetResource(key, variant, out var value) ? value : null;
    }

    private string WriteTheme(string json)
    {
        var path = Path.Combine(_tempDir, "theme.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Apply_SetsRequestedVariant()
    {
        var app = CreateApp();

        ThemeOverrides.Apply(app, ConfigThemeVariant.Dark, null);

        Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        Assert.False(app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Dark));
        Assert.Equal(Colors.OrangeRed, Assert.IsType<Color>(Resolve(app, "ErrorColor", ThemeVariant.Dark)));
    }

    [Fact]
    public void Apply_DarkSection_OverridesBothAppAndFluentKeys()
    {
        var app = CreateApp();
        var parsed = ThemeOverrides.Load(WriteTheme("""
            { "dark": { "ErrorColor": "#FF7A93", "SystemAccentColor": "#B69CFF", "MonospaceFontFamily": "JetBrains Mono" } }
            """));

        ThemeOverrides.Apply(app, ConfigThemeVariant.Dark, parsed);

        Assert.Equal(Color.Parse("#FF7A93"), Assert.IsType<Color>(Resolve(app, "ErrorColor", ThemeVariant.Dark)));
        Assert.Equal(Color.Parse("#B69CFF"), Assert.IsType<Color>(Resolve(app, "SystemAccentColor", ThemeVariant.Dark)));
        Assert.Equal("JetBrains Mono", Assert.IsType<FontFamily>(Resolve(app, "MonospaceFontFamily", ThemeVariant.Dark)).Name);
    }

    [Fact]
    public void Apply_Clear_RemovesOverridesAndRestoresDefaults()
    {
        var app = CreateApp();
        var parsed = ThemeOverrides.Load(WriteTheme("""
            { "dark": { "ErrorColor": "#FF7A93" } }
            """));
        ThemeOverrides.Apply(app, ConfigThemeVariant.Dark, parsed);

        ThemeOverrides.Apply(app, ConfigThemeVariant.Dark, null);

        Assert.Equal(Colors.OrangeRed, Assert.IsType<Color>(Resolve(app, "ErrorColor", ThemeVariant.Dark)));
        Assert.False(app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Dark));
    }

    [Fact]
    public void Apply_Shared_AppliesUnderBothVariants_VariantSectionWins()
    {
        var app = CreateApp();
        var parsed = ThemeOverrides.Load(WriteTheme("""
            {
              "shared": { "ErrorColor": "#FF7A93" },
              "light":  { "ErrorColor": "#112233" }
            }
            """));

        ThemeOverrides.Apply(app, ConfigThemeVariant.Default, parsed);

        Assert.Equal(Color.Parse("#FF7A93"), Assert.IsType<Color>(Resolve(app, "ErrorColor", ThemeVariant.Dark)));
        Assert.Equal(Color.Parse("#112233"), Assert.IsType<Color>(Resolve(app, "ErrorColor", ThemeVariant.Light)));
    }

    [Fact]
    public void Apply_NonOverriddenKeys_FallBackToDefaults()
    {
        var app = CreateApp();
        var parsed = ThemeOverrides.Load(WriteTheme("""
            { "dark": { "ErrorColor": "#FF7A93" } }
            """));

        ThemeOverrides.Apply(app, ConfigThemeVariant.Dark, parsed);

        Assert.Equal(Colors.LimeGreen, Assert.IsType<Color>(Resolve(app, "GoalMetColor", ThemeVariant.Dark)));
        Assert.Equal(Color.Parse("#FF0078D4"), Assert.IsType<Color>(Resolve(app, "SystemAccentColor", ThemeVariant.Dark)));
    }

    [Fact]
    public void Apply_UnknownKey_ThrowsAndPreservesCurrentTheme()
    {
        var app = CreateApp();

        var ex = Assert.Throws<ConfigException>(() => ThemeOverrides.Apply(app, ConfigThemeVariant.Dark,
            new ThemeOverrides.ParsedFile(null, new Dictionary<string, object> { ["NotAKey"] = "#000000" }, null)));

        Assert.Contains("does not match any known resource", ex.Message);
        Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        Assert.False(app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Dark));
    }
}
