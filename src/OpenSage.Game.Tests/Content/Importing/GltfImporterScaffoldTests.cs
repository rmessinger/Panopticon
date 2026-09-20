#nullable enable

using System;
using System.IO;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfImporterScaffoldTests
{
    // One indexed-free triangle (POSITION only), generated with SharpGLTF.Core 1.0.7
    // ModelRoot.WriteGLB and strict-parse verified.
    private static readonly byte[] MinimalGlb =
    new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x02, 0x00, 0x00, 0x00, 0xF0, 0x01, 0x00, 0x00, 0xB0, 0x01, 0x00, 0x00, 0x4A, 0x53, 0x4F, 0x4E, 0x7B, 0x22, 0x61, 0x73, 0x73, 0x65, 0x74, 0x22, 0x3A, 0x7B, 0x22, 0x63, 0x6F, 0x70, 0x79, 0x72, 0x69, 0x67, 0x68, 0x74, 0x22, 0x3A, 0x22, 0x22, 0x2C, 0x22, 0x67, 0x65, 0x6E, 0x65, 0x72, 0x61, 0x74, 0x6F, 0x72, 0x22, 0x3A, 0x22, 0x53, 0x68, 0x61, 0x72, 0x70, 0x47, 0x4C, 0x54, 0x46, 0x20, 0x31, 0x2E, 0x30, 0x2E, 0x30, 0x2B, 0x61, 0x63, 0x62, 0x63, 0x31, 0x35, 0x31, 0x65, 0x37, 0x30, 0x64, 0x36, 0x35, 0x63, 0x65, 0x62, 0x38, 0x62, 0x65, 0x63, 0x32, 0x34, 0x66, 0x61, 0x39, 0x35, 0x66, 0x30, 0x30, 0x36, 0x37, 0x64, 0x30, 0x65, 0x35, 0x65, 0x64, 0x39, 0x33, 0x33, 0x22, 0x2C, 0x22, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, 0x22, 0x3A, 0x22, 0x32, 0x2E, 0x30, 0x22, 0x7D, 0x2C, 0x22, 0x61, 0x63, 0x63, 0x65, 0x73, 0x73, 0x6F, 0x72, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x50, 0x4F, 0x53, 0x49, 0x54, 0x49, 0x4F, 0x4E, 0x22, 0x2C, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x56, 0x69, 0x65, 0x77, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x63, 0x6F, 0x6D, 0x70, 0x6F, 0x6E, 0x65, 0x6E, 0x74, 0x54, 0x79, 0x70, 0x65, 0x22, 0x3A, 0x35, 0x31, 0x32, 0x36, 0x2C, 0x22, 0x63, 0x6F, 0x75, 0x6E, 0x74, 0x22, 0x3A, 0x33, 0x2C, 0x22, 0x6D, 0x61, 0x78, 0x22, 0x3A, 0x5B, 0x31, 0x2C, 0x31, 0x2C, 0x30, 0x5D, 0x2C, 0x22, 0x6D, 0x69, 0x6E, 0x22, 0x3A, 0x5B, 0x30, 0x2C, 0x30, 0x2C, 0x30, 0x5D, 0x2C, 0x22, 0x74, 0x79, 0x70, 0x65, 0x22, 0x3A, 0x22, 0x56, 0x45, 0x43, 0x33, 0x22, 0x7D, 0x5D, 0x2C, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x56, 0x69, 0x65, 0x77, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x22, 0x3A, 0x30, 0x2C, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74, 0x68, 0x22, 0x3A, 0x33, 0x36, 0x7D, 0x5D, 0x2C, 0x22, 0x62, 0x75, 0x66, 0x66, 0x65, 0x72, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x62, 0x79, 0x74, 0x65, 0x4C, 0x65, 0x6E, 0x67, 0x74, 0x68, 0x22, 0x3A, 0x33, 0x36, 0x7D, 0x5D, 0x2C, 0x22, 0x6D, 0x65, 0x73, 0x68, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x6E, 0x61, 0x6D, 0x65, 0x22, 0x3A, 0x22, 0x74, 0x72, 0x69, 0x61, 0x6E, 0x67, 0x6C, 0x65, 0x22, 0x2C, 0x22, 0x70, 0x72, 0x69, 0x6D, 0x69, 0x74, 0x69, 0x76, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x61, 0x74, 0x74, 0x72, 0x69, 0x62, 0x75, 0x74, 0x65, 0x73, 0x22, 0x3A, 0x7B, 0x22, 0x50, 0x4F, 0x53, 0x49, 0x54, 0x49, 0x4F, 0x4E, 0x22, 0x3A, 0x30, 0x7D, 0x7D, 0x5D, 0x7D, 0x5D, 0x2C, 0x22, 0x6E, 0x6F, 0x64, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x6D, 0x65, 0x73, 0x68, 0x22, 0x3A, 0x30, 0x7D, 0x5D, 0x2C, 0x22, 0x73, 0x63, 0x65, 0x6E, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x7B, 0x22, 0x6E, 0x6F, 0x64, 0x65, 0x73, 0x22, 0x3A, 0x5B, 0x30, 0x5D, 0x7D, 0x5D, 0x7D, 0x24, 0x00, 0x00, 0x00, 0x42, 0x49, 0x4E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x00 };

    private static AssetImportContext CreateContext(params (string Path, byte[] Bytes)[] files)
    {
        var fileSystem = new MemoryFileSystem();
        foreach (var (path, bytes) in files)
        {
            fileSystem.AddFile(path, bytes);
        }

        return new AssetImportContext(fileSystem, null);
    }

    [Fact]
    public void Options_RejectNonPositiveOrNonFiniteUnits()
    {
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(unitsPerMeter: 0));
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(unitsPerMeter: -1));
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(unitsPerMeter: float.NaN));
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(unitsPerMeter: float.PositiveInfinity));
    }

    [Fact]
    public void Options_RejectNonPositiveLimits()
    {
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(maxRootDocumentBytes: 0));
        Assert.Throws<ArgumentException>(() => new GltfImportOptions(maxDataUriBytes: -1));
    }

    [Fact]
    public void Options_DefaultUnitsAreFeet()
    {
        Assert.Equal(1f / 0.3048f, GltfImportOptions.Default.UnitsPerMeter, 5);
    }

    [Theory]
    [InlineData("models/tank.gltf", "models/tank.gltf")]
    [InlineData("models\\tank.glb", "models/tank.glb")]
    [InlineData("TANK.GLB", "TANK.GLB")]
    public void ModelKeys_AcceptNormalizedGltfAndGlbPaths(string key, string expected)
    {
        Assert.True(GltfAssetKey.TryParseModel(key, out var modelKey));
        Assert.Equal(expected, modelKey.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("models/tank.w3d")]
    [InlineData("models/tank.gltf?x=1")]
    [InlineData("models/tank.gltf#fragment")]
    [InlineData("/absolute/tank.gltf")]
    public void ModelKeys_RejectOtherForms(string key)
    {
        Assert.False(GltfAssetKey.TryParseModel(key, out _));
    }

    [Theory]
    [InlineData("models/tank.gltf#animation=0", "models/tank.gltf", 0)]
    [InlineData("models\\tank.glb#animation=42", "models/tank.glb", 42)]
    [InlineData("models/tank.gltf#animation=007", "models/tank.gltf", 7)]
    public void AnimationKeys_AcceptModelPathWithIndex(string key, string expectedModelPath, int expectedIndex)
    {
        Assert.True(GltfAssetKey.TryParseAnimation(key, out var animationKey));
        Assert.Equal(expectedModelPath, animationKey.ModelPath);
        Assert.Equal(expectedIndex, animationKey.AnimationIndex);
    }

    [Theory]
    [InlineData("models/tank.gltf")]
    [InlineData("models/tank.gltf#")]
    [InlineData("models/tank.gltf#animation=")]
    [InlineData("models/tank.gltf#animation=-1")]
    [InlineData("models/tank.gltf#animation=abc")]
    [InlineData("models/tank.gltf#animation=1#animation=2")]
    [InlineData("models/tank.gltf#animation=1?x=2")]
    [InlineData("models/tank.gltf#animations=1")]
    [InlineData("models/tank.w3d#animation=1")]
    [InlineData("models/tank.gltf#animation=99999999999")]
    public void AnimationKeys_RejectOtherForms(string key)
    {
        Assert.False(GltfAssetKey.TryParseAnimation(key, out _));
    }

    [Fact]
    public void Loader_MissingRootReturnsNull()
    {
        var context = CreateContext();

        var result = GltfDocumentLoader.TryLoadDocument(
            "models/tank.gltf",
            context,
            GltfImportOptions.Default,
            externalResolver: null);

        Assert.Null(result);
    }

    [Fact]
    public void Loader_OversizedRootThrows()
    {
        var context = CreateContext(("models/big.gltf", new byte[1024]));
        var options = new GltfImportOptions(maxRootDocumentBytes: 512);

        Assert.Throws<InvalidDataException>(() => GltfDocumentLoader.TryLoadDocument(
            "models/big.gltf",
            context,
            options,
            externalResolver: null));
    }

    [Fact]
    public void Loader_MalformedJsonThrowsWithPath()
    {
        var context = CreateContext(("models/bad.gltf", "not json"u8.ToArray()));

        var exception = Assert.Throws<InvalidDataException>(() => GltfDocumentLoader.TryLoadDocument(
            "models/bad.gltf",
            context,
            GltfImportOptions.Default,
            externalResolver: null));

        Assert.Contains("models/bad.gltf", exception.Message);
    }

    [Fact]
    public void Loader_MalformedGlbThrows()
    {
        var context = CreateContext(("models/bad.glb", new byte[] { 0x47, 0x4C, 0x46, 0x46, 0x02, 0x00, 0x00, 0x00 }));

        Assert.Throws<InvalidDataException>(() => GltfDocumentLoader.TryLoadDocument(
            "models/bad.glb",
            context,
            GltfImportOptions.Default,
            externalResolver: null));
    }

    [Fact]
    public void Loader_ValidGlbParses()
    {
        var context = CreateContext(("models/triangle.glb", MinimalGlb));

        var result = GltfDocumentLoader.TryLoadDocument(
            "models/triangle.glb",
            context,
            GltfImportOptions.Default,
            externalResolver: null);

        Assert.NotNull(result);
        Assert.Single(result.Root.LogicalMeshes);
    }

    [Fact]
    public void Loader_ExternalReferenceWithoutResolverThrows()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "uri": "triangle.bin", "byteLength": 4 } ]
            }
            """;
        var context = CreateContext(("models/external.gltf", System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Throws<InvalidDataException>(() => GltfDocumentLoader.TryLoadDocument(
            "models/external.gltf",
            context,
            GltfImportOptions.Default,
            externalResolver: null));
    }

    [Fact]
    public void Loader_ExternalReferenceThroughResolverParses()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "uri": "triangle.bin", "byteLength": 4 } ]
            }
            """;
        var context = CreateContext(("models/external.gltf", System.Text.Encoding.UTF8.GetBytes(json)));

        var result = GltfDocumentLoader.TryLoadDocument(
            "models/external.gltf",
            context,
            GltfImportOptions.Default,
            relativePath => relativePath == "triangle.bin" ? new byte[4] : null);

        Assert.NotNull(result);
    }

    [Fact]
    public void Loader_ExternalReferenceMissingThroughResolverThrows()
    {
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "uri": "missing.bin", "byteLength": 4 } ]
            }
            """;
        var context = CreateContext(("models/external.gltf", System.Text.Encoding.UTF8.GetBytes(json)));

        Assert.Throws<InvalidDataException>(() => GltfDocumentLoader.TryLoadDocument(
            "models/external.gltf",
            context,
            GltfImportOptions.Default,
            _ => null));
    }
}
