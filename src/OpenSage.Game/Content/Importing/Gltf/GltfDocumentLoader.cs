#nullable enable

using System;
using System.IO;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Resolves one external resource (buffer or image) relative to the document's virtual
/// directory. Returns null when the resource does not exist.
/// </summary>
internal delegate byte[]? GltfExternalResourceResolver(string relativePath);

/// <summary>
/// Loads and strictly parses one bounded glTF root document through the import file system.
/// Returns null for a missing root; malformed documents throw <see cref="InvalidDataException"/>.
/// </summary>
internal static class GltfDocumentLoader
{
    public static ModelRoot? TryLoadRoot(
        string modelPath,
        AssetImportContext context,
        GltfImportOptions options,
        GltfResourceResolver? resourceResolver)
    {
        GltfExternalResourceResolver? externalResolver = resourceResolver is null
            ? null
            : resourceResolver.Resolve;

        return TryLoadRoot(modelPath, context, options, externalResolver);
    }

    public static ModelRoot? TryLoadRoot(
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

        using var rootStream = new MemoryStream(rootBytes);
        try
        {
            return modelPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                ? readContext.ReadBinarySchema2(rootStream)
                : readContext.ReadTextSchema2(rootStream);
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
}
