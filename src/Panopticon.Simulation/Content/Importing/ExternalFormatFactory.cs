#nullable enable

using System;
using Panopticon.Content.Importing.Gltf;

namespace Panopticon.Content.Importing;

/// <summary>
/// Engine-owned mapping from a public format identifier to internal C1 importer instances.
/// One explicit entry per supported format; no process-global registry and no public
/// exposure of the internal importer interfaces.
/// </summary>
internal static class ExternalFormatFactory
{
    public static bool TryCreateModelImporter(
        string formatId,
        GltfImportOptions options,
        GltfImportDiagnostics? diagnostics,
        out IModelImporter? importer)
    {
        importer = IsGltf(formatId)
            ? new GltfModelImporter(options, diagnostics)
            : null;

        return importer is not null;
    }

    public static bool TryCreateAnimationImporter(
        string formatId,
        GltfImportOptions options,
        GltfImportDiagnostics? diagnostics,
        out IAnimationImporter? importer)
    {
        importer = IsGltf(formatId)
            ? new GltfAnimationImporter(options, diagnostics)
            : null;

        return importer is not null;
    }

    private static bool IsGltf(string formatId)
    {
        return string.Equals(formatId, ExternalAssetInspection.GltfFormatId, StringComparison.Ordinal);
    }
}
