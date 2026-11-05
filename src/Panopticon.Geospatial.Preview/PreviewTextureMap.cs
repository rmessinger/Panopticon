using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Panopticon.Geospatial.Preview;

public enum PreviewTextureResolution
{
    Resolved,

    /// <summary>The palette name has no binding; palette keys compare <see cref="StringComparer.Ordinal"/>.</summary>
    MissingBinding,

    /// <summary>The bound value matches no catalog texture name, even case-insensitively.</summary>
    UnknownTextureName,
}

/// <summary>
/// Palette-name to terrain-texture bindings plus the known texture-name catalog (B4b
/// group 5 decision 3): a strict bare JSON object of strings, <see cref="StringComparer.Ordinal"/>
/// palette keys, extra entries allowed, binding values canonicalized case-insensitively
/// to actual catalog block names. An unknown value fails only when a payload palette
/// entry actually uses it.
/// </summary>
public sealed class PreviewTextureMap
{
    private readonly Dictionary<string, string> _bindings;

    private readonly Dictionary<string, string> _canonicalValues;

    private PreviewTextureMap(Dictionary<string, string> bindings, Dictionary<string, string> canonicalValues)
    {
        _bindings = bindings;
        _canonicalValues = canonicalValues;
    }

    /// <param name="paletteToTextureName">Palette name to Terrain.ini block name; Ordinal keys.</param>
    public PreviewTextureMap(IReadOnlyDictionary<string, string> paletteToTextureName, ITerrainTextureCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(paletteToTextureName);
        ArgumentNullException.ThrowIfNull(catalog);

        _bindings = new Dictionary<string, string>(paletteToTextureName.Count, StringComparer.Ordinal);
        foreach (var pair in paletteToTextureName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Value);

            if (!_bindings.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException($"The palette name '{pair.Key}' has more than one binding.", nameof(paletteToTextureName));
            }
        }

        _canonicalValues = ResolveCanonicalValues(catalog, _bindings);
    }

    /// <summary>
    /// Parses the strict textures file: a bare JSON object mapping palette names to
    /// terrain texture block names. Duplicate keys, non-string values, blank keys or
    /// values, and any non-object root fail with a typed diagnostic.
    /// </summary>
    public static PreviewTextureMap? FromJson(string json, ITerrainTextureCatalog catalog, GeoDiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        Dictionary<string, string>? bindings;
        try
        {
            bindings = ParseBindings(json, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.TextureMapJsonInvalid,
                GeoStage.Parse,
                GeoSeverity.Error,
                "The texture map is not valid strict JSON.",
                detail: exception));

            return null;
        }

        if (bindings is null)
        {
            return null;
        }

        try
        {
            return new PreviewTextureMap(bindings, catalog);
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.TextureMapJsonInvalid,
                GeoStage.Parse,
                GeoSeverity.Error,
                "A texture map entry is blank.",
                detail: exception));

            return null;
        }
    }

    /// <summary>Number of bindings, used and extra.</summary>
    public int Count => _bindings.Count;

    public PreviewTextureResolution TryResolve(string paletteName, out string? canonicalTextureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paletteName);

        if (!_bindings.TryGetValue(paletteName, out var value))
        {
            canonicalTextureName = null;
            return PreviewTextureResolution.MissingBinding;
        }

        if (!_canonicalValues.TryGetValue(paletteName, out var canonical))
        {
            canonicalTextureName = null;
            return PreviewTextureResolution.UnknownTextureName;
        }

        canonicalTextureName = canonical;
        return PreviewTextureResolution.Resolved;
    }

    private static Dictionary<string, string> ResolveCanonicalValues(ITerrainTextureCatalog catalog, Dictionary<string, string> bindings)
    {
        var catalogNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in catalog.TerrainTextureNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            // A catalog carrying the same block name twice differing only in case keeps the first.
            if (!catalogNames.TryAdd(name, name))
            {
                catalogNames[name] = name;
            }
        }

        var canonicalValues = new Dictionary<string, string>(bindings.Count, StringComparer.Ordinal);
        foreach (var pair in bindings)
        {
            // Unknown values stay unresolved here and fail only when a payload palette entry uses them.
            if (catalogNames.TryGetValue(pair.Value, out var canonical))
            {
                canonicalValues.Add(pair.Key, canonical);
            }
        }

        return canonicalValues;
    }

    private static Dictionary<string, string>? ParseBindings(string json, GeoDiagnosticBag diagnostics)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new GeoDiagnostic(
                PreviewDiagnosticCodes.TextureMapJsonInvalid,
                GeoStage.Parse,
                GeoSeverity.Error,
                "The texture map root must be a bare JSON object of palette name to texture name."));

            return null;
        }

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(new GeoDiagnostic(
                    PreviewDiagnosticCodes.TextureMapJsonInvalid,
                    GeoStage.Parse,
                    GeoSeverity.Error,
                    $"The binding for '{property.Name}' must be a string; found {property.Value.ValueKind}."));

                return null;
            }

            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                diagnostics.Add(new GeoDiagnostic(
                    PreviewDiagnosticCodes.TextureMapJsonInvalid,
                    GeoStage.Parse,
                    GeoSeverity.Error,
                    $"The binding for '{property.Name}' has a blank name or texture name."));

                return null;
            }

            if (!bindings.TryAdd(property.Name, property.Value.GetString()!))
            {
                diagnostics.Add(new GeoDiagnostic(
                    PreviewDiagnosticCodes.TextureMapDuplicateKey,
                    GeoStage.Parse,
                    GeoSeverity.Error,
                    $"The palette name '{property.Name}' appears more than once."));

                return null;
            }
        }

        return bindings;
    }
}
