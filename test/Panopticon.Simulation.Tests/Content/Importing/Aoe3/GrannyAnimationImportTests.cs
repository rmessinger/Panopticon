using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.Tests.Content.Importing.Aoe3;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public class GrannyAnimationImportTests
{
    private const string MarchRiflemanPath =
        @"Art\animation_library\range\march_rifleman.gr2";

    private const string VolleyStandingFirePath =
        @"Art\animation_library\range\volley\volley_standing_fire.gr2";

    private const string MusketeerPath = @"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2";

    private readonly ITestOutputHelper _output;

    public GrannyAnimationImportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MissingKeyReturnsNull()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();

        Assert.Null(importer.ImportAnimation("missing.gr2", new AssetImportContext(fileSystem, language: null), CreateSkeleton()));
    }

    [Fact]
    public void EmptyKeyReturnsNull()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();

        Assert.Null(importer.ImportAnimation(string.Empty, new AssetImportContext(fileSystem, language: null), CreateSkeleton()));
    }

    /// <summary>
    /// A skeleton whose single bone carries the identity bind, so absolute-clip-to-offset
    /// conversion is the identity and channel values equal the sampled file values.
    /// </summary>
    private static ImportedSkeleton CreateSkeleton()
    {
        return new ImportedSkeleton(
            "TestSkeleton",
            [new ImportedBone("Bip01_Test", -1, Vector3.Zero, Quaternion.Identity)]);
    }

    [Fact]
    public void ImporterIdIsGr2()
    {
        Assert.Equal("GR2", new GrannyAnimationImporter().Id);
    }

    [Fact]
    public void SyntheticClipSamplesToLinearChannels()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("clip.gr2", SyntheticGrannyBuilder.BuildAnimationClip());

        var animation = importer.ImportAnimation("clip.gr2", new AssetImportContext(fileSystem, language: null), CreateSkeleton());

        Assert.NotNull(animation);
        Assert.Equal("clip.gr2", animation!.Name);
        Assert.Equal(TimeSpan.FromSeconds(1.0), animation.Duration);
        Assert.Empty(importer.Diagnostics.Warnings);

        // One track with animated position and constant rotation: X/Y/Z channels plus one
        // quaternion channel; the empty scale-shear curve adds nothing. The track name
        // normalizes "Bip01 Test" to the skeleton naming "Bip01_Test".
        Assert.Equal(4, animation.Channels.Count);
        foreach (var channel in animation.Channels)
        {
            Assert.Equal("Bip01_Test", channel.Target.BoneName);
        }

        var translationZ = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationZ);
        // File-space +Y motion maps to engine +Z (identity axes, Y-up to Z-up cycle). The
        // curve is a quadratic B-spline with controls (0,2,0),(0,2t,0)-style rise 0..4 m
        // over knots 0/0.5/1: De Boor over the full clamped vector lags the control
        // polygon mid-span and hits the middle control at the middle knot, so sampling
        // 0..1 s at 0.25 s steps walks 0/0.25/1/2.25/4 m.
        Assert.Equal(5, translationZ.Keyframes.Count);
        Assert.Equal(0f, translationZ.Keyframes[0].FloatValue, 3);
        Assert.Equal(0.25f, translationZ.Keyframes[1].FloatValue, 3);
        Assert.Equal(1f, translationZ.Keyframes[2].FloatValue, 3);
        Assert.Equal(2.25f, translationZ.Keyframes[3].FloatValue, 3);
        Assert.Equal(4f, translationZ.Keyframes[4].FloatValue, 3);
        Assert.Equal(TimeSpan.FromSeconds(0.75), translationZ.Keyframes[3].Time);

        var translationX = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationX);
        Assert.All(translationX.Keyframes, keyframe => Assert.Equal(0f, keyframe.FloatValue, 3));

        var rotation = animation.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.Quaternion);
        Assert.All(
            rotation.Keyframes,
            keyframe => Assert.Equal(0f, MathF.Abs(Quaternion.Dot(keyframe.QuaternionValue, Quaternion.Identity) - 1f), 4));
    }

    [Fact]
    public void AbsoluteClipMatchingBindConvertsToIdentityOffsets()
    {
        // Granny clips store absolute local transforms. A clip whose sampled poses equal
        // the bone's bind must convert to identity offsets: channels near zero/identity,
        // keeping the runtime's offset-on-bind composition at the bind pose.
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();
        var bindRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var bindTranslation = new Vector3(1f, 2f, 3f);
        fileSystem.AddFile(
            "clip.gr2",
            SyntheticGrannyBuilder.BuildAnimationClip(
                [
                    bindTranslation.X, bindTranslation.Y, bindTranslation.Z,
                    bindTranslation.X, bindTranslation.Y, bindTranslation.Z,
                    bindTranslation.X, bindTranslation.Y, bindTranslation.Z,
                ],
                [bindRotation.X, bindRotation.Y, bindRotation.Z, bindRotation.W]));

        // Bone bind fields are engine space (GrannySkeletonConverter output): derive them
        // from the file-space pose through the same axis conversion the clip samples take.
        var axes = new GrannyAxisSystem(1f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var engineBind = axes.ToEngineMatrix(
            Matrix4x4.CreateFromQuaternion(bindRotation) * Matrix4x4.CreateTranslation(bindTranslation));
        Matrix4x4.Decompose(engineBind, out _, out var engineBindRotation, out var engineBindTranslation);

        var animation = importer.ImportAnimation(
            "clip.gr2",
            new AssetImportContext(fileSystem, language: null),
            new ImportedSkeleton(
                "TestSkeleton",
                [new ImportedBone("Bip01_Test", -1, engineBindTranslation, engineBindRotation)]));

        Assert.NotNull(animation);
        Assert.Empty(importer.Diagnostics.Warnings);
        Assert.Equal(4, animation!.Channels.Count);
        foreach (var channel in animation.Channels)
        {
            if (channel.ChannelType == ImportedAnimationChannelType.Quaternion)
            {
                Assert.All(
                    channel.Keyframes,
                    keyframe => Assert.Equal(
                        0f, MathF.Abs(Quaternion.Dot(keyframe.QuaternionValue, Quaternion.Identity) - 1f), 4));
            }
            else
            {
                Assert.All(
                    channel.Keyframes,
                    keyframe => Assert.Equal(0f, keyframe.FloatValue, 3));
            }
        }
    }

    [Fact]
    public void ClipUnitScaleMismatchIsCalibratedAgainstTheSkeleton()
    {
        // AoE3 clips and models disagree about units per meter (1.5625 vs 0.6152, exactly
        // 2.54). The importer derives the ratio from matched bind/clip translations and
        // rescales clip translations, so a clip 2x larger than the skeleton still yields
        // identity offsets.
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();
        var bindRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var bindTranslation = new Vector3(1f, 2f, 3f);
        fileSystem.AddFile(
            "clip.gr2",
            SyntheticGrannyBuilder.BuildAnimationClip(
                [
                    2f * bindTranslation.X, 2f * bindTranslation.Y, 2f * bindTranslation.Z,
                    2f * bindTranslation.X, 2f * bindTranslation.Y, 2f * bindTranslation.Z,
                    2f * bindTranslation.X, 2f * bindTranslation.Y, 2f * bindTranslation.Z,
                ],
                [bindRotation.X, bindRotation.Y, bindRotation.Z, bindRotation.W]));

        var axes = new GrannyAxisSystem(1f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var engineBind = axes.ToEngineMatrix(
            Matrix4x4.CreateFromQuaternion(bindRotation) * Matrix4x4.CreateTranslation(bindTranslation));
        Matrix4x4.Decompose(engineBind, out _, out var engineBindRotation, out var engineBindTranslation);

        var animation = importer.ImportAnimation(
            "clip.gr2",
            new AssetImportContext(fileSystem, language: null),
            new ImportedSkeleton(
                "TestSkeleton",
                [new ImportedBone("Bip01_Test", -1, engineBindTranslation, engineBindRotation)]));

        Assert.NotNull(animation);
        Assert.Empty(importer.Diagnostics.Warnings);
        Assert.Contains(importer.Diagnostics.Notes, note => note.Contains("0.5"));
        Assert.Equal(4, animation!.Channels.Count);
        foreach (var channel in animation.Channels)
        {
            if (channel.ChannelType == ImportedAnimationChannelType.Quaternion)
            {
                Assert.All(
                    channel.Keyframes,
                    keyframe => Assert.Equal(
                        0f, MathF.Abs(Quaternion.Dot(keyframe.QuaternionValue, Quaternion.Identity) - 1f), 4));
            }
            else
            {
                Assert.All(
                    channel.Keyframes,
                    keyframe => Assert.Equal(0f, keyframe.FloatValue, 3));
            }
        }
    }

    [Fact]
    public void UnsupportedCompressionFailsWithKeySectionAndType()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("clip.gr2", SyntheticGrannyBuilder.BuildWithCompressedSection(99));

        var exception = Assert.Throws<InvalidDataException>(
            () => importer.ImportAnimation("clip.gr2", new AssetImportContext(fileSystem, language: null), CreateSkeleton()));

        Assert.Contains("clip.gr2", exception.Message);
        Assert.Contains("section 0", exception.Message);
        Assert.Contains("compression 99", exception.Message);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void MarchRiflemanImportsWithNoMissingBoneTargets()
    {
        AssertClipImportsOnMusketeerSkeleton(MarchRiflemanPath, expectedTracks: 24, expectedDurationSeconds: 0.8f);
    }

    [Fact]
    [Trait("Category", "Interactive")]
    public void VolleyStandingFireImportsWithNoMissingBoneTargets()
    {
        AssertClipImportsOnMusketeerSkeleton(VolleyStandingFirePath, expectedTracks: 32, expectedDurationSeconds: 0.958333f);
    }

    private void AssertClipImportsOnMusketeerSkeleton(string clipPath, int expectedTracks, float expectedDurationSeconds)
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();
        var animationImporter = new GrannyAnimationImporter();
        var modelImporter = new GrannyModelImporter();
        var context = new AssetImportContext(fileSystem, language: null);

        var model = modelImporter.ImportModel(MusketeerPath, context);
        var animation = animationImporter.ImportAnimation(clipPath, context, model!.Skeleton);

        Assert.NotNull(animation);
        Assert.NotNull(model);
        Assert.True(
            Math.Abs((animation!.Duration - TimeSpan.FromSeconds(expectedDurationSeconds)).TotalMilliseconds) < 5,
            $"duration {animation.Duration.TotalMilliseconds} ms");

        var boneNames = model!.Skeleton.Bones.Select(b => b.Name).ToHashSet();
        var channelTargets = animation.Channels
            .Select(channel => channel.Target.BoneName)
            .ToHashSet();

        // Shared clips animate more bones than any one model carries (ponytails, toe bones,
        // attachment launch points), so extra clip tracks are expected, and a clip may leave
        // individual bones at their bind pose: march keeps the neck static.
        var unanimated = boneNames
            .Where(name => name.StartsWith("Bip01", StringComparison.Ordinal) && !channelTargets.Contains(name))
            .ToList();
        Assert.All(unanimated, name => Assert.Equal("Bip01_Neck", name));
        _output.WriteLine($"model bones left at bind pose: {(unanimated.Count == 0 ? "none" : string.Join(", ", unanimated))}");

        var extras = channelTargets.Where(name => !boneNames.Contains(name)).OrderBy(name => name).ToList();
        _output.WriteLine($"clip targets beyond the model skeleton: {string.Join(", ", extras)}");

        // Tracks beyond the skeleton bind to nothing and warn by design; those warnings quote
        // the raw track name (spaces intact), so match extras either way they are spelled.
        var unmatchedWarnings = animationImporter.Diagnostics.Warnings
            .Where(w => w.StartsWith("Granny track ", StringComparison.Ordinal)
                && w.Contains("has no bone in the target skeleton", StringComparison.Ordinal))
            .ToList();
        Assert.All(unmatchedWarnings, w => Assert.Contains(
            extras,
            extra => w.Contains(extra, StringComparison.Ordinal)
                || w.Contains(extra.Replace('_', ' '), StringComparison.Ordinal)));
        Assert.Equal(extras.Count, unmatchedWarnings.Count);
        Assert.Equal(animationImporter.Diagnostics.Warnings.Count, unmatchedWarnings.Count);

        var animatedBones = channelTargets.Count;
        Assert.Equal(expectedTracks, animatedBones);

        _output.WriteLine(
            $"{clipPath}: {animation.Channels.Count} channels over {animatedBones} bones, " +
            $"duration {animation.Duration.TotalSeconds:F4} s");
    }
}
