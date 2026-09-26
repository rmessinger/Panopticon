using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class ImportedTangentGeneratorTests
{
    [Fact]
    public void Generation_IsDeterministic_And_ProducesOrthonormalBasis()
    {
        var mesh = CreateQuadMesh();

        var first = ImportedTangentGenerator.GenerateTangents(mesh);
        var second = ImportedTangentGenerator.GenerateTangents(mesh);

        for (var i = 0; i < first.Vertices.Count; i++)
        {
            Assert.Equal(first.Vertices[i].Tangent, second.Vertices[i].Tangent);
            Assert.Equal(first.Vertices[i].Binormal, second.Vertices[i].Binormal);
        }

        foreach (var vertex in first.Vertices)
        {
            Assert.NotNull(vertex.Tangent);
            Assert.NotNull(vertex.Binormal);
            var tangent = vertex.Tangent!.Value;
            var binormal = vertex.Binormal!.Value;

            Assert.True(float.IsFinite(tangent.X) && float.IsFinite(tangent.Y) && float.IsFinite(tangent.Z));
            Assert.Equal(1.0f, tangent.Length(), 5);
            Assert.Equal(1.0f, binormal.Length(), 5);
            Assert.Equal(0.0f, Vector3.Dot(vertex.Normal, tangent), 5);
            Assert.Equal(0.0f, Vector3.Dot(vertex.Normal, binormal), 5);
            Assert.Equal(0.0f, Vector3.Dot(tangent, binormal), 5);
        }

        // Planar quad with identity-mapped UVs: tangent +X, binormal +Y.
        Assert.Equal(Vector3.UnitX, first.Vertices[0].Tangent!.Value);
        Assert.Equal(Vector3.UnitY, first.Vertices[0].Binormal!.Value);
    }

    [Fact]
    public void Source_Data_Is_Not_Mutated()
    {
        var mesh = CreateQuadMesh();
        var originalTangent = mesh.Vertices[0].Tangent;

        var generated = ImportedTangentGenerator.GenerateTangents(mesh);

        Assert.Null(originalTangent);
        Assert.NotSame(mesh, generated);
        Assert.Null(mesh.Vertices[0].Tangent);
        Assert.NotNull(generated.Vertices[0].Tangent);
        Assert.Equal(mesh.Indices, generated.Indices);
        Assert.Equal(mesh.Vertices[0].Position, generated.Vertices[0].Position);
        Assert.Equal(mesh.Vertices[0].Normal, generated.Vertices[0].Normal);
    }

    [Fact]
    public void MirroredUvs_FlipBinormalHandedness()
    {
        var mesh = CreateQuadMesh();

        // Mirror the quad's V coordinate across all vertices.
        var mirrored = WithUv0(
            mesh,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(0, 0));

        var normal = ImportedTangentGenerator.GenerateTangents(mesh);
        var mirroredResult = ImportedTangentGenerator.GenerateTangents(mirrored);

        // Both bases stay orthonormal; the flipped V gradient keeps the tangent and flips the
        // binormal handedness.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(normal.Vertices[i].Tangent!.Value.X, mirroredResult.Vertices[i].Tangent!.Value.X, 5);
            Assert.Equal(normal.Vertices[i].Tangent!.Value.Y, mirroredResult.Vertices[i].Tangent!.Value.Y, 5);
            Assert.Equal(
                -normal.Vertices[i].Binormal!.Value.X,
                mirroredResult.Vertices[i].Binormal!.Value.X,
                5);
            Assert.Equal(
                -normal.Vertices[i].Binormal!.Value.Y,
                mirroredResult.Vertices[i].Binormal!.Value.Y,
                5);
        }
    }

    [Fact]
    public void SharedVertex_WithConflictingUvs_Fails()
    {
        var materials = new[]
        {
            CreateNormalMappedMaterial(uvSet: 0),
            CreateNormalMappedMaterial(uvSet: 0),
        };

        var vertices = CreateQuadVertices();
        var mesh = new ImportedMesh(
            vertices,
            [0, 1, 2, 0, 2, 3],
            new AxisAlignedBoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 0)),
            new BoundingSphere(new Vector3(0.5f, 0.5f, 0), 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: materials,
            meshParts:
            [
                new ImportedMeshPart(
                    textureCoordinates0: new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                    textureCoordinates1: null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
                new ImportedMeshPart(
                    // Vertex 2 shared with part 0, but its UV differs.
                    textureCoordinates0: new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 1) },
                    textureCoordinates1: null,
                    startIndex: 3,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 1),
            ]);

        var exception = Assert.Throws<NotSupportedException>(() =>
            ImportedTangentGenerator.GenerateTangents(mesh));
        Assert.Contains("conflicting UV", exception.Message);
    }

    [Fact]
    public void DisjointParts_MaySelect_DifferentUvSets()
    {
        // Two disjoint triangles: part 0 uses UV set 0, part 1 uses UV set 1, on separate vertices.
        var vertices = new[]
        {
            new ImportedVertex(new Vector3(0, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 1, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(0, 0, 2), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 0, 2), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 1, 2), Vector3.UnitZ),
        };

        var mesh = new ImportedMesh(
            vertices,
            [0, 1, 2, 3, 4, 5],
            new AxisAlignedBoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 2)),
            new BoundingSphere(new Vector3(0.5f, 0.5f, 1), 2.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: new[]
            {
                CreateNormalMappedMaterial(uvSet: 0),
                CreateNormalMappedMaterial(uvSet: 1),
            },
            meshParts:
            [
                new ImportedMeshPart(
                    textureCoordinates0: new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0) },
                    textureCoordinates1: null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
                new ImportedMeshPart(
                    textureCoordinates0: null,
                    textureCoordinates1: new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) },
                    startIndex: 3,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 1),
            ]);

        var generated = ImportedTangentGenerator.GenerateTangents(mesh);

        Assert.NotNull(generated.Vertices[0].Tangent);
        Assert.NotNull(generated.Vertices[3].Tangent);
    }

    [Fact]
    public void DegenerateUvTriangle_Fails()
    {
        var mesh = WithUv0(
            CreateQuadMesh(),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f));

        var exception = Assert.Throws<NotSupportedException>(() =>
            ImportedTangentGenerator.GenerateTangents(mesh));
        Assert.Contains("degenerate UV", exception.Message);
    }

    [Fact]
    public void MeshWithoutNormalMappedParts_Fails()
    {
        var mesh = CreateQuadMesh();
        var plain = new ImportedMesh(
            mesh.Vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: new[] { CreatePbrMaterialWithoutNormalMap() },
            meshParts: mesh.MeshParts);

        var exception = Assert.Throws<NotSupportedException>(() =>
            ImportedTangentGenerator.GenerateTangents(plain));
        Assert.Contains("nothing to generate", exception.Message);
    }

    [Fact]
    public void Generation_PreservesAllFourSkinSlots()
    {
        var quad = CreateQuadMesh();
        var skinnedVertices = new ImportedVertex[quad.Vertices.Count];
        for (var i = 0; i < quad.Vertices.Count; i++)
        {
            var source = quad.Vertices[i];
            skinnedVertices[i] = new ImportedVertex(
                source.Position,
                source.Normal,
                boneIndex0: 0,
                boneIndex1: 1,
                boneIndex2: 2,
                boneIndex3: 3,
                boneWeight0: 0.4f,
                boneWeight1: 0.3f,
                boneWeight2: 0.2f,
                boneWeight3: 0.1f);
        }

        var skinned = new ImportedMesh(
            skinnedVertices,
            quad.Indices,
            quad.BoundingBox,
            quad.BoundingSphere,
            quad.Hidden,
            quad.CameraOriented,
            quad.HasHouseColor,
            isSkinned: true,
            quad.Materials,
            quad.MeshParts);

        var generated = ImportedTangentGenerator.GenerateTangents(skinned);

        for (var i = 0; i < generated.Vertices.Count; i++)
        {
            var vertex = generated.Vertices[i];
            Assert.Equal(2u, vertex.BoneIndex2);
            Assert.Equal(3u, vertex.BoneIndex3);
            Assert.Equal(0.2f, vertex.BoneWeight2);
            Assert.Equal(0.1f, vertex.BoneWeight3);
        }
    }

    private static ImportedVertex[] CreateQuadVertices()
    {
        return
        [
            new ImportedVertex(new Vector3(0, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 0, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(1, 1, 0), Vector3.UnitZ),
            new ImportedVertex(new Vector3(0, 1, 0), Vector3.UnitZ),
        ];
    }

    private static ImportedMesh CreateQuadMesh()
    {
        return WithUv0(
            CreateBaseQuad(),
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1));
    }

    private static ImportedMesh CreateBaseQuad()
    {
        return new ImportedMesh(
            CreateQuadVertices(),
            [0, 1, 2, 0, 2, 3],
            new AxisAlignedBoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 0)),
            new BoundingSphere(new Vector3(0.5f, 0.5f, 0), 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: [CreateNormalMappedMaterial(uvSet: 0)],
            meshParts:
            [
                new ImportedMeshPart(
                    textureCoordinates0: new[] { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero },
                    textureCoordinates1: null,
                    startIndex: 0,
                    indexCount: 6,
                    blendEnabled: false,
                    materialIndex: 0),
            ]);
    }

    private static ImportedMesh WithUv0(
        ImportedMesh mesh,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Vector2 uv3)
    {
        return new ImportedMesh(
            mesh.Vertices,
            mesh.Indices,
            mesh.BoundingBox,
            mesh.BoundingSphere,
            mesh.Hidden,
            mesh.CameraOriented,
            mesh.HasHouseColor,
            mesh.IsSkinned,
            mesh.Materials,
            new[]
            {
                new ImportedMeshPart(
                    textureCoordinates0: [uv0, uv1, uv2, uv3],
                    textureCoordinates1: null,
                    mesh.MeshParts[0].StartIndex,
                    mesh.MeshParts[0].IndexCount,
                    mesh.MeshParts[0].BlendEnabled,
                    mesh.MeshParts[0].MaterialIndex),
            });
    }

    private static ImportedMaterial CreateNormalMappedMaterial(int uvSet)
    {
        return ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(
                normalTexture: new ImportedTextureBinding(
                    ImportedImageSource.CreateVirtualPath("textures/normal.png"),
                    uvSet,
                    new ImportedSampler(),
                    ImportedTextureRole.Normal)));
    }

    private static ImportedMaterial CreatePbrMaterialWithoutNormalMap()
    {
        return ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness());
    }
}
