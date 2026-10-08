using System;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// The registered implementations of the model-import seam
/// (<c>model-import</c>). Settings are <see cref="AssetLoadStrategySelection"/> values whose
/// construction delegates to the C1-owned surfaces (`OnDemandAssetLoadStrategy.CreateW3d` and
/// `ExternalFormatFactory`), so no importer interface or format dispatch is duplicated here.
/// W3D stays the reference default.
/// </summary>
public static class ModelImportExperimentImplementations
{
    public const string SeamKey = "model-import";

    public const string W3dId = "w3d";

    public const string GltfId = "gltf";

    public static ImplementationDescriptor W3d()
    {
        return new ImplementationDescriptor(
            SeamKey,
            W3dId,
            "W3D model/animation import (reference)",
            "1",
            () => new AssetLoadStrategySelection(OnDemandAssetLoadStrategy.CreateW3d));
    }

    public static ImplementationDescriptor Gltf()
    {
        return new ImplementationDescriptor(
            SeamKey,
            GltfId,
            "glTF 2.0 model/animation import (C4)",
            "1",
            () => new AssetLoadStrategySelection((_, texturePathResolver) => CreateGltf(texturePathResolver)));
    }

    private static OnDemandAssetLoadStrategy CreateGltf(IPathResolver texturePathResolver)
    {
        if (!ExternalFormatFactory.TryCreateModelImporter(
                ExternalAssetInspection.GltfFormatId, new GltfImportOptions(), null, out var modelImporter)
            || modelImporter is null
            || !ExternalFormatFactory.TryCreateAnimationImporter(
                ExternalAssetInspection.GltfFormatId, new GltfImportOptions(), null, out var animationImporter)
            || animationImporter is null)
        {
            throw new InvalidOperationException("The glTF importers are not available through the C1 factory.");
        }

        return new OnDemandAssetLoadStrategy(modelImporter, animationImporter, texturePathResolver);
    }
}

/// <summary>
/// One model-import selection: a pure, deferred build of the asset-load strategy for a run,
/// given the game's model and texture path resolvers. Keeping the resolvers as inputs lets
/// definitions and registry entries stay installation-free.
/// </summary>
public sealed record AssetLoadStrategySelection(
    Func<IPathResolver, IPathResolver, OnDemandAssetLoadStrategy> CreateStrategy);
