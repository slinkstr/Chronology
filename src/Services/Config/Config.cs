using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Chronology.Services.Theming;
using Chronology.Services.TimeLogStores;

namespace Chronology.Services.Config;

public sealed class Config
{
    private const int    CurrentVersion = 1;
    private const string FileName       = "config.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
        Converters           =
        {
            new JsonStringEnumConverter<DayOfWeek>(allowIntegerValues: false),
            new JsonStringEnumConverter<ConfigThemeVariant>(allowIntegerValues: false),
        },
    };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public int             Version  { get; set; } = CurrentVersion;
    [JsonConverter(typeof(TimeLogStoreJsonConverter))]
    public ITimeLogStore   Store    { get; set; } = new PlaintextTimeLogStore();
    public ConfigReminder  Reminder { get; set; } = new();
    public ConfigWeeklySum WeeklySum { get; set; } = new();
    public ConfigTheme     Theme     { get; set; } = new();

    public static Config Load()
    {
        return Load(FilePath);
    }

    internal static Config Load(string filePath)
    {
        EnsureDefaultExists(filePath);
        var config = DeserializeFile(filePath);
        ValidateVersion(config.Version);
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        config.Reminder = ValidateReminder(config.Reminder, configDirectory);
        config.WeeklySum = ValidateWeeklySum(config.WeeklySum);
        config.Theme = ValidateTheme(config.Theme, configDirectory);
        return config;
    }

    private static Config DeserializeFile(string filePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Failed to read config file: {ex.Message}", ex);
        }

        try
        {
            var root = JsonNode.Parse(json)
                ?? throw new ConfigException("Config file is empty.");
            StripNullMembers(root);
            if (root is JsonObject rootObject && !rootObject.ContainsKey("version"))
            {
                throw new ConfigException("Missing or invalid 'version' field in config.");
            }
            return root.Deserialize<Config>(JsonOptions)
                ?? throw new ConfigException("Config file is empty.");
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config file is not valid JSON or does not match the expected format: {ex.Message}", ex);
        }
    }

    private static void EnsureDefaultExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(filePath, JsonSerializer.Serialize(new Config(), JsonOptions));
    }

    // Null == unset
    private static void StripNullMembers(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Value is null)
                    {
                        obj.Remove(property.Key);
                    }
                    else
                    {
                        StripNullMembers(property.Value);
                    }
                }
                break;
            case JsonArray array:
                foreach (var element in array)
                {
                    if (element is not null)
                    {
                        StripNullMembers(element);
                    }
                }
                break;
        }
    }

    private static string? ResolveSoundFile(string? configuredPath, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }
        if (!string.Equals(Path.GetExtension(configuredPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigException("Invalid 'reminder.soundFile' in config. Only .wav files are supported.");
        }

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(configuredPath, configDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigException($"Invalid 'reminder.soundFile' in config: {ex.Message}", ex);
        }
        if (!File.Exists(resolvedPath))
        {
            throw new ConfigException($"Reminder sound file does not exist: {configuredPath}");
        }
        return resolvedPath;
    }

    private static ConfigReminder ValidateReminder(ConfigReminder? reminder, string configDirectory)
    {
        reminder ??= new ConfigReminder();
        var minutes = reminder.IntervalMinutes;
        if (!double.IsFinite(minutes) || minutes <= 0)
        {
            throw new ConfigException("Invalid 'reminder.intervalMinutes' in config. Expected a positive number of minutes.");
        }
        try
        {
            _ = TimeSpan.FromMinutes(minutes);
        }
        catch (OverflowException ex)
        {
            throw new ConfigException($"Value '{minutes}' is out of range for 'reminder.intervalMinutes'.", ex);
        }
        if (reminder.Count < 1)
        {
            throw new ConfigException("Invalid 'reminder.count' in config. Expected a positive count.");
        }

        return new ConfigReminder
        {
            Count           = reminder.Count,
            Enabled         = reminder.Enabled,
            IntervalMinutes = minutes,
            SoundFile       = ResolveSoundFile(reminder.SoundFile, configDirectory),
        };
    }

    private static ConfigWeeklySum ValidateWeeklySum(ConfigWeeklySum? weeklySum)
    {
        weeklySum ??= new ConfigWeeklySum();
        var hours = weeklySum.GoalHours;
        if (!double.IsFinite(hours) || hours <= 0)
        {
            throw new ConfigException("Invalid 'weeklySum.goalHours' in config. Expected a positive number of hours.");
        }
        try
        {
            _ = TimeSpan.FromHours(hours);
        }
        catch (OverflowException ex)
        {
            throw new ConfigException($"Value '{hours}' is out of range for 'weeklySum.goalHours'.", ex);
        }

        return new ConfigWeeklySum
        {
            Enabled           = weeklySum.Enabled,
            GoalHighlightShow = weeklySum.GoalHighlightShow,
            GoalHours         = hours,
            GoalShow          = weeklySum.GoalShow,
            WeekStart         = weeklySum.WeekStart,
        };
    }

    private static ConfigTheme ValidateTheme(ConfigTheme? theme, string configDirectory)
    {
        theme ??= new ConfigTheme();
        var overrideFile = ResolveThemeFile(theme.OverrideFile, configDirectory);
        return new ConfigTheme
        {
            Variant      = theme.Variant,
            OverrideFile = overrideFile,
            Overrides    = overrideFile is null ? null : ThemeOverrides.Load(overrideFile),
        };
    }

    private static string? ResolveThemeFile(string? configuredPath, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }
        if (!string.Equals(Path.GetExtension(configuredPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigException("Invalid 'theme.overrideFile' in config. Only .json files are supported.");
        }

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(configuredPath, configDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigException($"Invalid 'theme.overrideFile' in config: {ex.Message}", ex);
        }
        if (!File.Exists(resolvedPath))
        {
            throw new ConfigException($"Theme override file does not exist: {configuredPath}");
        }
        return resolvedPath;
    }

    private static void ValidateVersion(int version)
    {
        if (version > CurrentVersion)
        {
            throw new ConfigException($"Config version {version} is not supported. Maximum supported version is {CurrentVersion}.");
        }
        if (version < CurrentVersion)
        {
            throw new ConfigException($"Config version {version} is too old. Migration not yet implemented.");
        }
    }
}
