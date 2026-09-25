#nullable enable

using System;
using System.IO;
using System.Text;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class GltfResourceResolverTests
{
    private static GltfResourceResolver CreateResolver(
        string rootDocumentPath,
        params (string Path, byte[] Bytes)[] files)
    {
        var fileSystem = new MemoryFileSystem();
        foreach (var (path, bytes) in files)
        {
            fileSystem.AddFile(path, bytes);
        }

        return new GltfResourceResolver(rootDocumentPath, fileSystem, GltfImportOptions.Default);
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        var fileSystem = new MemoryFileSystem();

        Assert.Throws<ArgumentException>(() => new GltfResourceResolver("", fileSystem, GltfImportOptions.Default));
        Assert.Throws<ArgumentNullException>(() => new GltfResourceResolver("a.gltf", null!, GltfImportOptions.Default));
        Assert.Throws<ArgumentNullException>(() => new GltfResourceResolver("a.gltf", fileSystem, null!));
    }

    [Theory]
    [InlineData("models/tank.gltf", "triangle.bin", "models/triangle.bin")]
    [InlineData("models/tank.gltf", "./triangle.bin", "models/triangle.bin")]
    [InlineData("models/sub/tank.gltf", "../other/image.png", "models/other/image.png")]
    [InlineData("models/tank.gltf", "../outside/triangle.bin", "outside/triangle.bin")]
    [InlineData("tank.gltf", "textures/skin.png", "textures/skin.png")]
    public void RelativeReferences_ResolveAgainstDocumentDirectory(string root, string uri, string expected)
    {
        var resolver = CreateResolver(root);

        Assert.Equal(expected, resolver.TryGetExternalPath(uri));
    }

    [Theory]
    [InlineData("models/tank.gltf", "/absolute/triangle.bin")]
    [InlineData("models/tank.gltf", "http://example.com/triangle.bin")]
    [InlineData("models/tank.gltf", "file:///triangle.bin")]
    [InlineData("models/tank.gltf", "C:/triangle.bin")]
    [InlineData("models/tank.gltf", "../../outside/triangle.bin")]
    [InlineData("tank.gltf", "../outside.bin")]
    [InlineData("models/tank.gltf", "a%2fb.bin")]
    [InlineData("models/tank.gltf", "bad%zz.bin")]
    [InlineData("models/tank.gltf", "")]
    public void InvalidReferences_AreRejected(string root, string uri)
    {
        var resolver = CreateResolver(root);

        Assert.Throws<InvalidDataException>(() => resolver.TryGetExternalPath(uri));
    }

    [Fact]
    public void DataUris_AreNotExternalPaths()
    {
        var resolver = CreateResolver("models/tank.gltf");

        Assert.Null(resolver.TryGetExternalPath("data:application/octet-stream;base64,QUJD"));
    }

    [Fact]
    public void ExternalResources_ReadThroughFileSystem_WithinLimits()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var resolver = CreateResolver(
            "models/tank.gltf",
            ("models/triangle.bin", payload));

        Assert.Equal(payload, resolver.Resolve("triangle.bin"));
    }

    [Fact]
    public void RepeatedReferences_ShareOnePayload()
    {
        var payload = new byte[] { 9, 8, 7 };
        var resolver = CreateResolver(
            "models/tank.gltf",
            ("models/triangle.bin", payload));

        var first = resolver.Resolve("triangle.bin");
        var second = resolver.Resolve("./triangle.bin");

        Assert.Same(first, second);
    }

    [Fact]
    public void MissingReference_ThrowsWithContext()
    {
        var resolver = CreateResolver("models/tank.gltf");

        var exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve("triangle.bin"));

        Assert.Contains("models/triangle.bin", exception.Message);
        Assert.Contains("models/tank.gltf", exception.Message);
    }

    [Fact]
    public void OversizedResource_Throws()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("models/big.bin", new byte[1024]);
        var options = new GltfImportOptions(maxExternalResourceBytes: 512);
        var resolver = new GltfResourceResolver("models/tank.gltf", fileSystem, options);

        Assert.Throws<InvalidDataException>(() => resolver.Resolve("big.bin"));
    }

    [Fact]
    public void AggregateLimit_Throws()
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("models/a.bin", new byte[600]);
        fileSystem.AddFile("models/b.bin", new byte[600]);
        var options = new GltfImportOptions(maxExternalResourceBytes: 1024, maxAggregateExternalResourceBytes: 1000);
        var resolver = new GltfResourceResolver("models/tank.gltf", fileSystem, options);

        Assert.Equal(600, resolver.Resolve("a.bin").Length);

        var exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve("b.bin"));

        Assert.Contains("aggregate", exception.Message);
    }

    [Fact]
    public void Base64DataUri_DecodesWithinLimits()
    {
        var resolver = new GltfResourceResolver(
            "models/tank.gltf",
            new MemoryFileSystem(),
            new GltfImportOptions(maxDataUriBytes: 16));

        var payload = resolver.Resolve("data:application/octet-stream;base64,QUJDRA==");

        Assert.Equal(new byte[] { 65, 66, 67, 68 }, payload);
    }

    [Fact]
    public void PercentEncodedDataUri_Decodes()
    {
        var resolver = CreateResolver("models/tank.gltf");

        var payload = resolver.Resolve("data:application/octet-stream,ABC");

        Assert.Equal(new byte[] { 65, 66, 67 }, payload);
    }

    [Theory]
    [InlineData("data:image/webp;base64,QUJD")]
    [InlineData("data:;base64,QUJD")]
    [InlineData("data:application/octet-stream;base64,!!")]
    [InlineData("data:application/octet-stream")]
    public void MalformedDataUris_Throw(string uri)
    {
        var resolver = CreateResolver("models/tank.gltf");

        Assert.Throws<InvalidDataException>(() => resolver.Resolve(uri));
    }

    [Fact]
    public void OversizedDataUri_Throws()
    {
        var resolver = new GltfResourceResolver(
            "models/tank.gltf",
            new MemoryFileSystem(),
            new GltfImportOptions(maxDataUriBytes: 2));

        Assert.Throws<InvalidDataException>(() => resolver.Resolve("data:application/octet-stream;base64,QUJDRA=="));
    }

    [Fact]
    public void DataUri_RepeatedReference_ShareOnePayload()
    {
        var resolver = CreateResolver("models/tank.gltf");

        var first = resolver.Resolve("data:application/octet-stream;base64,QUJD");
        var second = resolver.Resolve("data:application/octet-stream;base64,QUJD");

        Assert.Same(first, second);
    }

    [Fact]
    public void Loader_UsesResolverForExternalBuffers()
    {
        var buffer = new byte[36];
        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "uri": "triangle.bin", "byteLength": 36 } ]
            }
            """;
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("models/external.gltf", Encoding.UTF8.GetBytes(json));
        fileSystem.AddFile("models/triangle.bin", buffer);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("models/external.gltf", fileSystem, GltfImportOptions.Default);

        var root = GltfDocumentLoader.TryLoadDocument("models/external.gltf", context, GltfImportOptions.Default, resolver);

        Assert.NotNull(root);
    }
}
