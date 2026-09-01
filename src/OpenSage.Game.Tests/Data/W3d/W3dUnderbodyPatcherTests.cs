using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.FileFormats.Big;
using OpenSage.FileFormats.W3d;
using OpenSage.Mathematics;
using OpenSage.Tools.W3dPatcher;
using Xunit;

namespace OpenSage.Tests.Data.W3d;

public class W3dUnderbodyPatcherTests
{
    private const string CarEntryPath = @"Art\W3D\CVAsnCar1.W3D";

    private static byte[] ReadInstallFileBytes(string entryPath)
    {
        var bigFilePath = Path.Combine(InstalledFilesTestData.GetInstallationDirectory(SageGame.CncGenerals), "W3D.big");

        using var bigArchive = new BigArchive(bigFilePath);
        using var entryStream = bigArchive.GetEntry(entryPath)!.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    [GameFact(SageGame.CncGenerals)]
    public void WrittenCarFileReparsesAndIsByteStable()
    {
        var originalBytes = ReadInstallFileBytes(CarEntryPath);

        W3dFile original;
        using (var stream = new MemoryStream(originalBytes))
        {
            original = W3dFile.FromStream(stream, CarEntryPath);
        }

        byte[] firstWrite;
        using (var stream = new MemoryStream())
        {
            original.WriteTo(stream);
            firstWrite = stream.ToArray();
        }

        TestUtility.DoRoundtripTest(
            () => new MemoryStream(firstWrite),
            stream => W3dFile.FromStream(stream, CarEntryPath),
            (w3d, stream) => w3d.WriteTo(stream));
    }

    [GameFact(SageGame.CncGenerals)]
    public void PatchAddsUnderbodyMesh()
    {
        var originalBytes = ReadInstallFileBytes(CarEntryPath);

        W3dFile original;
        using (var stream = new MemoryStream(originalBytes))
        {
            original = W3dFile.FromStream(stream, CarEntryPath);
        }

        var patched = W3dUnderbodyPatcher.Patch(original);

        var underbody = Assert.IsType<W3dMesh>(patched.RenderableObjectsByName["CVASNCAR1.UNDERBODY"]);

        Assert.Equal(W3dUnderbodyPatcher.MeshName, underbody.Header.MeshName);
        Assert.Equal("CVASNCAR1", underbody.Header.ContainerName);
        Assert.Equal(4u, underbody.Header.NumVertices);
        Assert.Equal(4, underbody.Vertices.Items.Length);
        Assert.Equal(2u, underbody.Header.NumTris);
        Assert.Equal(2, underbody.Triangles.Items.Length);
        Assert.True(underbody.Header.Attributes.HasFlag(W3dMeshFlags.TwoSided));
        Assert.Equal(W3dVertexChannels.Location | W3dVertexChannels.Normal, underbody.Header.VertexChannels);

        Assert.Equal(1u, underbody.MaterialInfo.PassCount);
        Assert.Equal(1u, underbody.MaterialInfo.VertexMaterialCount);
        Assert.Equal(1u, underbody.MaterialInfo.ShaderCount);
        Assert.Equal(0u, underbody.MaterialInfo.TextureCount);
        Assert.Empty(underbody.MaterialPasses[0].TextureStages);
        Assert.Equal(W3dShaderTexturing.Disable, underbody.Shaders!.Items[0].Texturing);

        var expectedGrey = new ColorRgb(128, 128, 128);
        Assert.Equal(expectedGrey, underbody.VertexMaterials!.Items[0].Info.Diffuse);

        var bodySubObject = patched.HLod!.Lods[0].SubObjects.Single(s => s.Name == "CVASNCAR1.BOX02");
        var underbodySubObject = patched.HLod.Lods[0].SubObjects.Single(s => s.Name == "CVASNCAR1.UNDERBODY");
        Assert.Equal(bodySubObject.BoneIndex, underbodySubObject.BoneIndex);
        Assert.Equal(4u, patched.HLod.Lods[0].Header.ModelCount);
    }

    [GameFact(SageGame.CncGenerals)]
    public void PatchSingleMeshFileAddsHierarchyAndHlod()
    {
        var originalBytes = ReadInstallFileBytes(@"Art\W3D\CVSwatch01.W3D");

        W3dFile original;
        using (var stream = new MemoryStream(originalBytes))
        {
            original = W3dFile.FromStream(stream, "CVSwatch01.W3D");
        }

        Assert.Null(original.HLod);
        Assert.Single(original.RenderableObjects);

        var patched = W3dUnderbodyPatcher.Patch(original);

        Assert.NotNull(patched.HLod);
        Assert.NotNull(patched.Hierarchy);
        Assert.Equal(2, patched.RenderableObjects.Count);
        Assert.Equal(2u, patched.HLod!.Lods[0].Header.ModelCount);
        Assert.Equal(2, patched.HLod.Lods[0].SubObjects.Count);
        Assert.All(patched.HLod.Lods[0].SubObjects, s => Assert.Equal(0u, s.BoneIndex));

        var reparsed = TestUtility.DoRoundtripTest(
            () =>
            {
                var stream = new MemoryStream();
                patched.WriteTo(stream);
                return new MemoryStream(stream.ToArray());
            },
            stream => W3dFile.FromStream(stream, "CVSwatch01.W3D"),
            (w3d, stream) => w3d.WriteTo(stream));

        Assert.Equal(2, reparsed.RenderableObjects.Count);
        Assert.True(reparsed.RenderableObjectsByName.ContainsKey(".UNDERBODY"));
    }

    [GameFact(SageGame.CncGenerals)]
    public void PatchPlacesUnderbodyBelowRotatedBodyInModelSpace()
    {
        AssertUnderbodyPlacement(CarEntryPath, "CVASNCAR1.BOX02");
    }

    [GameFact(SageGame.CncGenerals)]
    public void PatchPlacesUnderbodyBelowIdentityBodyInModelSpace()
    {
        AssertUnderbodyPlacement(@"Art\W3D\CVSwatch01.W3D", ".CVSWATCH01");
    }

    [GameFact(SageGame.CncGenerals)]
    public void PatchedFileReparsesAndIsByteStable()
    {
        var originalBytes = ReadInstallFileBytes(CarEntryPath);

        W3dFile original;
        using (var stream = new MemoryStream(originalBytes))
        {
            original = W3dFile.FromStream(stream, CarEntryPath);
        }

        var patched = W3dUnderbodyPatcher.Patch(original);

        MemoryStream WritePatchedToFile()
        {
            var stream = new MemoryStream();
            patched.WriteTo(stream);
            return new MemoryStream(stream.ToArray());
        }

        var reparsed = TestUtility.DoRoundtripTest(
            WritePatchedToFile,
            stream => W3dFile.FromStream(stream, CarEntryPath),
            (w3d, stream) => w3d.WriteTo(stream));

        Assert.True(reparsed.RenderableObjectsByName.ContainsKey("CVASNCAR1.UNDERBODY"));

        var reparsedUnderbody = Assert.IsType<W3dMesh>(reparsed.RenderableObjectsByName["CVASNCAR1.UNDERBODY"]);
        Assert.Equal(4u, reparsedUnderbody.Header.NumVertices);
        Assert.Equal(2u, reparsedUnderbody.Header.NumTris);
        Assert.Equal("CVASNCAR1.UNDERBODY", reparsed.HLod!.Lods[0].SubObjects.Single(s => s.Name == "CVASNCAR1.UNDERBODY").Name);
    }

    private static void AssertUnderbodyPlacement(string entryPath, string bodySubObjectName)
    {
        var originalBytes = ReadInstallFileBytes(entryPath);
        W3dFile original;
        using (var stream = new MemoryStream(originalBytes))
        {
            original = W3dFile.FromStream(stream, entryPath);
        }

        var patched = W3dUnderbodyPatcher.Patch(original);
        var body = Assert.IsType<W3dMesh>(patched.RenderableObjectsByName[bodySubObjectName]);
        var underbodyName = $"{body.Header.ContainerName}.{W3dUnderbodyPatcher.MeshName}";
        var underbody = Assert.IsType<W3dMesh>(patched.RenderableObjectsByName[underbodyName]);
        var boneIndex = patched.HLod!.Lods[0].SubObjects
            .Single(subObject => subObject.Name == bodySubObjectName)
            .BoneIndex;
        var bodyToModel = GetBindPoseTransform(patched.Hierarchy!, boneIndex);
        var (bodyModelMin, bodyModelMax) = TransformBounds(body.Header.Min, body.Header.Max, bodyToModel);
        var modelVertices = underbody.Vertices.Items
            .Select(vertex => Vector3.Transform(vertex, bodyToModel))
            .ToArray();

        var underbodyZ = modelVertices[0].Z;
        Assert.All(modelVertices, vertex =>
            Assert.InRange(MathF.Abs(vertex.Z - underbodyZ), 0f, 0.001f));
        Assert.InRange(bodyModelMin.Z - underbodyZ, 0.099f, 0.101f);

        var underbodyMinX = modelVertices.Min(vertex => vertex.X);
        var underbodyMaxX = modelVertices.Max(vertex => vertex.X);
        var underbodyMinY = modelVertices.Min(vertex => vertex.Y);
        var underbodyMaxY = modelVertices.Max(vertex => vertex.Y);
        Assert.True(underbodyMinX > bodyModelMin.X);
        Assert.True(underbodyMaxX < bodyModelMax.X);
        Assert.True(underbodyMinY > bodyModelMin.Y);
        Assert.True(underbodyMaxY < bodyModelMax.Y);

        Assert.All(underbody.Normals!.Items, localNormal =>
        {
            var modelNormal = Vector3.Normalize(Vector3.TransformNormal(localNormal, bodyToModel));
            Assert.True(Vector3.Dot(modelNormal, -Vector3.UnitZ) > 0.999f);
        });

        Assert.All(underbody.Triangles.Items, triangle =>
        {
            var v0 = modelVertices[triangle.VIndex0];
            var v1 = modelVertices[triangle.VIndex1];
            var v2 = modelVertices[triangle.VIndex2];
            var faceNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            Assert.True(Vector3.Dot(faceNormal, -Vector3.UnitZ) > 0.999f);
        });
    }

    private static Matrix4x4 GetBindPoseTransform(W3dHierarchyDef hierarchy, uint boneIndex)
    {
        var result = Matrix4x4.Identity;
        var currentIndex = (int)boneIndex;
        while (currentIndex >= 0)
        {
            var pivot = hierarchy.Pivots.Items[currentIndex];
            result *= Matrix4x4.CreateFromQuaternion(pivot.Rotation)
                * Matrix4x4.CreateTranslation(pivot.Translation);
            currentIndex = pivot.ParentIdx;
        }
        return result;
    }

    private static (Vector3 Min, Vector3 Max) TransformBounds(
        in Vector3 min,
        in Vector3 max,
        in Matrix4x4 transform)
    {
        var transformedMin = new Vector3(float.MaxValue);
        var transformedMax = new Vector3(float.MinValue);
        for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            var corner = new Vector3(
                (cornerIndex & 1) == 0 ? min.X : max.X,
                (cornerIndex & 2) == 0 ? min.Y : max.Y,
                (cornerIndex & 4) == 0 ? min.Z : max.Z);
            var transformed = Vector3.Transform(corner, transform);
            transformedMin = Vector3.Min(transformedMin, transformed);
            transformedMax = Vector3.Max(transformedMax, transformed);
        }
        return (transformedMin, transformedMax);
    }
}
