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

        Assert.Null(importer.ImportAnimation("missing.gr2", new AssetImportContext(fileSystem, language: null)));
    }

    [Fact]
    public void EmptyKeyReturnsNull()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();

        Assert.Null(importer.ImportAnimation(string.Empty, new AssetImportContext(fileSystem, language: null)));
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

        var animation = importer.ImportAnimation("clip.gr2", new AssetImportContext(fileSystem, language: null));

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
        // File-space +Y motion maps to engine +Z (identity axes, Y-up to Z-up cycle); the
        // curve rises 0..4 m over 1 s, so sampling 0..1 s at 0.25 s steps walks 0..4 m.
        Assert.Equal(5, translationZ.Keyframes.Count);
        Assert.Equal(0f, translationZ.Keyframes[0].FloatValue, 3);
        Assert.Equal(1f, translationZ.Keyframes[1].FloatValue, 3);
        Assert.Equal(2f, translationZ.Keyframes[2].FloatValue, 3);
        Assert.Equal(3f, translationZ.Keyframes[3].FloatValue, 3);
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
    public void CompressedClipFailsWithKeySectionAndType()
    {
        var importer = new GrannyAnimationImporter();
        using var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("clip.gr2", SyntheticGrannyBuilder.BuildWithCompressedSection(2));

        var exception = Assert.Throws<InvalidDataException>(
            () => importer.ImportAnimation("clip.gr2", new AssetImportContext(fileSystem, language: null)));

        Assert.Contains("clip.gr2", exception.Message);
        Assert.Contains("section 0", exception.Message);
        Assert.Contains("Oodle1", exception.Message);
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
        var animation = animationImporter.ImportAnimation(clipPath, context);

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

        var animatedBones = channelTargets.Count;
        Assert.Equal(expectedTracks, animatedBones);
        Assert.Empty(animationImporter.Diagnostics.Warnings);

        _output.WriteLine(
            $"{clipPath}: {animation.Channels.Count} channels over {animatedBones} bones, " +
            $"duration {animation.Duration.TotalSeconds:F4} s");
    }
}
