#nullable enable

using System;
using System.IO;
using System.Text.Json;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Resolves one external resource (buffer or image) relative to the document's virtual
/// directory. Returns null when the resource does not exist.
/// </summary>
internal delegate byte[]? GltfExternalResourceResolver(string relativePath);

/// <summary>
/// A parsed root document plus whether the source declared a default scene. SharpGLTF
/// synthesizes a default scene when none is declared, so declared-ness is detected from the
/// raw document.
/// </summary>
internal sealed record GltfLoadedDocument(ModelRoot Root, bool HasDeclaredDefaultScene);

/// <summary>
/// Loads and strictly parses one bounded glTF root document through the import file system.
/// Returns null for a missing root; malformed documents throw <see cref="InvalidDataException"/>.
/// </summary>
internal static class GltfDocumentLoader
{
    public static GltfLoadedDocument? TryLoadDocument(
        string modelPath,
        AssetImportContext context,
        GltfImportOptions options,
        GltfResourceResolver? resourceResolver)
    {
        GltfExternalResourceResolver? externalResolver = resourceResolver is null
            ? null
            : resourceResolver.Resolve;

        return TryLoadDocument(modelPath, context, options, externalResolver);
    }

    public static GltfLoadedDocument? TryLoadDocument(
        string modelPath,
        AssetImportContext context,
        GltfImportOptions options,
        GltfExternalResourceResolver? externalResolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var entry = context.FileSystem.GetFile(modelPath);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > options.MaxRootDocumentBytes)
        {
            throw new InvalidDataException(
                $"glTF root document '{modelPath}' is {entry.Length} bytes; the limit is {options.MaxRootDocumentBytes} bytes.");
        }

        byte[] rootBytes;
        using (var stream = entry.Open())
        using (var bounded = new MemoryStream(capacity: (int)entry.Length))
        {
            stream.CopyTo(bounded);
            rootBytes = bounded.ToArray();
        }

        var readSettings = new ReadSettings
        {
            Validation = ValidationMode.Strict,
        };

        var readContext = ReadContext.Create(ResolveResource)
            .WithSettingsFrom(readSettings);

        var isGlb = modelPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        var hasDeclaredDefaultScene = HasDeclaredDefaultScene(rootBytes, isGlb);

        using var rootStream = new MemoryStream(rootBytes);
        try
        {
            var root = isGlb
                ? readContext.ReadBinarySchema2(rootStream)
                : readContext.ReadTextSchema2(rootStream);
            return new GltfLoadedDocument(root, hasDeclaredDefaultScene);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to parse glTF document '{modelPath}': {ex.Message}", ex);
        }

        ArraySegment<byte> ResolveResource(string assetName)
        {
            if (externalResolver is null)
            {
                throw new InvalidDataException(
                    $"glTF document '{modelPath}' references external resource '{assetName}', which requires an external resource resolver.");
            }

            var resolved = externalResolver(assetName);
            if (resolved is null)
            {
                throw new InvalidDataException(
                    $"glTF document '{modelPath}' references external resource '{assetName}', which could not be loaded.");
            }

            return new ArraySegment<byte>(resolved);
        }
    }

    private static bool HasDeclaredDefaultScene(byte[] rootBytes, bool isGlb)
    {
        var jsonBytes = isGlb
            ? ExtractGlbJsonChunk(rootBytes)
            : rootBytes;

        try
        {
            using var document = JsonDocument.Parse(jsonBytes);
            return document.RootElement.TryGetProperty("scene", out _);
        }
        catch (JsonException)
        {
            // The strict SharpGLTF parse below reports malformed documents with proper context.
            return true;
        }
    }

    private static byte[] ExtractGlbJsonChunk(byte[] glbBytes)
    {
        if (glbBytes.Length < 20)
        {
            return [];
        }

        var jsonChunkLength = BitConverter.ToInt32(glbBytes, 12);
        var jsonChunkStart = 20;

        if (jsonChunkLength <= 0 || jsonChunkStart + jsonChunkLength > glbBytes.Length)
        {
            return [];
        }

        var jsonBytes = new byte[jsonChunkLength];
        Array.Copy(glbBytes, jsonChunkStart, jsonBytes, 0, jsonChunkLength);
        return jsonBytes;
    }
}
