#nullable enable

namespace OpenSage.Content.Importing;

/// <summary>
/// Format-neutral external model inspection over a virtual asset key and import context.
/// Returns null for an invalid key or missing root document; fatal source problems throw
/// the same exceptions the corresponding importer throws.
/// </summary>
public interface IExternalAssetInspector
{
    ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context);
}
