using System;
using System.IO;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.IO;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Card WI-H2a-18: the selected EE2 ground-combat unit imports through the C4d skinned path with all
/// four bound states. Requires EMPIRE_EARTH_II_PATH; no EE2 asset is committed.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2GroundUnitProbeTests(ITestOutputHelper output)
{
    public const string ModelKey = "lhi1_maceman_we";

    public static readonly (string State, string KfPath)[] Clips =
    [
        ("Idle", "Graphics/Humans/sh_maceman_idle01.kf"),
        ("Walk", "Graphics/Humans/sh_maceman_walk01.kf"),
        ("Attack1", "Graphics/Humans/sh_maceman_attack01.kf"),
        ("Death", "Graphics/Humans/sh_maceman_death01.kf"),
    ];

    private static ZipFileSystem OpenGraphics()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH")
            ?? throw new InvalidOperationException("Interactive test requires EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install.");
        return new ZipFileSystem(Path.Combine(root, "zips/graphics.zip"));
    }

    [Theory]
    [InlineData("lhi1_maceman_we")]
    [InlineData("lhi1_maceman_am")]
    [InlineData("lhi1_maceman_me")]
    [InlineData("lhi1_maceman_fe")]
    public void RegionVariantImportStatus(string key)
    {
        using var fileSystem = OpenGraphics();
        var context = new AssetImportContext(fileSystem, language: null);

        try
        {
            var model = new NifModelImporter().ImportModel(key, context);
            output.WriteLine($"{key}: {(model is null ? "null" : $"{model.Skeleton.Bones.Count} bones, {model.SubObjects.Count} sub-objects")}");
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            output.WriteLine($"{key}: rejected: {exception.Message}");
        }
    }

    [Fact]
    public void SelectedModelAndFourClipsImport()
    {
        using var fileSystem = OpenGraphics();
        var context = new AssetImportContext(fileSystem, language: null);

        var model = new NifModelImporter().ImportModel(ModelKey, context);
        Assert.NotNull(model);
        Assert.Contains(model.SubObjects, sub => sub.RenderObject is ImportedMesh { IsSkinned: true });

        foreach (var (state, kfPath) in Clips)
        {
            var animation = new NifAnimationImporter().ImportAnimation($"{ModelKey}#kf={kfPath}", context);
            Assert.NotNull(animation);
            Assert.True(animation.Duration > TimeSpan.Zero, $"{state} has no duration.");
            output.WriteLine($"{state}: {animation.Duration.TotalSeconds:0.###} s, {animation.Channels.Count} channels");
        }
    }
}
