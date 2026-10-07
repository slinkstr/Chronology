using System.Text.Json;
using Chronology.Services;
using Chronology.Services.Config;
using Chronology.Services.TimeLogStores;
using Xunit;

namespace Chronology.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChronologyConfigTests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    [Fact]
    public void Load_CreatesDefaultConfig()
    {
        var config = Config.Load(ConfigPath);

        Assert.NotNull(config.Store);
        Assert.NotNull(config.Reminder);
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void Load_ConfigWithoutReminder_UsesDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext",
                "dataDirectory": "./timelog"
              }
            }
            """);

        var config = Config.Load(ConfigPath);

        AssertReminderMatches(new ConfigReminder(), config.Reminder);
    }

    [Fact]
    public void Load_CustomReminder_LoadsIntervalAndRelativeSoundFile()
    {
        Directory.CreateDirectory(_tempDir);
        var soundPath = Path.Combine(_tempDir, "reminder.wav");
        File.WriteAllBytes(soundPath, [0]);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "intervalMinutes": 12.5,
                "soundFile": "reminder.wav"
              }
            }
            """);

        var config = Config.Load(ConfigPath);

        Assert.Equal(TimeSpan.FromMinutes(12.5), config.Reminder.Interval);
        Assert.Equal(soundPath, config.Reminder.SoundFile);
    }

    [Fact]
    public void Load_ReminderDisabled_LoadsDisabled()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "enabled": false
              }
            }
            """);

        var config = Config.Load(ConfigPath);

        AssertReminderMatches(new ConfigReminder { Enabled = false }, config.Reminder);
    }

    [Fact]
    public void Load_InvalidReminderInterval_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "intervalMinutes": 0
              }
            }
            """);

        var ex = Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
        Assert.Contains("intervalMinutes", ex.Message);
    }

    [Fact]
    public void Load_InvalidReminderCount_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "count": 0
              }
            }
            """);

        var ex = Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
        Assert.Contains("count", ex.Message);
    }

    [Fact]
    public void Load_ValidReminderCount_LoadsCount()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "count": 3
              }
            }
            """);

        var config = Config.Load(ConfigPath);

        Assert.Equal(3, config.Reminder.Count);
    }

    [Fact]
    public void Load_MissingSoundFile_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "soundFile": "missing.wav"
              }
            }
            """);

        var ex = Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Load_NonWavSoundFile_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "reminder.mp3"), [0]);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": {
                "type": "Plaintext"
              },
              "reminder": {
                "soundFile": "reminder.mp3"
              }
            }
            """);

        var ex = Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
        Assert.Contains(".wav", ex.Message);
    }

    [Fact]
    public void Load_WritesFullyPopulatedSeedFile()
    {
        Config.Load(ConfigPath);

        using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        var root = document.RootElement;

        Assert.Equal(["reminder", "store", "version"], root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(1, root.GetProperty("version").GetInt32());

        var store = root.GetProperty("store");
        Assert.Equal(["dataDirectory", "type"], store.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("Plaintext", store.GetProperty("type").GetString());

        var reminder = root.GetProperty("reminder");
        Assert.Equal(["count", "enabled", "intervalMinutes", "soundFile"], reminder.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void Load_NullReminder_UsesDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": { "type": "Plaintext" },
              "reminder": null
            }
            """);

        var config = Config.Load(ConfigPath);

        AssertReminderMatches(new ConfigReminder(), config.Reminder);
    }

    [Fact]
    public void Load_StoreWithoutDataDirectory_UsesDefaultDirectory()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": { "type": "Plaintext" }
            }
            """);

        var config = Config.Load(ConfigPath);

        Assert.Equal(new PlaintextTimeLogStore().DataDirectory, Assert.IsType<PlaintextTimeLogStore>(config.Store).DataDirectory);
    }

    [Fact]
    public void Load_UnknownStoreType_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": { "type": "Nonexistent" }
            }
            """);

        var ex = Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
        Assert.Contains("Unknown store type", ex.Message);
    }

    [Fact]
    public void Load_MissingStore_Throws()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1
            }
            """);

        Assert.Throws<ConfigException>(() => Config.Load(ConfigPath));
    }

    [Fact]
    public void Load_NullStore_LoadsWithoutStore()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(ConfigPath, """
            {
              "version": 1,
              "store": null
            }
            """);

        var config = Config.Load(ConfigPath);

        Assert.Null(config.Store);
    }

    private static void AssertReminderMatches(ConfigReminder expected, ConfigReminder actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.IntervalMinutes, actual.IntervalMinutes);
        Assert.Equal(expected.SoundFile, actual.SoundFile);
    }
}
