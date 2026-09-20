#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Tests.Content;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class ExternalAssetInspectionTests
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static AssetImportContext CreateContext(params (string Path, string Content)[] files)
    {
        var fileSystem = new MemoryFileSystem();
        foreach (var (path, content) in files)
        {
            fileSystem.AddFile(path, content);
        }

        return new AssetImportContext(fileSystem, null);
    }

    private static string StaticModelBuffer()
    {
        var stream = new MemoryStream();
        foreach (var value in new float[]
                 {
                     0, 0, 0, 1, 0, 0, 0, 0, 1,
                     0, 0, 1, 1, 0, 0, 0, 0, 1,
                     0.25f, 0.5f, 0.75f, 0.25f, 0.5f, 0.75f,
                 })
        {
            stream.Write(BitConverter.GetBytes(value), 0, 4);
        }

        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(1);
        stream.WriteByte(0);
        stream.WriteByte(2);
        stream.WriteByte(0);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string B64(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static string BuildStaticModelJson()
    {
        return "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0," +
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 } ]," +
            "\"meshes\": [ { \"name\": \"hullMesh\", \"primitives\": [ { " +
            "\"attributes\": { \"POSITION\": 0, \"NORMAL\": 1, \"TEXCOORD_0\": 2 }, \"indices\": 3, \"material\": 0 } ] } ]," +
            "\"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0 } } } ]," +
            "\"textures\": [ { \"source\": 0 } ]," +
            "\"images\": [ { \"uri\": \"data:image/png;base64," + Convert.ToBase64String(MinimalPng) + "\" } ]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 0, 1] }," +
            "{ \"bufferView\": 1, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 2, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC2\" }," +
            "{ \"bufferView\": 3, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }" +
            "]," +
            "\"bufferViews\": [" +
            "{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 36, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 72, \"byteLength\": 24 }," +
            "{ \"buffer\": 0, \"byteOffset\": 96, \"byteLength\": 6 }" +
            "]," +
            "\"buffers\": [ { \"uri\": \"data:application/octet-stream;base64," + StaticModelBuffer() +
            "\", \"byteLength\": 102 } ]" +
            "}";
    }

    private static string BuildAnimatedModelJson()
    {
        // The static chunk is 102 bytes, a multiple of 3, so its base64 has no padding and
        // the animation chunk's base64 can be appended directly.
        var animationBytes = B64(
            0f, 1f,
            0f, 0f, 0f,
            1f, 0f, 0f);

        return "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0," +
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 } ]," +
            "\"meshes\": [ { \"primitives\": [ { " +
            "\"attributes\": { \"POSITION\": 0, \"NORMAL\": 1 }, \"indices\": 2 } ] } ]," +
            "\"animations\": [ { \"name\": \"drive\"," +
            "\"channels\": [ { \"sampler\": 0, \"target\": { \"node\": 0, \"path\": \"translation\" } } ]," +
            "\"samplers\": [ { \"input\": 3, \"output\": 4 } ] } ]," +
            "\"accessors\": [" +
            "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\", \"min\": [0, 0, 0], \"max\": [1, 0, 1] }," +
            "{ \"bufferView\": 1, \"componentType\": 5126, \"count\": 3, \"type\": \"VEC3\" }," +
            "{ \"bufferView\": 2, \"componentType\": 5123, \"count\": 3, \"type\": \"SCALAR\" }," +
            "{ \"bufferView\": 3, \"componentType\": 5126, \"count\": 2, \"type\": \"SCALAR\", \"min\": [0], \"max\": [1] }," +
            "{ \"bufferView\": 4, \"componentType\": 5126, \"count\": 2, \"type\": \"VEC3\" }" +
            "]," +
            "\"bufferViews\": [" +
            "{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 36, \"byteLength\": 36 }," +
            "{ \"buffer\": 0, \"byteOffset\": 96, \"byteLength\": 6 }," +
            "{ \"buffer\": 0, \"byteOffset\": 102, \"byteLength\": 8 }," +
            "{ \"buffer\": 0, \"byteOffset\": 110, \"byteLength\": 24 }" +
            "]," +
            "\"buffers\": [ { \"uri\": \"data:application/octet-stream;base64," +
            StaticModelBuffer() + animationBytes +
            "\", \"byteLength\": 134 } ]" +
            "}";
    }

    [Theory]
    [InlineData("tank.gltf")]
    [InlineData("tank.glb")]
    [InlineData("TANK.GLB")]
    [InlineData("dir/sub/model.gltf")]
    public void SelectFormat_GltfExtensions_Succeed(string fileName)
    {
        var result = ExternalAssetInspection.SelectFormat(fileName);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Selection);
        Assert.Equal(ExternalAssetInspection.GltfFormatId, result.Selection!.FormatId);
        Assert.Same(GltfImportOptions.Default, result.Selection.Options);
        Assert.IsType<GltfExternalAssetInspector>(result.Selection.Inspector);
    }

    [Theory]
    [InlineData("tank.obj")]
    [InlineData("tank")]
    [InlineData("tank.gltf.bak")]
    [InlineData("tank.gltf,glb")]
    [InlineData("")]
    [InlineData(" ")]
    public void SelectFormat_UnknownExtensions_ReturnExt001(string fileName)
    {
        var result = ExternalAssetInspection.SelectFormat(fileName);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Selection);
        Assert.Equal(ExternalFormatSelectionResult.UnsupportedFormatCode, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectFormat_HonorsExplicitOptions()
    {
        var options = new GltfImportOptions(unitsPerMeter: 2f);

        var result = ExternalAssetInspection.SelectFormat("tank.gltf", options);

        Assert.True(result.IsSuccess);
        Assert.Same(options, result.Selection!.Options);
    }

    [Fact]
    public void InspectModel_InvalidKey_ReturnsNull()
    {
        var inspector = new GltfExternalAssetInspector(GltfImportOptions.Default);
        var context = CreateContext(("models/tank.gltf", BuildStaticModelJson()));

        Assert.Null(inspector.InspectModel("models/tank.w3d", context));
        Assert.Null(inspector.InspectModel("models/tank.gltf?x=1", context));
    }

    [Fact]
    public void InspectModel_MissingRoot_ReturnsNull()
    {
        var inspector = new GltfExternalAssetInspector(GltfImportOptions.Default);
        var context = CreateContext();

        Assert.Null(inspector.InspectModel("models/tank.gltf", context));
    }

    [Fact]
    public void InspectModel_StaticModel_CatalogMatchesImport()
    {
        var options = GltfImportOptions.Default;
        var inspector = new GltfExternalAssetInspector(options);
        var context = CreateContext(("models/tank.gltf", BuildStaticModelJson()));

        var catalog = inspector.InspectModel("models/tank.gltf", context);

        Assert.NotNull(catalog);
        Assert.Equal("tank.gltf", catalog!.SourceDisplayName);
        Assert.Equal(ExternalAssetInspection.GltfFormatId, catalog.FormatId);
        Assert.Equal(1, catalog.SceneCount);
        Assert.Equal(1, catalog.NodeCount);
        Assert.Equal(1, catalog.PrimitiveCount);
        Assert.Equal(1, catalog.MaterialCount);
        Assert.Equal(1, catalog.ImageCount);
        Assert.Equal(0, catalog.SkinCount);
        Assert.Equal(0, catalog.JointCount);
        Assert.Empty(catalog.Animations);
        Assert.Empty(catalog.RequiredFeatureIds);
        Assert.Empty(catalog.Diagnostics);

        var model = new GltfModelImporter(options).ImportModel("models/tank.gltf", context);
        Assert.NotNull(model);
        var mesh = (ImportedMesh)model!.SubObjects[0].RenderObject;

        Assert.Equal(mesh.Vertices.Count, catalog.VertexCount);
        Assert.Equal(mesh.Indices.Count, catalog.IndexCount);
        Assert.Equal(mesh.BoundingBox.Min, catalog.Bounds.Min);
        Assert.Equal(mesh.BoundingBox.Max, catalog.Bounds.Max);
        Assert.Equal(mesh.BoundingSphere.Center, catalog.Bounds.SphereCenter);
        Assert.Equal(mesh.BoundingSphere.Radius, catalog.Bounds.SphereRadius);
    }

    [Fact]
    public void InspectModel_AnimationDescriptors_MatchImporter()
    {
        var options = GltfImportOptions.Default;
        var inspector = new GltfExternalAssetInspector(options);
        var context = CreateContext(("models/rig.gltf", BuildAnimatedModelJson()));

        var catalog = inspector.InspectModel("models/rig.gltf", context);

        Assert.NotNull(catalog);
        var descriptor = Assert.Single(catalog!.Animations);

        Assert.Equal("models/rig.gltf#animation=0", descriptor.Key);
        Assert.True(GltfAssetKey.TryParseAnimation(descriptor.Key, out var animationKey));
        Assert.Equal("models/rig.gltf", animationKey.ModelPath);
        Assert.Equal(0, animationKey.AnimationIndex);

        var imported = new GltfAnimationImporter(options)
            .ImportAnimation(descriptor.Key, context);
        Assert.NotNull(imported);
        Assert.Equal(imported!.Name, descriptor.Name);
        Assert.Equal(imported.Duration, descriptor.Duration);
        Assert.Equal(imported.Channels.Count, descriptor.ChannelCount);
    }

    [Fact]
    public void InspectModel_EnforcesSameLimits_AsImporter()
    {
        var options = new GltfImportOptions(maxNodeCount: 1);
        var json = BuildStaticModelJson().Replace(
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 } ],",
            "\"nodes\": [ { \"name\": \"hull\", \"mesh\": 0 }, { \"name\": \"turret\" } ],");
        var context = CreateContext(("models/tank.gltf", json));

        var inspector = new GltfExternalAssetInspector(options);
        var importer = new GltfModelImporter(options);

        Assert.Throws<InvalidDataException>(() => inspector.InspectModel("models/tank.gltf", context));
        Assert.Throws<InvalidDataException>(() => importer.ImportModel("models/tank.gltf", context));
    }

    [Fact]
    public void InspectModel_NoConvertibleGeometry_ThrowsLikeImporter()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0," +
            "\"nodes\": [ { \"name\": \"hull\" } ] }";
        var context = CreateContext(("models/empty.gltf", json));
        var inspector = new GltfExternalAssetInspector(GltfImportOptions.Default);

        Assert.Throws<InvalidDataException>(
            () => inspector.InspectModel("models/empty.gltf", context));
    }

    [Fact]
    public void Catalog_IsImmutable_AgainstInputMutations()
    {
        var features = new List<string> { "KHR_a" };
        var animations = new List<ExternalAnimationDescriptor>
        {
            new("model.gltf#animation=0", "idle", TimeSpan.FromSeconds(1), 1),
        };
        var diagnostics = new List<ExternalAssetInspectionDiagnostic>
        {
            new("GLTF0001", ExternalInspectionDiagnosticSeverity.Warning, "ignored"),
        };

        var catalog = new ExternalAssetCatalog(
            "model.gltf",
            "GLTF",
            1, 1, 1, 3, 3, 1, 1, 0, 0,
            new ExternalModelBounds(Vector3.Zero, Vector3.One, new Vector3(0.5f), 0.8660254f),
            features,
            features,
            animations,
            diagnostics);

        features.Add("KHR_b");
        animations.Add(new("model.gltf#animation=1", "walk", TimeSpan.FromSeconds(2), 2));
        diagnostics.Add(new("GLTF0002", ExternalInspectionDiagnosticSeverity.Warning, "second"));

        Assert.Single(catalog.RequiredFeatureIds);
        Assert.Single(catalog.OptionalFeatureIds);
        Assert.Single(catalog.Animations);
        Assert.Single(catalog.Diagnostics);
    }

    [Fact]
    public void Catalog_ExposesNoParserOrImporterTypes()
    {
        AssertNoExternalTypes(typeof(ExternalAssetCatalog));
        AssertNoExternalTypes(typeof(ExternalAnimationDescriptor));
        AssertNoExternalTypes(typeof(ExternalModelBounds));
        AssertNoExternalTypes(typeof(ExternalAssetInspectionDiagnostic));

        var selectionProperties = typeof(ExternalFormatSelection).GetProperties();
        Assert.DoesNotContain(selectionProperties, property =>
            property.PropertyType == typeof(IModelImporter) ||
            property.PropertyType == typeof(IAnimationImporter));
    }

    private static void AssertNoExternalTypes(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            var propertyType = property.PropertyType;
            Assert.False(
                propertyType.Namespace?.StartsWith("SharpGLTF", StringComparison.Ordinal) ?? false,
                $"{type.Name}.{property.Name} exposes {propertyType.FullName}");
        }
    }

    [Fact]
    public void Factory_MapsGltfOnly()
    {
        Assert.True(ExternalFormatFactory.TryCreateModelImporter(
            ExternalAssetInspection.GltfFormatId,
            GltfImportOptions.Default,
            null,
            out var modelImporter));
        Assert.IsType<GltfModelImporter>(modelImporter);

        Assert.True(ExternalFormatFactory.TryCreateAnimationImporter(
            ExternalAssetInspection.GltfFormatId,
            GltfImportOptions.Default,
            null,
            out var animationImporter));
        Assert.IsType<GltfAnimationImporter>(animationImporter);

        Assert.False(ExternalFormatFactory.TryCreateModelImporter(
            "W3D",
            GltfImportOptions.Default,
            null,
            out var unknownModelImporter));
        Assert.Null(unknownModelImporter);

        Assert.False(ExternalFormatFactory.TryCreateAnimationImporter(
            "W3D",
            GltfImportOptions.Default,
            null,
            out var unknownAnimationImporter));
        Assert.Null(unknownAnimationImporter);
    }
}
