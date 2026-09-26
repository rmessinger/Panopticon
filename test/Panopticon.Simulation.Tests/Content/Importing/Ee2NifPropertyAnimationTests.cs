using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.FileFormats.Nif;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

/// <summary>
/// Locks installed EE2 property-animation clips to the descriptors recorded
/// in docs/roadmap/deep-dives/c4e-ee2-property-animation.md section 9.
/// Interactive: requires EMPIRE_EARTH_II_PATH and zips/graphics.zip.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2NifPropertyAnimationTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";

    private static (AssetImportContext Context, FileSystem FileSystem) OpenArchive()
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

        var fileSystem = new ZipFileSystem(archivePath);
        return (new AssetImportContext(fileSystem, language: null), fileSystem);
    }

    private static ImportedAnimation ImportAnimation(string modelKey, string kfPath)
    {
        var (context, fileSystem) = OpenArchive();
        using var _ = fileSystem;
        return new NifAnimationImporter().ImportAnimation($"{modelKey}#kf={kfPath}", context)
            ?? throw new InvalidOperationException($"Animation import returned null for '{modelKey}#kf={kfPath}'.");
    }

    private static ImportedAnimation ImportFaufishAnimation()
    {
        return ImportAnimation("faufish", "Graphics/Animals/sh_faufish_idle01.kf");
    }

    private static NifFile ReadModel(string key)
    {
        var (context, fileSystem) = OpenArchive();
        using var _ = fileSystem;
        return NifModelImporter.ReadNifFile(key, context)
            ?? throw new InvalidOperationException($"Model read returned null for '{key}'.");
    }

    private static ImportedAnimationChannel SingleChannel(
        ImportedAnimation animation,
        ImportedAnimationChannelType channelType)
    {
        var matches = animation.Channels.Where(c => c.ChannelType == channelType).ToArray();
        Assert.True(matches.Length >= 1, $"Expected a {channelType} channel.");
        return matches[0];
    }

    [Fact]
    public void MarketOpenEmitsStepVisibilityForOpenNodes()
    {
        var animation = ImportAnimation(
            "bld_market8",
            "Graphics/Building/bld_market8_open.kf");

        Assert.Equal(3.3333, animation.Duration.TotalSeconds, 3);

        var visChannels = animation.Channels
            .Where(c => c.ChannelType == ImportedAnimationChannelType.Visibility)
            .ToArray();
        Assert.Equal(2, visChannels.Length);
        Assert.All(visChannels, c => Assert.Equal(ImportedAnimationInterpolation.Step, c.Interpolation));

        foreach (var name in new[] { "market_open_1", "market_open_2" })
        {
            var channel = visChannels.Single(c => c.Target.SubObjectName == name);
            Assert.Equal(ImportedAnimationTargetKind.SubObjectName, channel.Target.Kind);
            var keys = channel.Keyframes.ToArray();
            Assert.Equal(3, keys.Length);
            Assert.Equal(0, keys[0].Time.TotalSeconds, 4);
            Assert.False(keys[0].BoolValue);
            Assert.Equal(0.0333, keys[1].Time.TotalSeconds, 4);
            Assert.True(keys[1].BoolValue);
            Assert.Equal(3.3333, keys[2].Time.TotalSeconds, 3);
            Assert.True(keys[2].BoolValue);
        }

        // The root and root NonAccum keyframe controllers still emit bone channels.
        Assert.Contains(animation.Channels, c => c.Target.Kind == ImportedAnimationTargetKind.BoneName);
    }

    [Fact]
    public void MarketCloseInvertsOpenNodesAndShowsCloseNode()
    {
        var animation = ImportAnimation(
            "bld_market8",
            "Graphics/Building/bld_market8_close.kf");

        Assert.Equal(0.0667, animation.Duration.TotalSeconds, 3);

        var close = animation.Channels.Single(
            c => c.ChannelType == ImportedAnimationChannelType.Visibility &&
                c.Target.SubObjectName == "market_close_1");
        Assert.False(close.Keyframes.ToArray()[0].BoolValue);
        Assert.True(close.Keyframes.ToArray()[1].BoolValue);

        foreach (var name in new[] { "market_open_1", "market_open_2" })
        {
            var channel = animation.Channels.Single(
                c => c.ChannelType == ImportedAnimationChannelType.Visibility &&
                    c.Target.SubObjectName == name);
            Assert.True(channel.Keyframes.ToArray()[0].BoolValue);
            Assert.False(channel.Keyframes.ToArray()[2].BoolValue);
        }
    }

    [Fact]
    public void EifvWalkEmitsBaseSlotTextureTranslateU()
    {
        var animation = ImportAnimation(
            "civ12_EIFV_egypt",
            "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf");

        Assert.Equal(1, animation.Duration.TotalSeconds, 3);

        var channel = SingleChannel(animation, ImportedAnimationChannelType.TextureTranslateU);
        Assert.Equal(ImportedAnimationTargetKind.MaterialSlot, channel.Target.Kind);
        Assert.Equal(0, channel.Target.MaterialIndex);
        Assert.Contains("treads", channel.Target.SubObjectName);

        var keys = channel.Keyframes.ToArray();
        Assert.Equal(2, keys.Length);
        Assert.Equal(0, keys[0].FloatValue, 5);
        Assert.Equal(1.5, keys[1].FloatValue, 5);
        Assert.Equal(1, keys[1].Time.TotalSeconds, 3);
    }

    [Fact]
    public void StealthFighterIdleEmitsAmbientAndSpecularPerSlot()
    {
        var animation = ImportAnimation(
            "afb14_StealthFighterBomber",
            "Graphics/Vehicles/afb14_StealthFighterBomber_idle.kf");

        Assert.Equal(0.0333, animation.Duration.TotalSeconds, 3);

        var ambient = animation.Channels
            .Where(c => c.ChannelType == ImportedAnimationChannelType.MaterialAmbientColor)
            .ToArray();
        var specular = animation.Channels
            .Where(c => c.ChannelType == ImportedAnimationChannelType.MaterialSpecularColor)
            .ToArray();
        // Seven material properties each fan out to the same eight material
        // slots: the model's LOD shapes share every animated material.
        Assert.Equal(56, ambient.Length);
        Assert.Equal(56, specular.Length);
        Assert.Equal(8, ambient.Select(c => c.Target.SubObjectName).Distinct().Count());

        Assert.All(ambient.Concat(specular), c =>
        {
            Assert.Equal(ImportedAnimationTargetKind.MaterialSlot, c.Target.Kind);
            Assert.Equal(0, c.Target.MaterialIndex);
        });

        var ambientKeys = ambient[0].Keyframes.ToArray();
        Assert.Equal(2, ambientKeys.Length);
        Assert.Equal(1, ambientKeys[0].Vector3Value.X, 4);
        Assert.Equal(1, ambientKeys[1].Vector3Value.Z, 4);
        var specularKeys = specular[0].Keyframes.ToArray();
        Assert.Equal(2, specularKeys.Length);
        Assert.Equal(0, specularKeys[0].Vector3Value.X, 4);
        Assert.Equal(0, specularKeys[1].Vector3Value.Z, 4);
    }

    [Fact]
    public void FaufishIdleEmitsEightMaterialAlphaChannels()
    {
        var animation = ImportFaufishAnimation();

        Assert.Equal(4, animation.Duration.TotalSeconds, 3);

        var alpha = animation.Channels
            .Where(c => c.ChannelType == ImportedAnimationChannelType.MaterialAlpha)
            .ToArray();
        // Eight alpha controllers fan out to 14 material channels over 8
        // distinct slots: the LOD shapes share animated material properties.
        Assert.Equal(14, alpha.Length);
        Assert.Equal(8, alpha.Select(c => c.Target.SubObjectName).Distinct().Count());
        Assert.All(alpha, c => Assert.Equal(ImportedAnimationTargetKind.MaterialSlot, c.Target.Kind));

        foreach (var channel in alpha)
        {
            var keys = channel.Keyframes.ToArray();
            Assert.InRange(keys.Length, 4, 6);
            Assert.Equal(0, keys[0].FloatValue, 5);
            Assert.Equal(0, keys[0].Time.TotalSeconds, 4);
            Assert.Equal(0, keys[^1].FloatValue, 5);
            Assert.Equal(4, keys[^1].Time.TotalSeconds, 3);
            Assert.All(keys, k => Assert.InRange(k.FloatValue, 0, 1));
        }
    }

    [Fact]
    public void FarmYamFailsOnMorphControllerNamingC6()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => ImportAnimation("bld_farm_yam", "Graphics/Building/bld_farm_yam.kf"));

        Assert.Contains("NiGeomMorpherController", exception.Message);
        Assert.Contains("C6", exception.Message);
    }

    [Fact]
    public void Wall8EmbeddedMaterialControllersDecodeAndResolve()
    {
        var (context, fileSystem) = OpenArchive();
        using var _ = fileSystem;
        var file = ReadModel("bld_wall8_connector");
        var graph = NifSceneConverter.ConvertGraph(file, "bld_wall8_connector", context);

        var controllers = file.Blocks.OfType<NifMaterialColorController>().ToArray();
        Assert.Equal(4, controllers.Length);
        Assert.All(controllers, c => Assert.Equal(7, c.TargetReference));

        var material = Assert.IsType<NifMaterialProperty>(file.Blocks[7]);
        Assert.Equal("NiMaterialProperty", file.Header.Blocks[7].TypeName);
        Assert.Equal("Material #0", material.Name);

        var expected = new[]
        {
            (Index: 8, NifMaterialColorTarget.Emissive),
            (Index: 9, NifMaterialColorTarget.Specular),
            (Index: 10, NifMaterialColorTarget.Diffuse),
            (Index: 11, NifMaterialColorTarget.Ambient),
        };
        foreach (var (index, color) in expected)
        {
            var controller = Assert.IsType<NifMaterialColorController>(file.Blocks[index]);
            Assert.Equal(color, controller.TargetColor);
            var data = Assert.IsType<NifPosData>(file.Blocks[controller.DataReference]);
            Assert.NotNull(data.Keys);
            Assert.Single(data.Keys.Keys);
        }

        var slots = graph.PropertyTargets.ResolvePropertySlots(7);
        Assert.Single(slots);
    }

    [Fact]
    public void BuoyEmbeddedAlphaControllersResolveToDistinctSlots()
    {
        var (context, fileSystem) = OpenArchive();
        using var _ = fileSystem;
        var file = ReadModel("amb_buoy");
        var graph = NifSceneConverter.ConvertGraph(file, "amb_buoy", context);

        var controllers = file.Blocks.OfType<NifAlphaController>().ToArray();
        Assert.Equal(2, controllers.Length);
        Assert.All(controllers, c => Assert.Equal(0x28, c.Flags));

        var slots = controllers
            .Select(c => graph.PropertyTargets.ResolvePropertySlots(c.TargetReference))
            .ToArray();
        Assert.Equal(2, slots.Length);

        // Two distinct material blocks animate two distinct sub-objects.
        Assert.Equal(2, slots.Select(s => s.Single().SubObjectFullName).Distinct().Count());
    }

    [Fact]
    public void Market8EmbeddedVisibilityTargetsBoneObject04()
    {
        var (context, fileSystem) = OpenArchive();
        using var _ = fileSystem;
        var file = ReadModel("bld_market8");
        var graph = NifSceneConverter.ConvertGraph(file, "bld_market8", context);

        var controller = file.Blocks.OfType<NifVisController>().Single();
        Assert.Equal(0x2C, controller.Flags);
        Assert.Equal(37, controller.TargetReference);
        Assert.True(graph.PropertyTargets.TryGetBoneName(controller.TargetReference, out var boneName));
        Assert.Equal("Object04", boneName);
    }
}
