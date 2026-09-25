#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.Tests.Content;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class GltfAnimationImporterTests
{
    private static readonly float FeetPerMeter = 1f / 0.3048f;

    private static string B64(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static string DataUri(params float[] values)
    {
        return "\"data:application/octet-stream;base64," + B64(values) + "\", \"byteLength\": " + values.Length * 4;
    }

    /// <summary>
    /// Builds a one-channel animation document. Times occupy the first accessor bytes; values
    /// follow in the second buffer view.
    /// </summary>
    private static string BuildDocument(
        string animationName,
        string path,
        string interpolation,
        float[] times,
        float[] values,
        int valueCount,
        string valueType,
        string node = "{ \"name\": \"root\", \"translation\": [1, 2, 3] }")
    {
        return "{ \"asset\": { \"version\": \"2.0\" }," +
            "\"animations\": [ { " + (animationName == "" ? "" : "\"name\": \"" + animationName + "\", ") +
            "\"channels\": [ { \"sampler\": 0, \"target\": { \"node\": 0, \"path\": \"" + path + "\" } } ]," +
            "\"samplers\": [ { \"input\": 0, \"output\": 1" +
            (interpolation == "" ? "" : ", \"interpolation\": \"" + interpolation + "\"") + " } ] } ]," +
            "\"accessors\": [" +
            " { \"bufferView\": 0, \"componentType\": 5126, \"count\": " + times.Length + ", \"type\": \"SCALAR\", \"max\": [" + times.Max() + "], \"min\": [" + times.Min() + "] }," +
            " { \"bufferView\": 1, \"componentType\": 5126, \"count\": " + valueCount + ", \"type\": \"" + valueType + "\" }" +
            "]," +
            "\"bufferViews\": [" +
            " { \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": " + times.Length * 4 + " }," +
            " { \"buffer\": 0, \"byteOffset\": " + times.Length * 4 + ", \"byteLength\": " + values.Length * 4 + " }" +
            "]," +
            "\"buffers\": [ { \"uri\": " + DataUri([.. times, .. values]) + " } ]," +
            "\"nodes\": [ " + node + " ]," +
            "\"scenes\": [ { \"nodes\": [0] } ]," +
            "\"scene\": 0" +
            "}";
    }

    private static ImportedAnimation Import(string json)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var importer = new GltfAnimationImporter(GltfImportOptions.Default);
        return importer.ImportAnimation("model.gltf#animation=0", context)!;
    }

    private static void AssertThrowsImport(string json, Type exceptionType)
    {
        var fileSystem = new MemoryFileSystem();
        fileSystem.AddFile("model.gltf", json);
        var context = new AssetImportContext(fileSystem, null);
        var importer = new GltfAnimationImporter(GltfImportOptions.Default);

        Assert.Throws(exceptionType, () => importer.ImportAnimation("model.gltf#animation=0", context));
    }

    [Fact]
    public void FallbackName_IsUsedWhenAnimationIsUnnamed()
    {
        var json = BuildDocument(
            animationName: "",
            path: "translation",
            interpolation: "",
            times: [0f, 1f],
            values: [1f, 2f, 3f, 1f, 2f, 3f],
            valueCount: 2,
            valueType: "VEC3");

        var imported = Import(json);

        Assert.Equal("model.Animation0", imported.Name);
    }

    [Fact]
    public void TranslationChannel_ConvertsToNamedOffsetChannels_ReconstructsAuthoredSample()
    {
        // Rest translation (1, 2, 3); the first sample moves to (1, 3, 3).
        var json = BuildDocument(
            animationName: "move",
            path: "translation",
            interpolation: "LINEAR",
            times: [0f, 1f],
            values: [1f, 3f, 3f, 1f, 2f, 4f],
            valueCount: 2,
            valueType: "VEC3");

        var imported = Import(json);

        Assert.Equal("move", imported.Name);
        Assert.Equal(TimeSpan.FromSeconds(1), imported.Duration);

        Assert.Equal(3, imported.Channels.Count);
        foreach (var channel in imported.Channels)
        {
            Assert.Equal(ImportedAnimationTargetKind.BoneName, channel.Target.Kind);
            Assert.Equal("root", channel.Target.BoneName);
            Assert.Equal(2, channel.Keyframes.Count);
            Assert.Equal(ImportedAnimationInterpolation.Linear, channel.Interpolation);
        }

        var x = imported.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationX);
        var y = imported.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationY);
        var z = imported.Channels.Single(c => c.ChannelType == ImportedAnimationChannelType.TranslationZ);

        // glTF +Y is Panopticon +Z, so the authored Y delta of one meter lands in Z.
        Assert.Equal(0f, x.Keyframes[0].FloatValue, 4);
        Assert.Equal(0f, y.Keyframes[0].FloatValue, 4);
        Assert.Equal(FeetPerMeter, z.Keyframes[0].FloatValue, 2);

        // Reconstruction: offset matrix * rest matrix equals the converted animated matrix.
        var restMatrix = Matrix4x4.CreateTranslation(3 * FeetPerMeter, 1 * FeetPerMeter, 2 * FeetPerMeter);
        var animatedMatrix = Matrix4x4.CreateTranslation(3 * FeetPerMeter, 1 * FeetPerMeter, 3 * FeetPerMeter);
        var offsetMatrix = Matrix4x4.CreateTranslation(
            x.Keyframes[0].FloatValue,
            y.Keyframes[0].FloatValue,
            z.Keyframes[0].FloatValue) * restMatrix;

        Assert.Equal(animatedMatrix.Translation, offsetMatrix.Translation);
    }

    [Fact]
    public void RotationChannel_ConvertsWithStepInterpolation()
    {
        var rest = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var half = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);

        var node = "{ \"name\": \"root\", \"rotation\": [" + rest.X + ", " + rest.Y + ", " + rest.Z + ", " + rest.W + "] }";
        var json = BuildDocument(
            animationName: "spin",
            path: "rotation",
            interpolation: "STEP",
            times: [0f, 2f],
            values:
            [
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2).X,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2).Y,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2).Z,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2).W,
                half.X, half.Y, half.Z, half.W,
            ],
            valueCount: 2,
            valueType: "VEC4",
            node: node);

        var imported = Import(json);

        var channel = Assert.Single(imported.Channels);
        Assert.Equal(ImportedAnimationChannelType.Quaternion, channel.ChannelType);
        Assert.Equal(ImportedAnimationInterpolation.Step, channel.Interpolation);
        Assert.Equal(TimeSpan.FromSeconds(2), imported.Duration);

        // Reconstruction: offset rotation matrix * rest rotation matrix equals the converted
        // animated rotation matrix.
        var basis = GltfTransformConverter.CreateBasis(FeetPerMeter);
        var restMatrix = Matrix4x4.CreateFromQuaternion(GltfTransformConverter.ConvertRotation(rest, basis));
        var animatedMatrix = Matrix4x4.CreateFromQuaternion(GltfTransformConverter.ConvertRotation(half, basis));
        var offsetMatrix = Matrix4x4.CreateFromQuaternion(channel.Keyframes[1].QuaternionValue) * restMatrix;

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(animatedMatrix[i, i], offsetMatrix[i, i], 3);
        }
    }

    [Fact]
    public void IdentityScaleChannel_IsOmitted_LeavingEmptyAnimationInvalid()
    {
        var json = BuildDocument(
            animationName: "scale",
            path: "scale",
            interpolation: "",
            times: [0f, 1f],
            values: [1f, 1f, 1f, 1f, 1f, 1f],
            valueCount: 2,
            valueType: "VEC3");

        AssertThrowsImport(json, typeof(InvalidDataException));
    }

    [Fact]
    public void NonIdentityScaleChannel_Throws()
    {
        var json = BuildDocument(
            animationName: "scale",
            path: "scale",
            interpolation: "",
            times: [0f, 1f],
            values: [1f, 1f, 1f, 2f, 2f, 2f],
            valueCount: 2,
            valueType: "VEC3");

        AssertThrowsImport(json, typeof(NotSupportedException));
    }

    [Fact]
    public void CubicSpline_Throws()
    {
        // CUBICSPLINE output carries three values per key.
        var json = BuildDocument(
            animationName: "curve",
            path: "translation",
            interpolation: "CUBICSPLINE",
            times: [0f, 1f],
            values: new float[18],
            valueCount: 6,
            valueType: "VEC3");

        AssertThrowsImport(json, typeof(NotSupportedException));
    }

    [Fact]
    public void WeightChannel_Throws()
    {
        var json = BuildDocument(
            animationName: "morph",
            path: "weights",
            interpolation: "",
            times: [0f, 1f],
            values: [0f, 1f],
            valueCount: 2,
            valueType: "SCALAR");

        AssertThrowsImport(json, typeof(NotSupportedException));
    }

    [Fact]
    public void IndexOutOfRange_Throws()
    {
        var json = "{ \"asset\": { \"version\": \"2.0\" }, \"animations\": [], \"nodes\": [ { \"name\": \"root\" } ], \"scenes\": [ { \"nodes\": [0] } ], \"scene\": 0 }";

        AssertThrowsImport(json, typeof(InvalidDataException));
    }

    [Fact]
    public void UnnamedTargetNode_Throws()
    {
        var json = BuildDocument(
            animationName: "move",
            path: "translation",
            interpolation: "",
            times: [0f, 1f],
            values: [1f, 3f, 3f, 1f, 2f, 4f],
            valueCount: 2,
            valueType: "VEC3",
            node: "{ \"translation\": [1, 2, 3] }");

        AssertThrowsImport(json, typeof(InvalidDataException));
    }

    [Fact]
    public void InvalidKey_ReturnsNull()
    {
        var context = new AssetImportContext(new MemoryFileSystem(), null);
        var importer = new GltfAnimationImporter(GltfImportOptions.Default);

        Assert.Null(importer.ImportAnimation("model.gltf", context));
        Assert.Null(importer.ImportAnimation("model.w3d#animation=0", context));
    }

    [Fact]
    public void MissingRoot_ReturnsNull()
    {
        var context = new AssetImportContext(new MemoryFileSystem(), null);
        var importer = new GltfAnimationImporter(GltfImportOptions.Default);

        Assert.Null(importer.ImportAnimation("model.gltf#animation=0", context));
    }

    [Fact]
    public void NamedChannels_BindToReorderedHierarchy()
    {
        var channel = new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName("root"),
            ImportedAnimationChannelType.TranslationX,
            new[] { ImportedKeyframe.FromFloat(TimeSpan.Zero, 5f) });
        var imported = new ImportedAnimation("move", TimeSpan.FromSeconds(1), [channel]);
        var animation = Panopticon.Content.Loaders.ImportedAnimationMaterializer.Materialize(imported);

        var hierarchy = new Panopticon.Graphics.ModelBoneHierarchy(new ImportedSkeleton(
            "reordered",
            new[]
            {
                new ImportedBone("other", -1, Vector3.Zero, Quaternion.Identity),
                new ImportedBone("root", 0, Vector3.Zero, Quaternion.Identity),
            }));

        var bound = animation.Bind(hierarchy);

        var clip = Assert.Single(bound.Clips);
        Assert.Equal(1, clip.Bone);
    }
}
