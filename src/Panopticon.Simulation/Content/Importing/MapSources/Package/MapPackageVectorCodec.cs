#nullable enable

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Strict JSON codec for the package vector feature sidecar. Output is indented UTF-8 with LF line endings.</summary>
public static class MapPackageVectorCodec
{
    private const int DocumentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize(VectorLayerSet layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var document = new MapPackageVectorDocument(
            DocumentVersion,
            layers.Features.Select(MapPackageVectorFeature.FromModel).ToArray());
        var json = JsonSerializer.Serialize(document, SerializerOptions).Replace("\r\n", "\n") + "\n";
        return Encoding.UTF8.GetBytes(json);
    }

    public static VectorLayerSet Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        MapPackageVectorDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<MapPackageVectorDocument>(bytes, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Vector layer JSON is malformed: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new FormatException("Vector layer JSON deserialized to null.");
        }

        if (document.Version != DocumentVersion)
        {
            throw new FormatException($"Vector layer document version {document.Version} is not supported; expected version {DocumentVersion}.");
        }

        if (document.Features is null)
        {
            throw new FormatException("Vector layer document is missing its feature list.");
        }

        try
        {
            return new VectorLayerSet(document.Features.Select(f => (f ?? throw new FormatException("Vector layer contains a null feature.")).ToModel()).ToArray());
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Vector layer contains an invalid feature: {ex.Message}", ex);
        }
    }
}
