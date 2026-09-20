#nullable enable

using System;
using System.IO;
using OpenSage.Content.Importing.Gltf;

namespace OpenSage.Content.Importing;

/// <summary>
/// Explicit external format selection by file name. The selection is immutable and local to
/// a session; this is not a registry and does not change shipped-game load strategies.
/// </summary>
public static class ExternalAssetInspection
{
    public const string GltfFormatId = "GLTF";

    /// <summary>
    /// Selects the importer family for a source file name. Only the final extension is
    /// considered; anything else, including missing or double-suffixed extensions, is
    /// unsupported (<c>EXT001</c>).
    /// </summary>
    public static ExternalFormatSelectionResult SelectFormat(string fileName, GltfImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ExternalFormatSelectionResult.UnsupportedFormat(fileName ?? "");
        }

        string extension;
        try
        {
            extension = Path.GetExtension(fileName);
        }
        catch (ArgumentException)
        {
            return ExternalFormatSelectionResult.UnsupportedFormat(fileName);
        }

        if (!extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return ExternalFormatSelectionResult.UnsupportedFormat(fileName);
        }

        var effectiveOptions = options ?? GltfImportOptions.Default;
        var selection = new ExternalFormatSelection(
            GltfFormatId,
            effectiveOptions,
            new GltfExternalAssetInspector(effectiveOptions));

        return ExternalFormatSelectionResult.Success(selection);
    }
}
