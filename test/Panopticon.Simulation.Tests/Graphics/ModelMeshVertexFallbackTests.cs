using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public sealed class ModelMeshVertexFallbackTests
{
    [Fact]
    public void VerticesWithoutSecondaryGeometryDeformAtThePrimaryPosition()
    {
        // Granny/NIF/glTF vertices carry no secondary position; the second skin influence
        // evaluates in_Position1, so it must fall back to the primary geometry. Falling
        // back to zero collapses multi-influence vertices toward the origin.
        var vertices = ModelMesh.CreateVertices(
        [
            new ImportedVertex(
                new Vector3(1f, 2f, 3f),
                new Vector3(0f, 0f, 1f),
                boneIndex0: 0,
                boneIndex1: 1,
                boneWeight0: 0.7f,
                boneWeight1: 0.3f),
        ]);

        var vertex = vertices[0];
        Assert.Equal(new Vector3(1f, 2f, 3f), vertex.Position1);
        Assert.Equal(new Vector3(0f, 0f, 1f), vertex.Normal1);
    }

    [Fact]
    public void VerticesWithSecondaryGeometryKeepIt()
    {
        // W3D two-position skinning stores an alternate pose position used with the
        // second influence; it must survive into the vertex buffer.
        var vertices = ModelMesh.CreateVertices(
        [
            new ImportedVertex(
                new Vector3(1f, 2f, 3f),
                new Vector3(0f, 0f, 1f),
                secondaryPosition: new Vector3(4f, 5f, 6f),
                secondaryNormal: new Vector3(0f, 1f, 0f),
                boneIndex0: 0,
                boneIndex1: 1,
                boneWeight0: 0.5f,
                boneWeight1: 0.5f),
        ]);

        var vertex = vertices[0];
        Assert.Equal(new Vector3(4f, 5f, 6f), vertex.Position1);
        Assert.Equal(new Vector3(0f, 1f, 0f), vertex.Normal1);
    }
}
