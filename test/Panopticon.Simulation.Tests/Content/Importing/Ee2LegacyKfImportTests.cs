using System;
using System.Collections.Generic;
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
/// WI-C4i-04: locks installed-asset import, C2 binding, and sampled bone poses for the
/// four DDF-bound rigid 10.0.1.0 proof clips from
/// docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md section 2. Interactive:
/// requires EMPIRE_EARTH_II_PATH with zips/graphics.zip. No EE2 asset is copied or
/// committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2LegacyKfImportTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";

    private static readonly (string ModelKey, string KfPath, double DurationSeconds, string[] Targets)[] Clips =
    [
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_idle.kf", 1.5667,
            ["wheel_F", "Wheel1", "wheel2", "wheel3", "wheel4", "wheel_r", "Dummy01", "Dummy01 NonAccum"]),
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_walk.kf", 1.5667,
            ["wheel_F", "Wheel1", "wheel2", "wheel3", "wheel4", "wheel_r", "Dummy01", "Dummy01 NonAccum"]),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_open.kf", 0.9667,
            ["bld_palisade1_gate2", "bld_palisade1_gate3", "ROOT", "ROOT NonAccum"]),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_close.kf", 0.9667,
            ["bld_palisade1_gate2", "bld_palisade1_gate3", "ROOT", "ROOT NonAccum"]),
    ];

    private static readonly string[] GuntankWheels = ["wheel_F", "Wheel1", "wheel2", "wheel3", "wheel4", "wheel_r"];

    private static readonly string[] PalisadeLeaves = ["bld_palisade1_gate2", "bld_palisade1_gate3"];

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
        using var _ = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return importer.ImportAnimation($"{modelKey}#kf={kfPath}", context)
            ?? throw new InvalidOperationException($"Import returned null for '{modelKey}#kf={kfPath}'.");
    }

    [Fact]
    public void AllFourLegacyClipsImportWithDocumentedDescriptors()
    {
        foreach (var (modelKey, kfPath, durationSeconds, targets) in Clips)
        {
            var animation = Import(modelKey, kfPath);

            Assert.Equal(
                Path.GetFileNameWithoutExtension(kfPath),
                animation.Name);

            Assert.Equal(
                durationSeconds,
                animation.Duration.TotalSeconds,
                precision: 3);

            var boneNames = animation.Channels
                .Select(c => c.Target.BoneName)
                .ToHashSet();
            Assert.True(boneNames.SetEquals(targets),
                $"{kfPath}: expected targets [{string.Join(", ", targets)}], got [{string.Join(", ", boneNames)}].");

            // Targets carry at most one quaternion rotation channel; translation arrives as
            // whole X/Y/Z triplets when a target translates. The palisade gate leaves are
            // translation-only; the guntank wheels are rotation-only; the accumulation
            // roots carry both.
            foreach (var target in targets)
            {
                var targetChannels = animation.Channels
                    .Where(c => c.Target.BoneName == target)
                    .ToList();

                Assert.True(targetChannels.Count(c => c.ChannelType == ImportedAnimationChannelType.Quaternion) <= 1,
                    $"{kfPath}: target '{target}' has more than one quaternion channel.");

                var translationAxes = targetChannels
                    .Where(c => c.ChannelType is ImportedAnimationChannelType.TranslationX
                        or ImportedAnimationChannelType.TranslationY
                        or ImportedAnimationChannelType.TranslationZ)
                    .ToList();
                Assert.True(translationAxes.Count is 0 or 3,
                    $"{kfPath}: target '{target}' has {translationAxes.Count} translation axes.");
            }
        }
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

    private static Matrix4x4 BoneWorld(ModelInstance instance, string boneName)
    {
        var bone = Assert.Single(instance.Model.BoneHierarchy.Bones, b => b.Name == boneName);
        return instance.RelativeBoneTransforms[bone.Index];
    }

    private static Quaternion RotationOf(in Matrix4x4 matrix)
    {
        return Quaternion.CreateFromRotationMatrix(matrix);
    }

    private static Vector3 TranslationOf(in Matrix4x4 matrix)
    {
        return new Vector3(matrix.M41, matrix.M42, matrix.M43);
    }

    private static float AngleBetween(Quaternion a, Quaternion b)
    {
        var dot = MathF.Abs(Quaternion.Dot(a, b));
        return 2f * MathF.Acos(MathF.Min(1f, dot));
    }

    [Fact]
    public void EveryLegacyTargetBindsToExactlyOneRuntimeBone()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var models = new Dictionary<string, ModelInstance>
        {
            ["LLM11_guntank"] = context.Materialize("LLM11_guntank", Clips[0].KfPath).Instance,
            ["bld_palisade1_gate"] = context.Materialize("bld_palisade1_gate", Clips[2].KfPath).Instance,
        };

        foreach (var (modelKey, _, _, targets) in Clips)
        {
            var bones = models[modelKey].Model.BoneHierarchy.Bones;
            foreach (var target in targets)
            {
                Assert.Single(bones, b => string.Equals(b.Name, target, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void WalkMovesGuntankWheelsAwayFromIdleAtMidClip()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (idleInstance, idleImported) = context.Materialize("LLM11_guntank", Clips[0].KfPath);
        var (walkInstance, walkImported) = context.Materialize("LLM11_guntank", Clips[1].KfPath);

        var mid = (float)idleImported.Duration.TotalSeconds / 2f;
        PlayTo(idleInstance, ImportedAnimationMaterializer.Materialize(idleImported), mid);
        PlayTo(walkInstance, ImportedAnimationMaterializer.Materialize(walkImported), mid);

        var maxAngle = 0f;
        foreach (var wheel in GuntankWheels)
        {
            var idleRotation = RotationOf(BoneWorld(idleInstance, wheel));
            var walkRotation = RotationOf(BoneWorld(walkInstance, wheel));
            maxAngle = MathF.Max(maxAngle, AngleBetween(idleRotation, walkRotation));
        }

        Assert.True(maxAngle > MathF.PI / 180f,
            $"At least one wheel must differ between Idle and Walk at mid-clip; max angle was {maxAngle} rad.");
    }

    [Fact]
    public void PalisadeOpenAndCloseMoveLeavesInOppositeDirections()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (openInstance, openImported) = context.Materialize("bld_palisade1_gate", Clips[2].KfPath);
        var (closeInstance, closeImported) = context.Materialize("bld_palisade1_gate", Clips[3].KfPath);

        var openAnimation = ImportedAnimationMaterializer.Materialize(openImported);
        var closeAnimation = ImportedAnimationMaterializer.Materialize(closeImported);
        var end = (float)openImported.Duration.TotalSeconds;

        PlayTo(openInstance, openAnimation, 0f);
        PlayTo(closeInstance, closeAnimation, 0f);

        // Capture both start poses before advancing either instance.
        var openStarts = PalisadeLeaves.ToDictionary(leaf => leaf, leaf => TranslationOf(BoneWorld(openInstance, leaf)));
        var closeStarts = PalisadeLeaves.ToDictionary(leaf => leaf, leaf => TranslationOf(BoneWorld(closeInstance, leaf)));

        PlayTo(openInstance, openAnimation, end);
        PlayTo(closeInstance, closeAnimation, end);

        foreach (var leaf in PalisadeLeaves)
        {
            var openDelta = TranslationOf(BoneWorld(openInstance, leaf)) - openStarts[leaf];
            var closeDelta = TranslationOf(BoneWorld(closeInstance, leaf)) - closeStarts[leaf];

            Assert.True(openDelta.Length() > 1e-3f,
                $"Leaf '{leaf}' must translate over Open; moved {openDelta.Length()}.");

            Assert.True(closeDelta.Length() > 1e-3f,
                $"Leaf '{leaf}' must translate over Close; moved {closeDelta.Length()}.");

            Assert.True(
                Vector3.Dot(openDelta, closeDelta) < 0f,
                $"Leaf '{leaf}' Open delta {openDelta} and Close delta {closeDelta} must point in opposite directions.");
        }
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
