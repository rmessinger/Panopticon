using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public static class HarnessCatalogJson
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(HarnessCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return JsonSerializer.Serialize(catalog, Options);
    }

    public static HarnessCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException("Harness catalog JSON must not be empty.");
        var catalog = JsonSerializer.Deserialize<HarnessCatalog>(json, Options)
            ?? throw new JsonException("Harness catalog JSON is not an object.");
        if (catalog.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Harness catalog schema version {catalog.SchemaVersion} is not supported; expected {CurrentSchemaVersion}.");
        return catalog;
    }
}
