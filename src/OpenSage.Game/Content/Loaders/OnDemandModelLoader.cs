using OpenSage.Content.Importing;
using OpenSage.Graphics;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Loads models through the selected importer, then materializes the imported interchange
/// values. Referenced skeletons delegate to the shared ModelBoneHierarchies cache.
/// </summary>
internal sealed class OnDemandModelLoader : IOnDemandAssetLoader<Model>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IModelImporter _modelImporter;

    public OnDemandModelLoader(IModelImporter modelImporter)
    {
        _modelImporter = modelImporter;
    }

    public Model Load(string name, AssetLoadContext context)
    {
        var imported = _modelImporter.ImportModel(
            name,
            new AssetImportContext(context.FileSystem, context.Language));

        if (imported is null)
        {
            Logger.Warn("Failed to load model: " + name);
            return null;
        }

        if (imported.SkeletonOrigin == ImportedSkeletonOrigin.Referenced)
        {
            var boneHierarchy = context.AssetStore.ModelBoneHierarchies.GetByName(imported.Skeleton.Name);
            if (boneHierarchy is null)
            {
                throw new System.IO.InvalidDataException(
                    $"Model '{imported.Name}' references hierarchy '{imported.Skeleton.Name}', " +
                    "which could not be loaded.");
            }

            return ImportedModelMaterializer.MaterializeModel(imported, boneHierarchy, context);
        }

        return ImportedModelMaterializer.MaterializeModel(imported, context);
    }
}
