using System.Text.Json;
using System.Text.Json.Serialization;
using Chronology.Services.Config;

namespace Chronology.Services.TimeLogStores;

internal sealed class TimeLogStoreJsonConverter : JsonConverter<ITimeLogStore>
{
    private const string TypeKey = "type";

    public override ITimeLogStore? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigException("Invalid 'store' section in config. Expected an object.");
        }
        if (!root.TryGetProperty(TypeKey, out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new ConfigException("Missing or invalid 'store.type' in config.");
        }

        var typeName = typeElement.GetString()!;
        try
        {
            if (typeName.Equals(PlaintextTimeLogStore.StoreName, StringComparison.OrdinalIgnoreCase))
            {
                return DeserializeStore<PlaintextTimeLogStore>(root, options);
            }
            if (typeName.Equals(LegacyTimeLogStore.StoreName, StringComparison.OrdinalIgnoreCase))
            {
                return DeserializeStore<LegacyTimeLogStore>(root, options);
            }
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Failed to construct store '{typeName}': {ex.Message}", ex);
        }
        throw new ConfigException($"Unknown store type '{typeName}'.");
    }

    public override void Write(Utf8JsonWriter writer, ITimeLogStore value, JsonSerializerOptions options)
    {
        var typeName = value switch
        {
            PlaintextTimeLogStore => PlaintextTimeLogStore.StoreName,
            LegacyTimeLogStore    => LegacyTimeLogStore.StoreName,
            _ => throw new ConfigException($"Cannot serialize store type '{value.GetType().Name}'."),
        };
        writer.WriteStartObject();
        writer.WriteString(TypeKey, typeName);
        using var document = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static TStore DeserializeStore<TStore>(JsonElement root, JsonSerializerOptions options)
        where TStore : class, IFileBackedStore
    {
        var store = root.Deserialize<TStore>(options);
        if (store is null || string.IsNullOrWhiteSpace(store.DataDirectory))
        {
            throw new ConfigException("Missing or invalid 'store.dataDirectory' in config. Expected a non-empty directory path.");
        }
        return store;
    }
}
