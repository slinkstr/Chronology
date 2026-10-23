using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Chronology.Services.Config;
using Chronology.Services.TimeLogStores;

namespace Chronology.Tools.ConfigDocGen;

internal static class ConfigDocGenerator
{
    private static readonly Dictionary<string, string> Notes = new()
    {
        ["version"]                  = "Config schema version, do not edit.",
        ["reminder.enabled"]         = "Plays audio alert when application has been idle for the specified time.",
        ["reminder.intervalMinutes"] = "Must be > 0.",
        ["reminder.count"]           = "Must be > 0.",
        ["reminder.soundFile"]       = "`.wav` file; `null` = system sound. File must exist.",
        ["weeklySum.enabled"]        = "Show the weekly counter under Total.",
        ["weeklySum.goalHighlightShow"] = "Highlight the weekly sum when the goal is met.",
        ["weeklySum.goalHours"]      = "Must be > 0.",
        ["weeklySum.goalShow"]       = "Show the `/ goal` suffix.",
        ["store.dataDirectory"]      = "Relative paths resolve against the working directory.",
        ["store.durationFormat"]     = "Duration format in files - `9:30` vs `9.50`.",
    };

    private static readonly HashSet<string> UsedNotes = new(StringComparer.Ordinal);

    private const string StartMarker = "<!-- configdocgen:start -->";
    private const string EndMarker   = "<!-- configdocgen:end -->";

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+Config\s*$", RegexOptions.Compiled);
    private static readonly Regex AnyHeadingPattern = new(@"^(#{1,6})\s", RegexOptions.Compiled);

    private record Row(string Key, string Type, string Default, string Note);

    public static string Splice(string readme)
    {
        var lines = readme.Replace("\r\n", "\n").Split('\n').ToList();
        var headingIndex = lines.FindIndex(l => HeadingPattern.IsMatch(l));
        if (headingIndex < 0)
        {
            throw new InvalidOperationException("No '# Config' heading found. Add one where the generated section should live.");
        }
        var level = HeadingPattern.Match(lines[headingIndex]).Groups[1].Length;
        var sectionEnd = lines.FindIndex(headingIndex + 1, l => AnyHeadingPattern.Match(l) is { Success: true } m && m.Groups[1].Length <= level);
        if (sectionEnd < 0)
        {
            sectionEnd = lines.Count;
        }

        var sectionLength = sectionEnd - headingIndex - 1;
        var startIndex = lines.FindIndex(headingIndex + 1, sectionLength, l => l.Trim() == StartMarker);
        var endIndex   = lines.FindIndex(headingIndex + 1, sectionLength, l => l.Trim() == EndMarker);
        if (startIndex < 0 || endIndex <= startIndex)
        {
            throw new InvalidOperationException($"No '{StartMarker}'/'{EndMarker}' markers found in the '# Config' section. Add them around the generated content.");
        }

        lines.RemoveRange(startIndex + 1, endIndex - startIndex - 1);
        lines.InsertRange(startIndex + 1, GenerateBody(level));
        return string.Join('\n', lines);
    }

    public static void WriteSection(string readmePath)
    {
        File.WriteAllText(readmePath, Splice(File.ReadAllText(readmePath)));
    }

    private static List<string> GenerateBody(int level)
    {
        UsedNotes.Clear();
        var sb = new StringBuilder();
        sb.Append('\n');
        AppendTable(sb, MainRows());

        foreach (var (name, type) in StoreTypes())
        {
            var instance = CreateDefault(type);
            sb.Append('\n')
              .Append('#', Math.Min(level + 1, 6)).Append(' ').Append('`').Append(Escape(name)).Append("` store").Append("\n\n");
            AppendTable(sb, LeafRows(type, instance, StorePrefix));
        }
        ValidateNotes();

        var body = sb.ToString().TrimEnd('\n').Split('\n').ToList();
        body.Add("");
        return body;
    }

    private static List<Row> MainRows()
    {
        var rows = new List<Row>();
        var config = new Config();
        foreach (var prop in PropertiesOf(typeof(Config)))
        {
            if (typeof(ITimeLogStore).IsAssignableFrom(prop.Type))
            {
                continue;
            }
            AddRows(rows, prop, "");
        }

        var storeTypes = StoreTypes();
        var storeNames = string.Join(" \\| ", storeTypes.Select(s => $"`{Escape(s.Name)}`"));
        var defaultStore = storeTypes.FirstOrDefault(s => s.Type == config.Store.GetType()).Name
            ?? throw new InvalidOperationException($"Default store type '{config.Store.GetType().Name}' is not registered in {nameof(TimeLogStoreJsonConverter)}.{nameof(TimeLogStoreJsonConverter.StoreTypes)}.");
        var typeKey = $"{StorePrefix}{TimeLogStoreJsonConverter.TypeKey}";
        rows.Add(new Row($"`{typeKey}`", storeNames, $"`{Escape(defaultStore)}`", Note(typeKey)));
        return rows;
    }

    private static void AddRows(List<Row> rows, Prop prop, string prefix)
    {
        var key = prefix + prop.Name;
        if (IsSectionType(prop.Type))
        {
            rows.AddRange(LeafRows(prop.Type, prop.Value, key + "."));
            return;
        }
        if (!IsScalarType(prop.Type))
        {
            throw new InvalidOperationException($"Config key '{key}' has unsupported type '{prop.Type.Name}'.");
        }
        rows.Add(new Row($"`{key}`", TypeOf(prop.Type), Render(prop.Value), Note(key)));
    }

    private static List<Row> LeafRows(Type pocoType, object? instance, string prefix)
    {
        var rows = new List<Row>();
        foreach (var prop in PropertiesOf(pocoType, instance))
        {
            AddRows(rows, prop, prefix);
        }
        return rows;
    }

    private static bool IsSectionType(Type type)
    {
        return type is { IsClass: true }
            && type != typeof(string)
            && !typeof(ITimeLogStore).IsAssignableFrom(type)
            && type.Assembly == typeof(Config).Assembly;
    }

    private static bool IsScalarType(Type type)
    {
        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;
        return unwrapped.IsPrimitive || unwrapped.IsEnum || unwrapped == typeof(string);
    }

    private static bool IsConfigurable(Type type, PropertyInfo p)
    {
        if (p.CanWrite)
        {
            return true;
        }
        return type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(param => string.Equals(param.Name, p.Name, StringComparison.OrdinalIgnoreCase));
    }

    private record Prop(string Name, Type Type, object? Value);

    private static IEnumerable<Prop> PropertiesOf(Type type, object? instance = null)
    {
        instance ??= CreateDefault(type);
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true } && p.GetIndexParameters().Length == 0
                     && p.GetCustomAttribute<JsonIgnoreAttribute>() is null
                     && IsConfigurable(type, p))
            .Select(p => new Prop(JsonName(p), p.PropertyType, p.GetValue(instance)));
    }

    private static string JsonName(PropertyInfo p)
    {
        return p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? Config.JsonOptions.PropertyNamingPolicy?.ConvertName(p.Name)
            ?? p.Name;
    }

    private static string StorePrefix =>
        JsonName(typeof(Config).GetProperty(nameof(Config.Store))!) + ".";

    private static object CreateDefault(Type type)
    {
        var ctor = type.GetConstructors()
            .OrderBy(c => c.GetParameters().Length)
            .First(c => c.GetParameters().All(p => p.HasDefaultValue));
        return ctor.Invoke(ctor.GetParameters().Select(p => p.DefaultValue).ToArray());
    }

    private static List<(string Name, Type Type)> StoreTypes()
    {
        return TimeLogStoreJsonConverter.StoreTypes
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static string TypeOf(Type type)
    {
        if (type.IsEnum)
        {
            return string.Join(" \\| ", Enum.GetNames(type).Select(n => $"`{n}`"));
        }
        return type switch
        {
            _ when type == typeof(bool)   => "bool",
            _ when type == typeof(int)    => "int",
            _ when type == typeof(double) => "double",
            _ when type == typeof(string) => "string",
            _ => type.Name,
        };
    }

    private static string Render(object? value)
    {
        return value switch
        {
            null     => "`null`",
            bool b   => b ? "`true`" : "`false`",
            Enum e   => $"`{e}`",
            string s => $"`{Escape(s)}`",
            _        => $"`{Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}`",
        };
    }

    private static string Escape(string s)
    {
        return s.Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }

    private static string Note(string key)
    {
        if (Notes.TryGetValue(key, out var note))
        {
            UsedNotes.Add(key);
            return note;
        }
        return "";
    }

    private static void ValidateNotes()
    {
        var unused = Notes.Keys.Where(k => !UsedNotes.Contains(k)).ToList();
        if (unused.Count > 0)
        {
            throw new InvalidOperationException($"Notes entries reference unknown config keys: {string.Join(", ", unused.Select(k => $"'{k}'"))}");
        }
    }

    private static void AppendTable(StringBuilder sb, IReadOnlyList<Row> rows)
    {
        var headers = new[] { "Key", "Type", "Default", "Notes" };
        var widths = new int[4];
        foreach (var row in rows.Append(new Row(headers[0], headers[1], headers[2], headers[3])))
        {
            widths[0] = Math.Max(widths[0], row.Key.Length);
            widths[1] = Math.Max(widths[1], row.Type.Length);
            widths[2] = Math.Max(widths[2], row.Default.Length);
            widths[3] = Math.Max(widths[3], row.Note.Length);
        }

        AppendRow(sb, widths, headers[0], headers[1], headers[2], headers[3]);
        AppendRow(sb, widths, new string('-', widths[0]), new string('-', widths[1]), new string('-', widths[2]), new string('-', widths[3]));
        foreach (var row in rows)
        {
            AppendRow(sb, widths, row.Key, row.Type, row.Default, row.Note);
        }
    }

    private static void AppendRow(StringBuilder sb, int[] widths, params string[] cells)
    {
        sb.Append("| ").Append(cells[0].PadRight(widths[0]))
          .Append(" | ").Append(cells[1].PadRight(widths[1]))
          .Append(" | ").Append(cells[2].PadRight(widths[2]))
          .Append(" | ").Append(cells[3].PadRight(widths[3])).Append(" |\n");
    }
}
