using System;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.IO;
using Xunit;

namespace OpenSage.Tests.Content;

public sealed class OnDemandAssetLoadStrategyTests
{
    private sealed class FakeModelImporter : IModelImporter
    {
        public string Id => "FakeModel";

        public ImportedModel? ImportModel(string name, AssetImportContext context) => null;

        public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context) => null;
    }

    private sealed class FakeAnimationImporter : IAnimationImporter
    {
        public string Id => "FakeAnimation";

        public ImportedAnimation? ImportAnimation(string name, AssetImportContext context) => null;
    }

    [Fact]
    public void InternalConstructor_RetainsSuppliedImporters()
    {
        var modelImporter = new FakeModelImporter();
        var animationImporter = new FakeAnimationImporter();

        var strategy = new OnDemandAssetLoadStrategy(
            modelImporter,
            animationImporter,
            PathResolvers.W3d,
            PathResolvers.GeneralsTexture);

        Assert.Same(modelImporter, strategy.ModelImporter);
        Assert.Same(animationImporter, strategy.AnimationImporter);
    }

    [Fact]
    public void InternalConstructor_RejectsNullImporters()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OnDemandAssetLoadStrategy(
                null,
                new FakeAnimationImporter(),
                PathResolvers.W3d,
                PathResolvers.GeneralsTexture));

        Assert.Throws<ArgumentNullException>(() =>
            new OnDemandAssetLoadStrategy(
                new FakeModelImporter(),
                null,
                PathResolvers.W3d,
                PathResolvers.GeneralsTexture));
    }

    [Fact]
    public void None_CreatesNoModelHierarchyOrAnimationLoaders()
    {
        Assert.Null(OnDemandAssetLoadStrategy.None.CreateModelLoader());
        Assert.Null(OnDemandAssetLoadStrategy.None.CreateModelBoneHierarchyLoader());
        Assert.Null(OnDemandAssetLoadStrategy.None.CreateAnimationLoader());
        Assert.Null(OnDemandAssetLoadStrategy.None.ModelImporter);
        Assert.Null(OnDemandAssetLoadStrategy.None.AnimationImporter);
    }

    [Fact]
    public void ResolverConstructor_CreatesUnchangedLoaders()
    {
        var strategy = new OnDemandAssetLoadStrategy(
            PathResolvers.W3d,
            PathResolvers.GeneralsTexture);

        Assert.NotNull(strategy.CreateModelLoader());
        Assert.NotNull(strategy.CreateModelBoneHierarchyLoader());
        Assert.NotNull(strategy.CreateAnimationLoader());
        Assert.NotNull(strategy.CreateTextureLoader());
    }
}
