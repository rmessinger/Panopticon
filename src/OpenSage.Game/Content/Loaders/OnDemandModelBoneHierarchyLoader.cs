using OpenSage.Content.Importing;
using OpenSage.Graphics;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Loads hierarchies through the selected importer, then materializes the imported skeleton.
/// The ModelBoneHierarchies collection this loader serves remains the shared cache for both
/// direct hierarchy loads and model references.
/// </summary>
internal sealed class OnDemandModelBoneHierarchyLoader : IOnDemandAssetLoader<ModelBoneHierarchy>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IModelImporter _modelImporter;

    public OnDemandModelBoneHierarchyLoader(IModelImporter modelImporter)
    {
        _modelImporter = modelImporter;
    }

    public ModelBoneHierarchy Load(string name, AssetLoadContext context)
    {
        var imported = _modelImporter.ImportSkeleton(
            name,
            new AssetImportContext(context.FileSystem, context.Language));

        if (imported is null)
        {
            Logger.Warn("Failed to load hierarchy: " + name);
            return null;
        }

        return ImportedModelMaterializer.MaterializeSkeleton(imported);
    }
}
