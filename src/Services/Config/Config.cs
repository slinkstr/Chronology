using System.Text.Json;
using System.Text.Json.Serialization;
using Chronology.Services.TimeLogStores;

namespace Chronology.Services;

public sealed class Config
{
    private const int    CurrentVersion = 1;
    private const string FileName       = "config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = true,
    };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public int            Version  { get; set; } = CurrentVersion;
    [JsonConverter(typeof(TimeLogStoreJsonConverter))]
    public ITimeLogStore? Store    { get; set; } = new PlaintextTimeLogStore();
    public ConfigReminder Reminder { get; set; } = new();

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
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("version", out _))
                {
                    throw new ConfigException("Missing or invalid 'version' field in config.");
                }
                if (!root.TryGetProperty("store", out _))
                {
                    throw new ConfigException("Missing 'store' section in config.");
                }
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
