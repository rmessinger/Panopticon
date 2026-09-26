#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.M3;
using Panopticon.FileFormats.Casc;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

[Trait("Category", "Interactive")]
public sealed class Sc2M3ImportTests
{
    private const string MarinePath = "mods/liberty.sc2mod/base.sc2assets/assets/units/terran/marine/marine.m3";
    private const string ZealotPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/protoss/zealot/zealot.m3";
    private const string ZerglingPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/zerg/zergling/zergling.m3";

    public static IEnumerable<object[]> ProofModels =>
    [
        [MarinePath, 1_678, 2, false],
        [ZealotPath, 2_619, 1, true],
        [ZerglingPath, 775, 1, true],
    ];

    [Theory]
    [MemberData(nameof(ProofModels))]
    public void ProofModelsImportStaticGeometryAndTextures(
        string path,
        int expectedVertices,
        int expectedSubObjects,
        bool expectedEmissive)
    {
        using var fileSystem = OpenCascFileSystem();
        var importer = new M3ModelImporter();

        var imported = importer.ImportModel(path, new AssetImportContext(fileSystem, language: null));

        Assert.NotNull(imported);
        var model = imported!;
        Assert.Equal(path, model.Name);
        Assert.Single(model.Skeleton.Bones);
        Assert.Equal(expectedSubObjects, model.SubObjects.Count);

        var meshes = model.SubObjects
            .Select(subObject => Assert.IsType<ImportedMesh>(subObject.RenderObject))
            .ToArray();
        Assert.Equal(expectedVertices, meshes.Sum(mesh => mesh.Vertices.Count));
        Assert.All(meshes, mesh => Assert.False(mesh.IsSkinned));

        foreach (var mesh in meshes)
        {
            Assert.NotEmpty(mesh.Indices);
            Assert.Equal(0, mesh.Indices.Count % 3);
            var material = Assert.Single(mesh.Materials);
            Assert.Equal(ImportedMaterial.PbrMetallicRoughnessFamily, material.ShaderFamily);
            Assert.NotNull(material.PbrMetallicRoughness);
            var pbr = material.PbrMetallicRoughness!;
            AssertBinding(pbr.BaseColorTexture, ImportedTextureRole.BaseColor, "#BaseColor");
            AssertBinding(pbr.MetallicRoughnessTexture, ImportedTextureRole.MetallicRoughness, "#Roughness");
            AssertBinding(pbr.NormalTexture, ImportedTextureRole.Normal, "#Normal");
            if (expectedEmissive)
            {
                AssertBinding(pbr.EmissiveTexture, ImportedTextureRole.Emissive, "#Emissive");
            }
            else
            {
                Assert.Null(pbr.EmissiveTexture);
            }
        }

        if (path == ZealotPath)
        {
            var skipped = Assert.Single(importer.LastDiagnostics.SkippedBatches);
            Assert.Equal(0, skipped.BatchIndex);
            Assert.Equal(2, skipped.MaterialReferenceIndex);
            Assert.Equal(2u, skipped.BlendMode);
            Assert.Contains("non-opaque", skipped.Reason);
        }
        else
        {
            Assert.Empty(importer.LastDiagnostics.SkippedBatches);
        }
    }

    [Fact]
    public void MissingInstallFailsWithDirectSetupMessage()
    {
        var root = Environment.GetEnvironmentVariable("STARCRAFT_II_PATH");
        if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, ".build.info")))
        {
            return;
        }

        var ex = Assert.ThrowsAny<InvalidOperationException>(() => OpenCascFileSystem());
        Assert.Contains("STARCRAFT_II_PATH", ex.Message);
    }

    private static FileSystem OpenCascFileSystem()
    {
        var root = Environment.GetEnvironmentVariable("STARCRAFT_II_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires STARCRAFT_II_PATH pointing at a StarCraft II install.");
        }

        if (!File.Exists(Path.Combine(root, ".build.info")))
        {
            throw new InvalidOperationException(
                $"Interactive test requires a StarCraft II .build.info file at '{root}'.");
        }

        return new CascFileSystem(CascStorage.Open(root));
    }

    private static void AssertBinding(ImportedTextureBinding? binding, ImportedTextureRole role, string diagnosticSuffix)
    {
        Assert.NotNull(binding);
        var actual = binding!;
        Assert.Equal(role, actual.Role);
        Assert.True(
            actual.ImageSource.DiagnosticName.EndsWith(diagnosticSuffix, StringComparison.Ordinal),
            $"Expected diagnostic name ending with '{diagnosticSuffix}', got '{actual.ImageSource.DiagnosticName}'.");
    }
}
