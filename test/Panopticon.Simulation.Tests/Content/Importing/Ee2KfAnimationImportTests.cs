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
/// WI-C4c-05: locks installed-asset import, C2 binding, and sampled bone poses for the
/// seven DDF-bound proof clips from docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md
/// section 2, plus the unreferenced negative clip. Interactive: requires
/// EMPIRE_EARTH_II_PATH with zips/graphics.zip. No EE2 asset is copied or committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2KfAnimationImportTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";

    private static readonly (string ModelKey, string KfPath, double DurationSeconds, string[] Targets, string[] TranslationTargets, int ChannelCount)[] Clips =
    [
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf", 2.1667,
            ["R_OAR02", "R_OAR03", "R_OAR04", "R_OAR05", "R_OAR06", "R_OAR13", "R_OAR14", "R_OAR15", "R_OAR16", "R_OAR17", "R_OAR18", "R_OAR19", "ROOT", "ROOT NonAccum"],
            ["ROOT", "ROOT NonAccum"], 20),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 2.1667,
            ["R_OAR02", "R_OAR03", "R_OAR04", "R_OAR05", "R_OAR06", "R_OAR13", "R_OAR14", "R_OAR15", "R_OAR16", "R_OAR17", "R_OAR18", "R_OAR19", "ROOT", "ROOT NonAccum"],
            ["ROOT", "ROOT NonAccum"], 20),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_idle.kf", 1.0,
            ["root", "root NonAccum", "prop_01", "prop_02"],
            ["root", "root NonAccum", "prop_01", "prop_02"], 16),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1.0,
            ["root", "root NonAccum", "prop_01", "prop_02"],
            ["root", "root NonAccum", "prop_01", "prop_02"], 16),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_attack.kf", 1.0,
            ["root", "root NonAccum", "prop_01", "prop_02"],
            ["root", "root NonAccum", "prop_01", "prop_02"], 16),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 3.0,
            ["root", "root NonAccum", "prop_01", "prop_02"],
            ["root", "root NonAccum", "prop_01", "prop_02"], 16),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_death.kf", 1.0,
            ["root", "root NonAccum", "prop_01", "prop_02"],
            ["root", "root NonAccum", "prop_01", "prop_02"], 16),
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
        using var _ = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return importer.ImportAnimation($"{modelKey}#kf={kfPath}", context)
            ?? throw new InvalidOperationException($"Import returned null for '{modelKey}#kf={kfPath}'.");
    }

    [Fact]
    public void AllSevenClipsImportWithDocumentedDescriptors()
    {
        foreach (var (modelKey, kfPath, durationSeconds, targets, translationTargets, channelCount) in Clips)
        {
            var animation = Import(modelKey, kfPath);

            Assert.Equal(
                Path.GetFileNameWithoutExtension(kfPath),
                animation.Name);

            Assert.Equal(
                durationSeconds,
                animation.Duration.TotalSeconds,
                precision: 3);

            Assert.Equal(channelCount, animation.Channels.Count);

            var boneNames = animation.Channels
                .Select(c => c.Target.BoneName)
                .ToHashSet();
            Assert.True(boneNames.SetEquals(targets),
                $"{kfPath}: expected targets [{string.Join(", ", targets)}], got [{string.Join(", ", boneNames)}].");

            // Every target carries exactly one quaternion rotation channel. The accum-root
            // and (on the Bomber) the prop targets also carry X/Y/Z translation channels;
            // the unit scale keys validate and drop.
            foreach (var target in targets)
            {
                var targetChannels = animation.Channels
                    .Where(c => c.Target.BoneName == target)
                    .ToList();
                var expectedCount = translationTargets.Contains(target) ? 4 : 1;
                Assert.Equal(expectedCount, targetChannels.Count);

                Assert.Single(targetChannels, c => c.ChannelType == ImportedAnimationChannelType.Quaternion);
                if (expectedCount == 4)
                {
                    Assert.Single(targetChannels, c => c.ChannelType == ImportedAnimationChannelType.TranslationX);
                    Assert.Single(targetChannels, c => c.ChannelType == ImportedAnimationChannelType.TranslationY);
                    Assert.Single(targetChannels, c => c.ChannelType == ImportedAnimationChannelType.TranslationZ);
                }
            }
        }
    }

    [Fact]
    public void UnreferencedBomberClipBindsMatchedTargetsAndSkipsForeignBones()
    {
        // The stock bomber clip addresses "prop_1", a bone only sibling models
        // carry (the same EE2 skeleton-superset pattern as the epoch-15 death
        // clips); the matched targets bind and the foreign ones are skipped.
        var animation = Import("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber.kf");

        Assert.NotNull(animation);
        Assert.True(animation!.Channels.Count > 0, "expected the matched targets to bind channels.");
        Assert.DoesNotContain(animation.Channels, c => c.Target.BoneName == "prop_1");
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
    public void GalleyIdleOffsetsAreIdentity()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (instance, imported) = context.Materialize("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf");

        PlayTo(instance, ImportedAnimationMaterializer.Materialize(imported), (float)imported.Duration.TotalSeconds / 2f);

        foreach (var boneInstance in instance.ModelBoneInstances)
        {
            Assert.True(
                Quaternion.Dot(boneInstance.AnimatedOffset.Rotation, Quaternion.Identity) > 0.9999995f,
                "Idle rotation offsets must be identity within 1e-4.");
            Assert.True(
                boneInstance.AnimatedOffset.Translation.Length() < 1e-4f,
                "Idle translation offsets must be identity within 1e-4.");
        }
    }

    [Fact]
    public void BomberWalkTurnsPropNinetyDegreesInOneTwelfthSecond()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (instance, imported) = context.Materialize("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf");
        var animation = ImportedAnimationMaterializer.Materialize(imported);

        PlayTo(instance, animation, 0f);
        var atStart = RotationOf(BoneWorld(instance, "prop_01"));

        PlayTo(instance, animation, 1f / 12f);
        var after = RotationOf(BoneWorld(instance, "prop_01"));

        var angle = AngleBetween(atStart, after);
        Assert.InRange(
            angle,
            MathF.PI / 2f - 0.5f * MathF.PI / 180f,
            MathF.PI / 2f + 0.5f * MathF.PI / 180f);
    }

    [Fact]
    public void BomberTakeOffStartsAtRestPose()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (instance, imported) = context.Materialize("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf");

        var restWorlds = instance.Model.BoneHierarchy.Bones
            .Select(b => instance.RelativeBoneTransforms[b.Index])
            .ToArray();

        PlayTo(instance, ImportedAnimationMaterializer.Materialize(imported), 0f);

        // The flight clips key 'root' to identity while 'root NonAccum' carries the ground
        // pose (deep dive section 2), so the airframe world pose at time 0 equals rest and
        // only the 'root' bone itself differs from its rest local.
        var rootIndex = Array.FindIndex(instance.Model.BoneHierarchy.Bones, b => b.Name == "root");
        for (var i = 0; i < restWorlds.Length; i++)
        {
            if (i == rootIndex)
            {
                Assert.True(
                    Quaternion.Dot(RotationOf(restWorlds[i]), RotationOf(instance.RelativeBoneTransforms[i])) < 0.9999995f,
                    "root must be keyed away from its rest rotation at TakeOff time 0.");
                continue;
            }

            Assert.True(
                Quaternion.Dot(RotationOf(restWorlds[i]), RotationOf(instance.RelativeBoneTransforms[i])) > 0.9999995f,
                $"Bone {i} rotation at TakeOff time 0 must equal rest.");
            Assert.True(
                (TranslationOf(restWorlds[i]) - TranslationOf(instance.RelativeBoneTransforms[i])).Length() < 1e-4f,
                $"Bone {i} translation at TakeOff time 0 must equal rest.");
        }
    }

    [Fact]
    public void BomberTakeOffFlightPoseMatchesWalkPose()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = PoseContext.Create();
        var (walkInstance, walkImported) = context.Materialize("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf");
        var (takeOffInstance, takeOffImported) = context.Materialize("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf");

        PlayTo(walkInstance, ImportedAnimationMaterializer.Materialize(walkImported), 0f);
        PlayTo(takeOffInstance, ImportedAnimationMaterializer.Materialize(takeOffImported), 1.333f);

        var walk = BoneWorld(walkInstance, "root NonAccum");
        var takeOff = BoneWorld(takeOffInstance, "root NonAccum");

        Assert.True(
            Quaternion.Dot(RotationOf(walk), RotationOf(takeOff)) > 0.9999995f,
            "root NonAccum rotation at TakeOff 1.333 s must equal the Walk pose.");
        Assert.True(
            (TranslationOf(walk) - TranslationOf(takeOff)).Length() < 1e-4f,
            "root NonAccum translation at TakeOff 1.333 s must equal the Walk pose.");
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
