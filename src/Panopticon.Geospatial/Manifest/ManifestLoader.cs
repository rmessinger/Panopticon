using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Geospatial.Manifest;

public static class ManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new ManifestKebabEnumConverterFactory() },
    };

    public static GeospatialImportManifest? Load(
        string path,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(diagnostics);
        limits ??= new GeospatialImportLimits();

        var fullPath = Path.GetFullPath(path);
        var manifestDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(manifestDirectory))
        {
            manifestDirectory = Directory.GetCurrentDirectory();
        }

        byte[] json;
        try
        {
            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestFailure,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    $"Manifest file not found: {fullPath}",
                    fullPath));
                return null;
            }

            json = ReadBounded(fullPath, limits.MaxManifestBytes, out var oversized);
            if (oversized)
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestOversized,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    $"Manifest exceeds the limit of {limits.MaxManifestBytes} bytes.",
                    fullPath));
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestFailure,
                GeoStage.Manifest,
                GeoSeverity.Error,
                $"Manifest could not be read: {ex.Message}",
                fullPath,
                ex));
            return null;
        }

        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestSchemaInvalid,
                GeoStage.Manifest,
                GeoSeverity.Error,
                $"Manifest does not match the schema: {ex.Message}",
                fullPath,
                ex));
            return null;
        }

        return ManifestValidator.TryBuild(dto, manifestDirectory, fullPath, diagnostics);
    }

    private static byte[] ReadBounded(string path, long limit, out bool oversized)
    {
        oversized = false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                oversized = true;
                return [];
            }
        }

        return buffer.ToArray();
    }
}
