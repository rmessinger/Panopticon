using System;
using System.Collections.Generic;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.IO;
using OpenSage.Mods.BuiltIn;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content;

/// <summary>
/// Locks the WI-C1-13 loader cutover: on-demand model, hierarchy, and animation loading goes
/// through the selected importers exactly once per cache miss, caches the runtime asset, and
/// returns null for top-level missing assets.
/// </summary>
public sealed class OnDemandImporterWiringTests
{
    private sealed class FakeModelImporter : IModelImporter
    {
        public int ModelImportCalls;
        public int SkeletonImportCalls;

        public IReadOnlyList<string> RequestedModelNames { get; } = new List<string>();

        private readonly ImportedModel? _model;
        private readonly ImportedSkeleton? _skeleton;

        public FakeModelImporter(ImportedModel? model, ImportedSkeleton? skeleton = null)
        {
            _model = model;
            _skeleton = skeleton;
        }

        public string Id => "FakeModel";

        public ImportedModel? ImportModel(string name, AssetImportContext context)
        {
            ModelImportCalls++;
            ((List<string>)RequestedModelNames).Add(name);
            return _model;
        }

        public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
        {
            SkeletonImportCalls++;
            return _skeleton;
        }
    }

    private sealed class FakeAnimationImporter : IAnimationImporter
    {
        public int ImportCalls;

        private readonly ImportedAnimation? _animation;

        public FakeAnimationImporter(ImportedAnimation? animation = null)
        {
            _animation = animation;
        }

        public string Id => "FakeAnimation";

        public ImportedAnimation? ImportAnimation(string name, AssetImportContext context)
        {
            ImportCalls++;
            return _animation;
        }
    }

    private sealed class WiringTestContext : IDisposable
    {
        public AssetStore AssetStore { get; }

        public WiringTestContext(FakeModelImporter modelImporter, FakeAnimationImporter animationImporter)
        {
            AssetStore = new AssetStore(
                SageGame.CncGenerals,
                new MemoryFileSystem(),
                "english",
                null,
                null,
                null,
                null,
                new OnDemandAssetLoadStrategy(modelImporter, animationImporter, PathResolvers.GeneralsTexture));

            AssetStore.PushScope();
        }

        public void Dispose()
        {
        }
    }

    private static ImportedSkeleton CreateSkeleton(string name = "TestSkeleton")
    {
        return new ImportedSkeleton(
            name,
            new[] { new ImportedBone("Root", -1, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity) });
    }

    private static ImportedAnimation CreateAnimation()
    {
        return new ImportedAnimation(
            "Skeleton.Run",
            TimeSpan.FromSeconds(1),
            Array.Empty<ImportedAnimationChannel>());
    }

    private static ImportedModel CreateModel(ImportedSkeletonOrigin skeletonOrigin)
    {
        return new ImportedModel(
            "TestModel",
            new ImportedSkeleton(
                "TestSkeleton",
                new[] { new ImportedBone("Root", -1, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity) }),
            Array.Empty<ImportedSubObject>(),
            skeletonOrigin);
    }

    [Fact]
    public void Model_CacheMissImportsOnceThenCaches()
    {
        var modelImporter = new FakeModelImporter(CreateModel(ImportedSkeletonOrigin.Embedded));
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        var first = context.AssetStore.Models.GetByName("TestModel");
        var second = context.AssetStore.Models.GetByName("TestModel");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, modelImporter.ModelImportCalls);
        Assert.Equal("TestModel", modelImporter.RequestedModelNames[0]);
    }

    [Fact]
    public void Model_TopLevelMissing_ReturnsNull()
    {
        var modelImporter = new FakeModelImporter(model: null);
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        Assert.Null(context.AssetStore.Models.GetByName("Missing"));
        Assert.Equal(1, modelImporter.ModelImportCalls);
    }

    [Fact]
    public void Hierarchy_CacheMissImportsOnceThenCaches()
    {
        var modelImporter = new FakeModelImporter(model: null, skeleton: CreateSkeleton());
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        var first = context.AssetStore.ModelBoneHierarchies.GetByName("TestSkeleton");
        var second = context.AssetStore.ModelBoneHierarchies.GetByName("TestSkeleton");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, modelImporter.SkeletonImportCalls);
    }

    [Fact]
    public void Hierarchy_TopLevelMissing_ReturnsNull()
    {
        var modelImporter = new FakeModelImporter(model: null);
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        Assert.Null(context.AssetStore.ModelBoneHierarchies.GetByName("Missing"));
        Assert.Equal(1, modelImporter.SkeletonImportCalls);
    }

    [Fact]
    public void Animation_CacheMissImportsOnceThenCaches()
    {
        var modelImporter = new FakeModelImporter(model: null);
        var animationImporter = new FakeAnimationImporter(CreateAnimation());
        using var context = new WiringTestContext(modelImporter, animationImporter);

        var first = context.AssetStore.ModelAnimations.GetByName("Skeleton.Run");
        var second = context.AssetStore.ModelAnimations.GetByName("Skeleton.Run");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, animationImporter.ImportCalls);
    }

    [Fact]
    public void Animation_TopLevelMissing_ReturnsNull()
    {
        var modelImporter = new FakeModelImporter(model: null);
        var animationImporter = new FakeAnimationImporter(animation: null);
        using var context = new WiringTestContext(modelImporter, animationImporter);

        Assert.Null(context.AssetStore.ModelAnimations.GetByName("Skeleton.Missing"));
        Assert.Equal(1, animationImporter.ImportCalls);
    }

    [Fact]
    public void ReferencedSkeleton_DelegatesToHierarchyCache()
    {
        var modelImporter = new FakeModelImporter(CreateModel(ImportedSkeletonOrigin.Referenced), CreateSkeleton());
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        var model = context.AssetStore.Models.GetByName("TestModel");
        Assert.NotNull(model);

        var hierarchy = context.AssetStore.ModelBoneHierarchies.GetByName("TestSkeleton");

        Assert.NotNull(hierarchy);
        Assert.Same(hierarchy, model.BoneHierarchy);
        Assert.Equal(1, modelImporter.ModelImportCalls);
        Assert.Equal(1, modelImporter.SkeletonImportCalls);
    }

    [Fact]
    public void EmbeddedSkeleton_DoesNotTouchHierarchyCache()
    {
        var modelImporter = new FakeModelImporter(CreateModel(ImportedSkeletonOrigin.Embedded));
        var animationImporter = new FakeAnimationImporter();
        using var context = new WiringTestContext(modelImporter, animationImporter);

        var model = context.AssetStore.Models.GetByName("TestModel");

        Assert.NotNull(model);
        Assert.Equal(0, modelImporter.SkeletonImportCalls);
    }
}
