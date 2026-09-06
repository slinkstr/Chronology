using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chronology.Services;

public class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppStateStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "state.json");
    }

    public AppState Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppState();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}

public class AppState
{
    public long ClockTicks { get; set; }

    public string Activity { get; set; } = "";

    [JsonIgnore]
    public TimeSpan Clock
    {
        get => TimeSpan.FromTicks(ClockTicks);
        set => ClockTicks = value.Ticks;
    }
}
