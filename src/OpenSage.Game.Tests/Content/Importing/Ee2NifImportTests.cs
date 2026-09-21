using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Nif;
using OpenSage.IO;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

/// <summary>
/// Locks the two installed EE2 target models to the complete CPU descriptors
/// recorded by WI-C4a-01 in docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md
/// section 9. Interactive: requires EMPIRE_EARTH_II_PATH and zips/graphics.zip.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Ee2NifImportTests
{
    private const string ArchiveRelativePath = "zips/graphics.zip";
    private const float FeetPerMeter = 3.280839895f;

    private const string TexturesArchiveRelativePath = "zips/textures.zip";

    private static (FileSystem FileSystem, NifModelImporter Importer) OpenArchive(bool withTextures)
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

        var fileSystem = (FileSystem)new ZipFileSystem(archivePath);
        if (withTextures)
        {
            var texturesPath = Path.Combine(root, TexturesArchiveRelativePath);
            if (!File.Exists(texturesPath))
            {
                throw new InvalidOperationException(
                    $"Interactive test requires the EE2 textures archive at '{texturesPath}'.");
            }
            fileSystem = new CompositeFileSystem(fileSystem, new ZipFileSystem(texturesPath));
        }

        return (fileSystem, new NifModelImporter());
    }

    private static ImportedModel Import(string key, bool withTextures = false)
    {
        var (fileSystem, importer) = OpenArchive(withTextures);
        using var _ = fileSystem;
        var context = new AssetImportContext(fileSystem, language: null);
        return importer.ImportModel(key, context)
            ?? throw new InvalidOperationException($"Import returned null for '{key}'.");
    }

    private static ImportedMesh MeshAt(ImportedModel model, int index)
    {
        return (ImportedMesh)model.SubObjects[index].RenderObject;
    }

    [Fact]
    public void WarGalleyMatchesC4a01Descriptor()
    {
        var model = Import("nwg1_galley");

        Assert.Equal("nwg1_galley", model.Name);
        Assert.Equal(ImportedSkeletonOrigin.Default, model.SkeletonOrigin);
        Assert.Single(model.Skeleton.Bones);
        Assert.Equal(-1, model.Skeleton.Bones[0].ParentIndex);

        Assert.Equal(13, model.SubObjects.Count);
        Assert.All(model.SubObjects, s => Assert.Equal(0, s.ParentBoneIndex));
        Assert.All(model.SubObjects, s => Assert.False(((ImportedMesh)s.RenderObject).IsSkinned));

        Assert.Equal("Galley1", model.SubObjects[0].DisplayName);
        var hull = MeshAt(model, 0);
        Assert.Equal(833, hull.Vertices.Count);
        Assert.Equal(774, hull.Indices.Count / 3);

        for (var i = 1; i < model.SubObjects.Count; i++)
        {
            Assert.Equal("Editable Mesh", model.SubObjects[i].DisplayName);
            var oar = MeshAt(model, i);
            Assert.Equal(19, oar.Vertices.Count);
            Assert.Equal(34, oar.Indices.Count / 3);
        }

        AssertTexture0(model, i: 0, expected: "nwg1_galley.tga");
        AssertTexture0(model, i: 6, expected: "nwg1_galley.tga");

        // C4a-01 world AABB in source units x [-14.71, 14.66],
        // y [-23.91, 22.86], z [-9.92, 24.60]; engine = (-y, x, z) * feet.
        AssertBounds(model,
            new Vector3(-22.860f, -14.712f, -9.922f) * FeetPerMeter,
            new Vector3(23.911f, 14.656f, 24.604f) * FeetPerMeter,
            tolerance: 0.2f);
    }

    [Fact]
    public void TwinEngineBomberMatchesC4a01DescriptorWithHighestDetailLodOnly()
    {
        var model = Import("ab11_TwinEngineBomber");

        Assert.Equal("ab11_TwinEngineBomber", model.Name);
        Assert.Equal(6, model.SubObjects.Count);
        Assert.All(model.SubObjects, s => Assert.False(((ImportedMesh)s.RenderObject).IsSkinned));

        var expected = new[]
        {
            ("Editable Poly", 76, 68, "af11_biplane_prop.tga"),
            ("Editable Poly", 76, 68, "af11_biplane_prop.tga"),
            ("wheels", 40, 24, "ab11_TwinEngineBomber.tga"),
            ("body_LOD0", 738, 531, "ab11_TwinEngineBomber.tga"),
            ("pilot_LOD0", 335, 420, "af11_biplane_prop.tga"),
            ("pilot01_LOD0", 335, 420, "af11_biplane_prop.tga"),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            var (name, vertices, triangles, texture) = expected[i];
            Assert.Equal(name, model.SubObjects[i].DisplayName);
            var mesh = MeshAt(model, i);
            Assert.Equal(vertices, mesh.Vertices.Count);
            Assert.Equal(triangles, mesh.Indices.Count / 3);
            AssertTexture0(model, i, texture);
        }

        var displayNames = model.SubObjects.Select(s => s.DisplayName).ToArray();
        Assert.DoesNotContain("body_LOD1", displayNames);
        Assert.DoesNotContain("pilot_LOD1", displayNames);
        Assert.DoesNotContain("pilot01_LOD1", displayNames);

        // Wheels/body carry alpha blending and specular per the inventory.
        Assert.True(MeshAt(model, 2).Materials[0].BlendEnabled);
        Assert.True(MeshAt(model, 3).Materials[0].BlendEnabled);
        Assert.True(MeshAt(model, 3).Materials[0].FixedFunctionShading.SpecularEnabled);
        Assert.Equal(
            new Vector3(0.15294118f, 0.15294118f, 0.15294118f),
            MeshAt(model, 3).Materials[0].FixedFunctionShading.Specular);

        // C4a-01 world AABB in source units x [-9.66, 9.62],
        // y [-7.25, 6.53], z [-1.95, 5.61]; engine = (-y, x, z) * feet.
        AssertBounds(model,
            new Vector3(-6.535f, -9.660f, -1.953f) * FeetPerMeter,
            new Vector3(7.248f, 9.622f, 5.608f) * FeetPerMeter,
            tolerance: 0.2f);
    }

    [Fact]
    public void TextureStageFollowsTexcacheAvailability()
    {
        // graphics.zip alone: no texcache resolves, C4a untextured fallback.
        var untextured = Import("nwg1_galley");
        var untexturedShading = ((ImportedMesh)untextured.SubObjects[0].RenderObject)
            .Materials[0].FixedFunctionShading!;
        Assert.False(untexturedShading.TexturingEnabled);
        Assert.Equal(0, untexturedShading.TextureStageCount);

        // With textures.zip mounted: the base stage turns on.
        var textured = Import("nwg1_galley", withTextures: true);
        var texturedShading = ((ImportedMesh)textured.SubObjects[0].RenderObject)
            .Materials[0].FixedFunctionShading!;
        Assert.True(texturedShading.TexturingEnabled);
        Assert.Equal(1, texturedShading.TextureStageCount);
        AssertTexture0(textured, 0, "nwg1_galley.tga", expectedStageCount: 1);

        // Counts and bounds are unchanged by the texture resolution.
        Assert.Equal(untextured.SubObjects.Count, textured.SubObjects.Count);
    }

    [Fact]
    public void MissingArchiveFailsWithDirectSetupMessage()
    {
        var root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, ArchiveRelativePath)))
        {
            // Cannot exercise the failure path on a machine with the install.
            return;
        }

        var ex = Assert.ThrowsAny<InvalidOperationException>(() => OpenArchive(withTextures: false));
        Assert.Contains("EMPIRE_EARTH_II_PATH", ex.Message);
    }

    private static void AssertTexture0(ImportedModel model, int i, string expected, int expectedStageCount = 0)
    {
        var material = MeshAt(model, i).Materials[0];
        var textureProperty = material.Properties.Single(p => p.Name == "Texture0");
        Assert.Equal(expected, textureProperty.TextureName);
        Assert.Equal(expectedStageCount, material.FixedFunctionShading.TextureStageCount);
    }

    private static void AssertBounds(
        ImportedModel model,
        Vector3 expectedMin,
        Vector3 expectedMax,
        float tolerance)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var subObject in model.SubObjects)
        {
            var box = ((ImportedMesh)subObject.RenderObject).BoundingBox;
            min = Vector3.Min(min, box.Min);
            max = Vector3.Max(max, box.Max);
        }

        Assert.True(
            (min - expectedMin).Length() < tolerance &&
            (max - expectedMax).Length() < tolerance,
            $"Bounds ({min}..{max}) differ from expected ({expectedMin}..{expectedMax}).");
    }
}
