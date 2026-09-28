using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// WI-C4f-05: locks installed-asset import and sampled bone scales for the DDF-bound
/// skinless proof clips from docs/roadmap/deep-dives/c4f-ee2-scale-animation.md
/// section 2, plus the RoyalChariot non-unit rest proof and the LLA11 singular-rest
/// negative. Interactive: requires EMPIRE_EARTH_II_PATH with zips/graphics.zip.
/// No EE2 asset is copied or committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2KfScaleAnimationImportTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";

    private static readonly (string ModelKey, string KfPath, string[] Targets, float FirstSourceScale, float LastSourceScale)[] Clips =
    [
        ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf",
            ["LandingGear_02", "LandingGear_03"], 1.0f, 0.883883f),
        ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_idle.kf",
            ["LandingGear_02", "LandingGear_03"], 0.891850f, 0.895959f),
        ("ab15_orbital", "Graphics/Vehicles/ab15_orbital_takeoff.kf",
            ["LandingGear_02", "LandingGear_03"], 1.0f, 0.883883f),
        ("afb13_StrikeFighter", "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf",
            ["rearwheeltwo"], 1.0f, 0.665078f),
    ];

    private static (FileSystem FileSystem, NifAnimationImporter Importer) OpenArchive()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");
        }

        var archivePath = Path.Combine(root, ArchiveRelativePath);
        if (!File.Exists(archivePath))
        {
            throw new InvalidOperationException(
                $"Interactive test requires the EE2 graphics archive at '{archivePath}'.");
        }

        return (new ZipFileSystem(archivePath), new NifAnimationImporter());
    }

    private static ImportedAnimation Import(string modelKey, string kfPath)
    {
        var (fileSystem, importer) = OpenArchive();
        using var archive = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return importer.ImportAnimation($"{modelKey}#kf={kfPath}", context)
            ?? throw new InvalidOperationException($"Import returned null for '{modelKey}#kf={kfPath}'.");
    }

    private static ImportedModel ImportModel(string modelKey)
    {
        var (fileSystem, _) = OpenArchive();
        using var archive = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return new NifModelImporter().ImportModel(modelKey, context)
            ?? throw new InvalidOperationException($"Model import returned null for '{modelKey}'.");
    }

    [Fact]
    public void SkinlessClipsEmitScaleChannelsForDocumentedTargets()
    {
        foreach (var (modelKey, kfPath, targets, firstSource, lastSource) in Clips)
        {
            var animation = Import(modelKey, kfPath);
            var model = ImportModel(modelKey);

            var scaleChannels = animation.Channels
                .Where(c => c.ChannelType == ImportedAnimationChannelType.Scale)
                .ToList();
            var names = scaleChannels.Select(c => c.Target.BoneName).ToHashSet();
            Assert.True(
                names.SetEquals(targets),
                $"{kfPath}: expected scale targets [{string.Join(", ", targets)}], got [{string.Join(", ", names)}].");

            foreach (var channel in scaleChannels)
            {
                var rest = Assert.Single(
                    model.Skeleton.Bones,
                    b => string.Equals(b.Name, channel.Target.BoneName, StringComparison.OrdinalIgnoreCase)).BindScale;
                Assert.Equal(0.0, channel.Keyframes[0].Time.TotalSeconds, precision: 3);
                Assert.Equal(
                    animation.Duration.TotalSeconds,
                    channel.Keyframes[^1].Time.TotalSeconds,
                    precision: 3);
                Assert.Equal(firstSource, channel.Keyframes[0].FloatValue * rest, precision: 5);
                Assert.Equal(lastSource, channel.Keyframes[^1].FloatValue * rest, precision: 5);
            }
        }
    }

    [Fact]
    public void TakeoffScalesLandingGearDownAtRuntime()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (instance, imported) = context.Materialize(
            "ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf");
        var animation = ImportedAnimationMaterializer.Materialize(imported);
        var bone = Assert.Single(instance.Model.BoneHierarchy.Bones, b => b.Name == "LandingGear_02");

        PlayTo(instance, animation, 0f);
        Assert.Equal(
            1.0f,
            ScaleOf(instance.RelativeBoneTransforms[bone.Index]),
            precision: 5);

        PlayTo(instance, animation, (float)imported.Duration.TotalSeconds);
        Assert.Equal(
            0.883883f,
            ScaleOf(instance.RelativeBoneTransforms[bone.Index]),
            precision: 5);
    }

    [Fact]
    public void RoyalChariotRestScaleReconstructsAuthoredConstantScale()
    {
        // The model carries a skinned shape on a non-unit-rest bone, which stays
        // C4d-limited for mesh conversion; the skeleton itself converts fine, and
        // the constant 0.85 keys on a 0.8499998 rest produce identity offsets
        // (offset 1) that the converter drops, so the authored scale lives in the
        // bind pose.
        var (fileSystem, _) = OpenArchive();
        using var archive = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        var skeleton = new NifModelImporter().ImportSkeleton("CIV1_RoyalChariot_Egyptian", context)
            ?? throw new InvalidOperationException("Skeleton import returned null.");
        var bone = Assert.Single(skeleton.Bones, b => b.Name == "Bone01");
        Assert.Equal(0.8499998f, bone.BindScale, precision: 6);

        var animation = Import(
            "CIV1_RoyalChariot_Egyptian", "Graphics/Vehicles/CIV1_RoyalChariot_Egyptian_idle01.kf");
        Assert.DoesNotContain(
            animation.Channels,
            c => c.ChannelType == ImportedAnimationChannelType.Scale && c.Target.BoneName == "Bone01");
    }

    [Fact]
    public void FieldArtilleryIdleFailsOnSingularBoomRest()
    {
        var ex = Record.Exception(() =>
            Import("LLA11_FieldArtillery", "Graphics/Vehicles/LLA11_FieldArtillery_idle01.kf"));

        var notSupported = Assert.IsType<NotSupportedException>(ex);
        Assert.Contains("boom", notSupported.Message);
        Assert.Contains("singular rest scale 0", notSupported.Message);
    }

    private static float ScaleOf(in Matrix4x4 matrix)
    {
        Assert.True(Matrix4x4.Decompose(matrix, out var scale, out _, out _));
        Assert.Equal(scale.X, scale.Y, precision: 5);
        Assert.Equal(scale.X, scale.Z, precision: 5);
        return scale.X;
    }

    private sealed class PoseContext : IDisposable
    {
        public TestGraphicsContext Graphics { get; }
        public AssetImportContext ImportContext { get; }

        private PoseContext(TestGraphicsContext graphics, AssetImportContext importContext)
        {
            Graphics = graphics;
            ImportContext = importContext;
        }

        public static PoseContext Create()
        {
            var ee2FileSystem = OpenArchive().FileSystem;
            var graphics = new TestGraphicsContext(ee2FileSystem);
            var importContext = new AssetImportContext(ee2FileSystem, language: null);
            return new PoseContext(graphics, importContext);
        }

        public (ModelInstance Instance, ImportedAnimation Animation) Materialize(string modelKey, string kfPath)
        {
            var modelImporter = new NifModelImporter();
            var importedModel = modelImporter.ImportModel(modelKey, ImportContext)
                ?? throw new InvalidOperationException($"Model import returned null for '{modelKey}'.");
            var model = ImportedModelMaterializer.MaterializeModel(importedModel, Graphics.Context);

            var importedAnimation = Import(modelKey, kfPath);

            var instance = new ModelInstance(model, Graphics.Context);
            instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

            return (instance, importedAnimation);
        }

        public void Dispose()
        {
            Graphics.Dispose();
            ImportContext.FileSystem.Dispose();
        }
    }

    private static void PlayTo(ModelInstance instance, ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, delta));
        }

        instance.Update(new TimeInterval(TimeSpan.FromSeconds(seconds), delta));
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }
}
