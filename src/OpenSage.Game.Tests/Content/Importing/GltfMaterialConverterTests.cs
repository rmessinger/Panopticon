#nullable enable

using System;
using System.IO;
using System.Text;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfMaterialConverterTests
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static (SharpGLTF.Schema2.ModelRoot Root, GltfResourceResolver Resolver) Load(
        string materialJson,
        byte[]? externalImage = null)
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }, \"materials\": [ " + materialJson + " ] }";
        var fileSystem = new MemoryFileSystem();
        if (externalImage != null)
        {
            fileSystem.AddFile("textures/skin.png", externalImage);
        }

        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);
        Assert.NotNull(document);
        return (document!.Root, resolver);
    }

    private static string ImageBlock(string uri) => "{ \"uri\": \"" + uri + "\", \"mimeType\": \"image/png\" }";

    private static string DataUriImage =>
        "{ \"bufferView\": 0, \"mimeType\": \"image/png\" }";

    private static ImportedMaterial ConvertMaterial(string materialJson)
    {
        var (root, resolver) = Load(materialJson);
        return GltfMaterialConverter.Convert(root.LogicalMaterials[0], resolver);
    }

    [Fact]
    public void Defaults_MapToGltfSpecDefaults()
    {
        var material = ConvertMaterial("{}");

        var payload = material.PbrMetallicRoughness!;
        Assert.Equal(System.Numerics.Vector4.One, payload.BaseColorFactor);
        Assert.Equal(1f, payload.MetallicFactor);
        Assert.Equal(1f, payload.RoughnessFactor);
        Assert.Equal(System.Numerics.Vector3.Zero, payload.EmissiveFactor);
        Assert.Equal(ImportedAlphaMode.Opaque, payload.AlphaMode);
        Assert.Equal(0.5f, payload.AlphaCutoff);
        Assert.False(payload.DoubleSided);
        Assert.Null(payload.BaseColorTexture);
        Assert.Equal(ImportedMaterial.PbrMetallicRoughnessFamily, material.ShaderFamily);
        Assert.Equal(ImportedCullMode.Back, material.CullMode);
        Assert.False(material.BlendEnabled);
    }

    [Fact]
    public void Factors_AndAlphaModes_Map()
    {
        var material = ConvertMaterial("""
            {
              "pbrMetallicRoughness": { "baseColorFactor": [1, 0, 0, 0.5], "metallicFactor": 0.25, "roughnessFactor": 0.75 },
              "emissiveFactor": [0.1, 0.2, 0.3],
              "alphaMode": "MASK", "alphaCutoff": 0.33, "doubleSided": true
            }
            """);

        var payload = material.PbrMetallicRoughness!;
        Assert.Equal(new System.Numerics.Vector4(1, 0, 0, 0.5f), payload.BaseColorFactor);
        Assert.Equal(0.25f, payload.MetallicFactor);
        Assert.Equal(0.75f, payload.RoughnessFactor);
        Assert.Equal(new System.Numerics.Vector3(0.1f, 0.2f, 0.3f), payload.EmissiveFactor);
        Assert.Equal(ImportedAlphaMode.Mask, payload.AlphaMode);
        Assert.Equal(0.33f, payload.AlphaCutoff, 5);
        Assert.True(payload.DoubleSided);
        Assert.Equal(ImportedCullMode.None, material.CullMode);
    }

    [Fact]
    public void BlendAlpha_EnablesBlendState()
    {
        var material = ConvertMaterial("{ \"alphaMode\": \"BLEND\" }");

        Assert.True(material.BlendEnabled);
        Assert.False(material.DepthWriteEnabled);
    }

    [Fact]
    public void ExternalImage_BecomesNormalizedVirtualPath()
    {
        var materialJson = """
            {
              "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } }
            }
            """;
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ " + materialJson + " ]," +
            "\"textures\": [ { \"source\": 0, \"sampler\": 0 } ]," +
            "\"images\": [ { \"uri\": \"textures/skin.png\" } ]," +
            "\"samplers\": [ { \"magFilter\": 9728, \"minFilter\": 9984, \"wrapS\": 33071, \"wrapT\": 33648 } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/skin.png", MinimalPng);
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        Assert.NotNull(document);
        var material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        var binding = material.PbrMetallicRoughness!.BaseColorTexture!;
        Assert.Equal(ImportedImageSourceKind.VirtualPath, binding.ImageSource.Kind);
        Assert.Equal("textures/skin.png", binding.ImageSource.VirtualPath);
        Assert.Equal(ImportedTextureRole.BaseColor, binding.Role);
        Assert.Equal(0, binding.UvSet);
    }

    [Fact]
    public void ExternalImageSampler_MapsWrapAndFilters()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0 } } } ]," +
            "\"textures\": [ { \"source\": 0, \"sampler\": 0 } ]," +
            "\"images\": [ { \"uri\": \"textures/skin.png\" } ]," +
            "\"samplers\": [ { \"magFilter\": 9728, \"minFilter\": 9984, \"wrapS\": 33071, \"wrapT\": 33648 } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/skin.png", MinimalPng);
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        var material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        var sampler = material.PbrMetallicRoughness!.BaseColorTexture!.Sampler;
        Assert.Equal(ImportedAddressMode.Clamp, sampler.AddressModeU);
        Assert.Equal(ImportedAddressMode.MirroredRepeat, sampler.AddressModeV);
        Assert.Equal(ImportedFilterMode.Nearest, sampler.MagFilter);
        Assert.Equal(ImportedFilterMode.Nearest, sampler.MinFilter);
        Assert.Equal(ImportedFilterMode.Nearest, sampler.MipFilter);
    }

    [Fact]
    public void MissingSampler_UsesGltfDefaults()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"textures/skin.png\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("textures/skin.png", MinimalPng);
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        var material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        var sampler = material.PbrMetallicRoughness!.BaseColorTexture!.Sampler;
        Assert.Equal(ImportedAddressMode.Repeat, sampler.AddressModeU);
        Assert.Equal(ImportedAddressMode.Repeat, sampler.AddressModeV);
        Assert.Equal(ImportedFilterMode.Linear, sampler.MinFilter);
        Assert.Equal(ImportedFilterMode.Linear, sampler.MagFilter);
        Assert.Equal(ImportedFilterMode.Linear, sampler.MipFilter);
    }

    [Fact]
    public void DataUriImage_BecomesEmbeddedBytes()
    {
        var b64 = Convert.ToBase64String(MinimalPng);
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0, \"texCoord\": 1 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + b64 + "\", \"name\": \"embedded skin\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        var material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        var binding = material.PbrMetallicRoughness!.BaseColorTexture!;
        Assert.Equal(ImportedImageSourceKind.EmbeddedBytes, binding.ImageSource.Kind);
        Assert.Equal(MinimalPng, binding.ImageSource.EmbeddedBytes.ToArray());
        Assert.Equal("image/png", binding.ImageSource.MimeType);
        Assert.Equal("embedded skin", binding.ImageSource.DiagnosticName);
        Assert.Equal(1, binding.UvSet);
    }

    [Fact]
    public void AllTextureRoles_Map()
    {
        var b64 = Convert.ToBase64String(MinimalPng);
        var image = "{ \"uri\": \"data:image/png;base64," + b64 + "\" }";
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ {" +
            "\"pbrMetallicRoughness\": {" +
            "\"baseColorTexture\": { \"index\": 0 }," +
            "\"metallicRoughnessTexture\": { \"index\": 0 } }," +
            "\"normalTexture\": { \"index\": 0, \"scale\": 2.5 }," +
            "\"occlusionTexture\": { \"index\": 0, \"strength\": 0.6 }," +
            "\"emissiveTexture\": { \"index\": 0 }" +
            "} ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ " + image + " ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        var material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        var payload = material.PbrMetallicRoughness!;
        Assert.NotNull(payload.BaseColorTexture);
        Assert.Equal(ImportedTextureRole.BaseColor, payload.BaseColorTexture!.Role);
        Assert.NotNull(payload.MetallicRoughnessTexture);
        Assert.Equal(ImportedTextureRole.MetallicRoughness, payload.MetallicRoughnessTexture!.Role);
        Assert.NotNull(payload.NormalTexture);
        Assert.Equal(2.5f, payload.NormalScale, 4);
        Assert.NotNull(payload.OcclusionTexture);
        Assert.Equal(0.6f, payload.OcclusionStrength, 4);
        Assert.NotNull(payload.EmissiveTexture);
        Assert.Equal(ImportedTextureRole.Emissive, payload.EmissiveTexture!.Role);
    }

    [Fact]
    public void TextureTransform_Throws()
    {
        var b64 = Convert.ToBase64String(MinimalPng);
        var json = "{ \"asset\": { \"version\": \"2.0\", \"extensionsUsed\": [\"KHR_texture_transform\"] }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0, \"extensions\": {" +
            "\"KHR_texture_transform\": { \"offset\": [0.5, 0.5] } } } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + b64 + "\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        // SharpGLTF Core has no extension assembly registered, so unsupported material
        // extensions fail strict parsing before the converter ever sees them.
        Assert.Throws<InvalidDataException>(() =>
            GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver));
    }

    [Fact]
    public void UnlitExtension_Throws()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\", \"extensionsUsed\": [\"KHR_materials_unlit\"] }," +
            "\"materials\": [ { \"extensions\": { \"KHR_materials_unlit\": {} } } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        Assert.Throws<InvalidDataException>(() =>
            GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver));
    }

    [Fact]
    public void UvSetTwo_Throws()
    {
        var b64 = Convert.ToBase64String(MinimalPng);
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0, \"texCoord\": 2 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + b64 + "\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);

        Assert.Throws<NotSupportedException>(() =>
            GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver));
    }

    [Fact]
    public void WebpImage_Throws()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/webp;base64,QUJD\", \"mimeType\": \"image/webp\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        Assert.Throws<InvalidDataException>(() =>
            GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver));
    }

    [Fact]
    public void UvValidation_RejectsMissingUvArray()
    {
        var material = ConvertMaterial("{}");

        GltfMaterialConverter.ValidateTextureBindingUvSets(material, hasTextureCoordinates0: true, hasTextureCoordinates1: false);

        var b64 = Convert.ToBase64String(MinimalPng);
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0, \"texCoord\": 1 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + b64 + "\" } ]" +
            "}";
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var resolver = new GltfResourceResolver("model.gltf", fileSystem, GltfImportOptions.Default);
        var document = GltfDocumentLoader.TryLoadDocument("model.gltf", context, GltfImportOptions.Default, resolver);
        var uv1Material = GltfMaterialConverter.Convert(document!.Root.LogicalMaterials[0], resolver);

        Assert.Throws<InvalidDataException>(() =>
            GltfMaterialConverter.ValidateTextureBindingUvSets(uv1Material, hasTextureCoordinates0: true, hasTextureCoordinates1: false));
    }

    [Fact]
    public void NullMaterial_UsesDefaultPbr()
    {
        var resolver = new GltfResourceResolver("model.gltf", new MemoryFileSystem(), GltfImportOptions.Default);

        var material = GltfMaterialConverter.Convert(null, resolver);

        Assert.NotNull(material.PbrMetallicRoughness);
        Assert.Equal(ImportedAlphaMode.Opaque, material.PbrMetallicRoughness!.AlphaMode);
    }
}
