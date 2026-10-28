using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Chronology.Services.Config;

namespace Chronology.Services.Theming;

internal static class ThemeOverrides
{
    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling     = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static void Apply(Application app, ConfigThemeVariant variant, ParsedFile? overrides)
    {
        var sharedDict = BuildDictionary(overrides?.Shared, ThemeVariant.Default, app);
        var darkDict   = BuildDictionary(overrides?.Dark, ThemeVariant.Dark, app);
        var lightDict  = BuildDictionary(overrides?.Light, ThemeVariant.Light, app);

        app.RequestedThemeVariant = variant switch
        {
            ConfigThemeVariant.Light => ThemeVariant.Light,
            ConfigThemeVariant.Dark  => ThemeVariant.Dark,
            _                        => ThemeVariant.Default,
        };

        var themeDictionaries = app.Resources.ThemeDictionaries;
        SetOrRemove(themeDictionaries, ThemeVariant.Default, sharedDict);
        SetOrRemove(themeDictionaries, ThemeVariant.Dark, darkDict);
        SetOrRemove(themeDictionaries, ThemeVariant.Light, lightDict);
    }

    internal static Dictionary<string, object> BuildEntries(IReadOnlyDictionary<string, object> entries, Func<string, object?> resolveExisting)
    {
        var built = new Dictionary<string, object>();
        foreach (var (key, raw) in entries)
        {
            built[key] = ConvertValue(key, raw, resolveExisting(key));
        }
        return built;
    }

    internal static ParsedFile Load(string path)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: JsonDocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Theme override file '{path}' is not valid JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Failed to read theme override file '{path}': {ex.Message}", ex);
        }

        if (root is not JsonObject rootObject)
        {
            throw new ConfigException($"Theme override file '{path}' must contain a JSON object.");
        }

        IReadOnlyDictionary<string, object>? shared = null, dark = null, light = null;
        foreach (var (name, node) in rootObject)
        {
            var entries = ParseSection(name, node, path);
            switch (name)
            {
                case "shared": shared = entries; break;
                case "dark":   dark   = entries; break;
                case "light":  light  = entries; break;
                default:
                    throw new ConfigException($"Theme override file '{path}' has unknown section '{name}' (expected 'shared', 'light', or 'dark').");
            }
        }

        return new ParsedFile(shared, dark, light);
    }

    private static ResourceDictionary? BuildDictionary(IReadOnlyDictionary<string, object>? entries, ThemeVariant variant, IResourceHost host)
    {
        if (entries is null)
        {
            return null;
        }
        var dictionary = NewDictionary(variant);
        foreach (var (key, value) in BuildEntries(entries, key => ResolveExisting(host, key, variant)))
        {
            dictionary[key] = value;
        }
        return dictionary;
    }

    private static object ConvertValue(string key, object raw, object? existing) => existing switch
    {
        null => throw new ConfigException($"Theme override key '{key}' does not match any known resource."),
        Color when raw is string color       => ParseColor(key, color),
        SolidColorBrush when raw is string color => new SolidColorBrush(ParseColor(key, color)),
        IBrush when raw is string color      => new ImmutableSolidColorBrush(ParseColor(key, color)),
        FontFamily when raw is string family => FontFamily.Parse(family),
        double when raw is double number     => number,
        float when raw is double number      => (float)number,
        int when raw is double number        => (int)number,
        _ => throw new ConfigException($"Theme override value for '{key}' does not match resource type '{existing.GetType().Name}'."),
    };

    private static ResourceDictionary NewDictionary(ThemeVariant variant)
    {
        var dictionary = new ResourceDictionary();
        ((IThemeVariantProvider)dictionary).Key = variant;
        return dictionary;
    }

    private static Color ParseColor(string key, string text)
    {
        if (!Color.TryParse(text, out var color))
        {
            throw new ConfigException($"Theme override '{key}' has invalid color '{text}'.");
        }
        return color;
    }

    private static Dictionary<string, object> ParseSection(string name, JsonNode? node, string path)
    {
        if (node is not JsonObject section)
        {
            throw new ConfigException($"Theme override file '{path}': section '{name}' must be a JSON object.");
        }

        var entries = new Dictionary<string, object>();
        foreach (var (key, leaf) in section)
        {
            if (leaf is JsonValue text && text.TryGetValue<string>(out var s))
            {
                entries[key] = s;
            }
            else if (leaf is JsonValue number && number.TryGetValue<double>(out var n))
            {
                entries[key] = n;
            }
            else
            {
                throw new ConfigException($"Theme override file '{path}': '{name}.{key}' must be a string or number.");
            }
        }
        return entries;
    }

    private static object? ResolveExisting(IResourceHost host, string key, ThemeVariant variant)
    {
        return host.TryGetResource(key, variant, out var value) ? value : null;
    }

    private static void SetOrRemove(IDictionary<ThemeVariant, IThemeVariantProvider> themeDictionaries, ThemeVariant variant, ResourceDictionary? dictionary)
    {
        if (dictionary is not null)
        {
            themeDictionaries[variant] = dictionary;
        }
        else
        {
            themeDictionaries.Remove(variant);
        }
    }

    internal sealed record ParsedFile(
        IReadOnlyDictionary<string, object>? Shared,
        IReadOnlyDictionary<string, object>? Dark,
        IReadOnlyDictionary<string, object>? Light);
}
