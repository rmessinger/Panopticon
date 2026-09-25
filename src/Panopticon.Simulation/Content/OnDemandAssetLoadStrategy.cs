using System;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.W3d;
using Panopticon.Content.Loaders;

namespace Panopticon.Content;

public sealed class OnDemandAssetLoadStrategy
{
    private readonly bool _allowOnDemandLoading;
    private readonly IPathResolver _texturePathResolver;
    private readonly IModelImporter _modelImporter;
    private readonly IAnimationImporter _animationImporter;

    public static readonly OnDemandAssetLoadStrategy None = new OnDemandAssetLoadStrategy(false);

    /// <summary>
    /// Selects the shipped W3D importers for model, hierarchy, and animation loading. Mod
    /// assemblies use this factory so importer contracts stay internal to Panopticon.Simulation.
    /// </summary>
    public static OnDemandAssetLoadStrategy CreateW3d(IPathResolver w3dPathResolver, IPathResolver texturePathResolver)
    {
        return new OnDemandAssetLoadStrategy(
            new W3dModelImporter(w3dPathResolver),
            new W3dAnimationImporter(w3dPathResolver),
            texturePathResolver);
    }

    internal OnDemandAssetLoadStrategy(
        IModelImporter modelImporter,
        IAnimationImporter animationImporter,
        IPathResolver texturePathResolver)
        : this(true)
    {
        _modelImporter = modelImporter ?? throw new ArgumentNullException(nameof(modelImporter));
        _animationImporter = animationImporter ?? throw new ArgumentNullException(nameof(animationImporter));
        _texturePathResolver = texturePathResolver;
    }

    private OnDemandAssetLoadStrategy(bool allowOnDemandLoading)
    {
        _allowOnDemandLoading = allowOnDemandLoading;
    }

    internal IModelImporter ModelImporter => _modelImporter;

    internal IAnimationImporter AnimationImporter => _animationImporter;

    internal OnDemandAnimationLoader CreateAnimationLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandAnimationLoader(_animationImporter)
            : null;
    }

    internal OnDemandModelLoader CreateModelLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandModelLoader(_modelImporter)
            : null;
    }

    internal OnDemandModelBoneHierarchyLoader CreateModelBoneHierarchyLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandModelBoneHierarchyLoader(_modelImporter)
            : null;
    }

    internal OnDemandGuiTextureLoader CreateGuiTextureLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandGuiTextureLoader(false, _texturePathResolver)
            : null;
    }

    internal OnDemandTextureLoader CreateTextureLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandTextureLoader(true, _texturePathResolver)
            : null;
    }

    internal OnDemandAudioFileLoader CreateAudioFileLoader()
    {
        return _allowOnDemandLoading
            ? new OnDemandAudioFileLoader()
            : null;
    }
}
