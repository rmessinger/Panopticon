using OpenSage.Content.Importing;
using OpenSage.Graphics.Animation;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Loads animations through the selected importer, then materializes the imported animation.
/// </summary>
internal sealed class OnDemandAnimationLoader : IOnDemandAssetLoader<ModelAnimation>
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IAnimationImporter _animationImporter;

    public OnDemandAnimationLoader(IAnimationImporter animationImporter)
    {
        _animationImporter = animationImporter;
    }

    public ModelAnimation Load(string key, AssetLoadContext context)
    {
        var imported = _animationImporter.ImportAnimation(
            key,
            new AssetImportContext(context.FileSystem, context.Language));

        if (imported is null)
        {
            Logger.Warn("Failed to load animation: " + key);
            return null;
        }

        return ImportedAnimationMaterializer.Materialize(imported);
    }
}
