using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.M3;
using Panopticon.FileFormats.M3;
using Panopticon.Tests.Content;
using Panopticon.Tests.Data.M3;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class M3MaterialConverterTests
{
    private const string ModelPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/test/test.m3";

    [Fact]
    public void OpaqueMaterialBindsPbrTextures()
    {
        var importer = new M3ModelImporter();
        var fileSystem = CreateFileSystem(new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            Layers =
            [
                new(M3MaterialLayerSlot.Diffuse, "Assets/Textures/diffuse.dds", UvSource: 0),
                new(M3MaterialLayerSlot.Specular, "Assets/Textures/specular.dds", UvSource: 0),
                new(M3MaterialLayerSlot.Emissive1, "Assets/Textures/emissive.dds", UvSource: 1, ColorChannelSelector: 2),
                new(M3MaterialLayerSlot.Normal, "Assets/Textures/normal.dds", UvSource: 1),
            ],
        });

        var model = importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null))!;

        var mesh = Assert.IsType<ImportedMesh>(Assert.Single(model.SubObjects).RenderObject);
        var material = Assert.Single(mesh.Materials);
        var pbr = material.PbrMetallicRoughness!;
        Assert.Equal(Vector4.One, pbr.BaseColorFactor);
        Assert.Equal(0.0f, pbr.MetallicFactor);
        Assert.Equal(ImportedAlphaMode.Mask, pbr.AlphaMode);
        Assert.Equal(17.0f / 255.0f, pbr.AlphaCutoff);
        AssertBinding(pbr.BaseColorTexture, ImportedTextureRole.BaseColor, 0, "diffuse.dds#BaseColor");
        AssertBinding(pbr.MetallicRoughnessTexture, ImportedTextureRole.MetallicRoughness, 0, "specular.dds#Roughness");
        AssertBinding(pbr.EmissiveTexture, ImportedTextureRole.Emissive, 1, "emissive.dds#Emissive");
        AssertBinding(pbr.NormalTexture, ImportedTextureRole.Normal, 1, "normal.dds#Normal");
        Assert.Equal(Vector3.One, pbr.EmissiveFactor);
    }

    [Fact]
    public void AdditiveMaterialIsSkippedAndNoImportableBatchFails()
    {
        var importer = new M3ModelImporter();
        var fileSystem = CreateFileSystem(new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            MaterialBlendMode = 2,
        });

        var ex = Assert.Throws<InvalidDataException>(
            () => importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null)));

        Assert.Contains("no importable batch", ex.Message);
        var skipped = Assert.Single(importer.LastDiagnostics.SkippedBatches);
        Assert.Equal(2u, skipped.BlendMode);
    }

    [Fact]
    public void DecalLayerIsIgnoredWithDiagnostic()
    {
        var importer = new M3ModelImporter();
        var fileSystem = CreateFileSystem(new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            Layers =
            [
                new(M3MaterialLayerSlot.Diffuse, "Assets/Textures/diffuse.dds"),
                new(M3MaterialLayerSlot.Decal, "Assets/Textures/decal.dds", UvSource: 1),
            ],
        });

        importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null));

        var ignored = Assert.Single(importer.LastDiagnostics.IgnoredLayers);
        Assert.Equal(M3MaterialLayerSlot.Decal, ignored.Slot);
        Assert.Contains("unsupported layer slot", ignored.Reason);
    }

    [Fact]
    public void UnsupportedUvSourceIsIgnoredWithDiagnostic()
    {
        var importer = new M3ModelImporter();
        var fileSystem = CreateFileSystem(new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            Layers =
            [
                new(M3MaterialLayerSlot.Diffuse, "Assets/Textures/diffuse.dds", UvSource: 2),
            ],
        });

        var model = importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null))!;

        var mesh = Assert.IsType<ImportedMesh>(Assert.Single(model.SubObjects).RenderObject);
        Assert.Null(Assert.Single(mesh.Materials).PbrMetallicRoughness!.BaseColorTexture);
        var ignored = Assert.Single(importer.LastDiagnostics.IgnoredLayers);
        Assert.Equal(M3MaterialLayerSlot.Diffuse, ignored.Slot);
        Assert.Contains("UV source 2", ignored.Reason);
    }

    [Fact]
    public void MaterialReferenceTypeTwoIsRejected()
    {
        var importer = new M3ModelImporter();
        var fileSystem = CreateFileSystem(new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            MaterialReferenceType = 2,
        });

        var ex = Assert.Throws<NotSupportedException>(
            () => importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null)));

        Assert.Contains("type 2", ex.Message);
    }

    [Fact]
    public void TextureResolutionFallsBackToCorePackage()
    {
        var importer = new M3ModelImporter();
        var builder = new M3TestDataBuilder
        {
            Vertices = TriangleVertices(),
            Layers =
            [
                new(M3MaterialLayerSlot.Diffuse, "Assets/Textures/core_diffuse.dds"),
            ],
        };
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(ModelPath, builder.Build());
        fileSystem.AddFile("mods/core.sc2mod/base.sc2assets/Assets/Textures/core_diffuse.dds", CreateDxt1Dds());

        var model = importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null))!;

        var mesh = Assert.IsType<ImportedMesh>(Assert.Single(model.SubObjects).RenderObject);
        Assert.NotNull(Assert.Single(mesh.Materials).PbrMetallicRoughness!.BaseColorTexture);
    }

    [Fact]
    public void MissingTextureListsOnlySupportedSearchOrder()
    {
        var importer = new M3ModelImporter();
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(ModelPath, new M3TestDataBuilder { Vertices = TriangleVertices() }.Build());

        var ex = Assert.Throws<FileNotFoundException>(
            () => importer.ImportModel(ModelPath, new AssetImportContext(fileSystem, null)));

        Assert.Contains("mods/liberty.sc2mod/base.sc2assets", ex.Message);
        Assert.Contains("mods/core.sc2mod/base.sc2assets", ex.Message);
        Assert.DoesNotContain("teen.sc2assets", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryFileSystem CreateFileSystem(M3TestDataBuilder builder)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile(ModelPath, builder.Build());
        foreach (var layer in builder.Layers.Select(layer => layer.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            fileSystem.AddFile($"mods/liberty.sc2mod/base.sc2assets/{layer}", CreateDxt1Dds());
        }

        return fileSystem;
    }

    private static M3TestVertex[] TriangleVertices()
    {
        return
        [
            new(new Vector3(0, -1, 0), Vector3.UnitZ, new Vector2(0, 0), new Vector2(1, 0)),
            new(new Vector3(1, -1, 0), Vector3.UnitZ, new Vector2(1, 0), new Vector2(1, 1)),
            new(new Vector3(0, -2, 0), Vector3.UnitZ, new Vector2(0, 1), new Vector2(0, 1)),
        ];
    }

    private static void AssertBinding(
        ImportedTextureBinding? binding,
        ImportedTextureRole role,
        int uvSet,
        string diagnosticSuffix)
    {
        Assert.NotNull(binding);
        binding = binding!;
        Assert.Equal(role, binding.Role);
        Assert.Equal(uvSet, binding.UvSet);
        Assert.EndsWith(diagnosticSuffix, binding.ImageSource.DiagnosticName);
    }

    private static byte[] CreateDxt1Dds()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        writer.Write((uint)124);
        writer.Write((uint)0x0002100F);
        writer.Write((uint)4);
        writer.Write((uint)4);
        writer.Write((uint)8);
        writer.Write((uint)0);
        writer.Write((uint)1);
        writer.Write(new byte[44]);
        writer.Write((uint)32);
        writer.Write((uint)0x4);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("DXT1"));
        writer.Write(new byte[20]);
        writer.Write((uint)0x1000);
        writer.Write(new byte[16]);
        writer.Write(new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0, 0, 0, 0 });
        return stream.ToArray();
    }
}
