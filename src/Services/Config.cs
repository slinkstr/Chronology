using System.Reflection;
using System.Text.Json;

namespace Chronology.Services;

public static class Config
{
    public const int CurrentVersion = 1;
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public static ITimeLogStore? LoadStore()
    {
        EnsureDefaultExists();

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Failed to read config file: {ex.Message}");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config file is not valid JSON: {ex.Message}");
        }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("version", out var versionEl) || versionEl.ValueKind != JsonValueKind.Number)
            {
                throw new ConfigException("Missing or invalid 'version' field in config.");
            }
            var version = versionEl.GetInt32();
            if (version > CurrentVersion)
            {
                throw new ConfigException($"Config version {version} is not supported. Maximum supported version is {CurrentVersion}.");
            }
            if (version < CurrentVersion)
            {
                throw new ConfigException($"Config version {version} is too old. Migration not yet implemented.");
            }

            if (!doc.RootElement.TryGetProperty("store", out var storeEl) || storeEl.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigException("Missing 'store' section in config.");
            }
            if (!storeEl.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                throw new ConfigException("Missing 'store.type' in config.");
            }

            var typeName = typeEl.GetString()!;
            var type = ResolveStoreType(typeName);
            if (type is null)
            {
                return null;
            }

            try
            {
                return (ITimeLogStore?)storeEl.Deserialize(type);
            }
            catch (JsonException ex)
            {
                throw new ConfigException($"Failed to construct store '{typeName}': {ex.Message}");
            }
        }
    }

    private static Type? ResolveStoreType(string name)
    {
        var target = name.Trim();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract)
                {
                    continue;
                }
                if (!typeof(ITimeLogStore).IsAssignableFrom(type))
                {
                    continue;
                }
                var typeName = type.Name;
                if (typeName.EndsWith("TimeLogStore", StringComparison.Ordinal))
                {
                    typeName = typeName[..^"TimeLogStore".Length];
                }
                if (string.Equals(typeName, target, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }
        }
        return null;
    }

    private static void EnsureDefaultExists()
    {
        if (File.Exists(FilePath))
        {
            return;
        }

        var defaultType = ResolveStoreType("Plaintext");
        var storeSection = BuildDefaultStoreSection(defaultType!);
        var config = new Dictionary<string, object?>
        {
            ["version"] = CurrentVersion,
            ["store"] = storeSection,
        };
        var json = JsonSerializer.Serialize(config, SerializeOptions);
        File.WriteAllText(FilePath, json);
    }

    private static Dictionary<string, object?> BuildDefaultStoreSection(Type storeType)
    {
        var section = new Dictionary<string, object?>();
        var constructor = storeType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var typeName = storeType.Name;
        if (typeName.EndsWith("TimeLogStore", StringComparison.Ordinal))
        {
            typeName = typeName[..^"TimeLogStore".Length];
        }
        section["type"] = typeName;
        foreach (var param in constructor.GetParameters())
        {
            if (param.HasDefaultValue)
            {
                section[char.ToLowerInvariant(param.Name![0]) + param.Name![1..]] = param.DefaultValue;
            }
        }
        return section;
    }
}

public class ConfigException(string message) : Exception(message);
