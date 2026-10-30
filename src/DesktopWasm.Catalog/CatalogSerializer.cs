using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog;

/// <summary>Deterministic catalog JSON: sorted entries/keys, stable formatting.
/// Byte-identical output for identical input, so diffs mean content changed.</summary>
public static class CatalogSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // CapabilityConverter first: the enum converter factory would
        // otherwise claim AppCapability and emit a prose "mixed" string.
        Converters = { new CapabilityConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Wire form for capabilities: ["winforms"], ["wpf"], or
    /// ["winforms","wpf"] for mixed. Fixed order, never an exclusive enum.</summary>
    public sealed class CapabilityConverter : JsonConverter<AppCapability>
    {
        public override AppCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = AppCapability.None;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Capabilities must be an array.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var name = reader.GetString();
                result |= name switch
                {
                    "winforms" => AppCapability.WinForms,
                    "wpf" => AppCapability.Wpf,
                    _ => throw new JsonException($"Unknown capability '{name}'."),
                };
            }
            return result;
        }

        public override void Write(Utf8JsonWriter writer, AppCapability value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            if (value.HasFlag(AppCapability.WinForms)) writer.WriteStringValue("winforms");
            if (value.HasFlag(AppCapability.Wpf)) writer.WriteStringValue("wpf");
            writer.WriteEndArray();
        }
    }

    public static string Serialize(IEnumerable<AppDescriptor> apps)
    {
        var ordered = apps
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(Normalize).ToList();
        return JsonSerializer.Serialize(new CatalogFile(2, ordered), Options) + "\n";
    }

    private static AppDescriptor Normalize(AppDescriptor app) => app with
    {
        Aliases = app.Aliases.OrderBy(a => a, StringComparer.Ordinal).ToArray(),
        Evidence = app.Evidence.OrderBy(e => e, StringComparer.Ordinal).ToArray(),
        Gaps = app.Gaps.OrderBy(g => g, StringComparer.Ordinal).ToArray(),
        Package = app.Package is null ? null : app.Package with
        {
            Assemblies = app.Package.Assemblies.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => a with { References = a.References.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToArray() }).ToArray(),
            Gaps = app.Package.Gaps.OrderBy(g => g.Assembly, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Kind, StringComparer.Ordinal).ToArray(),
        },
    };

    public static AppRegistry Deserialize(string json)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(json, Options)
            ?? throw new InvalidDataException("Invalid catalog file.");
        if (file.SchemaVersion != 2)
            throw new InvalidDataException("Unsupported catalog schema.");
        foreach (var app in file.Apps)
            CatalogValidator.ValidateDescriptor(app);
        return new AppRegistry(file.Apps);
    }

    public sealed record CatalogFile(int SchemaVersion, List<AppDescriptor> Apps);
}
