using System.IO;
using System.Linq;
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
}
