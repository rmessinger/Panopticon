#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Strict JSON parser and deterministic serializer for map package manifests.</summary>
public static class MapPackageManifestJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(MapPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var json = JsonSerializer.Serialize(manifest, SerializerOptions);

        return json.Replace("\r\n", "\n") + "\n";
    }

    public static (MapPackageManifest? Manifest, IReadOnlyList<MapPackageDiagnostic> Diagnostics) Parse(string json)
    {
        var diagnostics = new List<MapPackageDiagnostic>();

        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageDiagnostic.MalformedJson,
                "Manifest JSON content is empty or whitespace."));
            return (null, diagnostics);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<MapPackageManifest>(json, SerializerOptions);

            if (manifest is null)
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageDiagnostic.NullComponent,
                    "Manifest JSON deserialized to null."));
                return (null, diagnostics);
            }

            return (manifest, diagnostics);
        }
        catch (JsonException ex)
        {
            if (ex.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new MapPackageDiagnostic(MapPackageDiagnostic.UnknownJsonMember, ex.Message));
            }
            else if (ex.Message.Contains("Unknown property type", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new MapPackageDiagnostic(MapPackageDiagnostic.PropertyType, ex.Message));
            }
            else
            {
                diagnostics.Add(new MapPackageDiagnostic(MapPackageDiagnostic.MalformedJson, ex.Message));
            }

            return (null, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new MapPackageDiagnostic(MapPackageDiagnostic.MalformedJson, ex.Message));
            return (null, diagnostics);
        }
    }

    public static (MapPackageManifest? Manifest, IReadOnlyList<MapPackageDiagnostic> Diagnostics) ParseAndValidate(string json)
    {
        var (parsed, parseDiagnostics) = Parse(json);

        if (parsed is null || parseDiagnostics.Count > 0)
        {
            return (parsed, parseDiagnostics);
        }

        var validationDiagnostics = MapPackageManifestValidator.Validate(parsed);

        return (parsed, validationDiagnostics);
    }

    public static bool TryParse(
        string json,
        [NotNullWhen(true)] out MapPackageManifest? manifest,
        out IReadOnlyList<MapPackageDiagnostic> diagnostics)
    {
        var (parsed, allDiagnostics) = ParseAndValidate(json);

        if (parsed is null || allDiagnostics.Count > 0)
        {
            manifest = null;
            diagnostics = allDiagnostics;
            return false;
        }

        manifest = parsed;
        diagnostics = [];
        return true;
    }
}
