using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class ModelMorphTests
{
    // Graphics-free tests for the CPU morph core.

    [Fact]
    public void Apply_AddsWeightedDeltasAtWeightsZeroHalfAndOne()
    {
        var set = ModelMorphTargetSet.Create(CreateImportedSet(), vertexCount: 3);
        var baseVertices = CreateBaseVertices();
        var result = new MeshShaderResources.MeshVertex.Basic[3];

        set.Apply(baseVertices, new float[] { 0f, 0f }, result);
        Assert.Equal(new Vector3(0, 0, 0), result[0].Position0);
        Assert.Equal(new Vector3(1, 0, 0), result[1].Position0);
        Assert.Equal(new Vector3(0, 1, 0), result[2].Position0);

        set.Apply(baseVertices, new float[] { 1f, 0f }, result);
        Assert.Equal(new Vector3(1, 0, 0), result[0].Position0);
        Assert.Equal(new Vector3(1, 2, 0), result[1].Position0);
        Assert.Equal(new Vector3(0, 1, 3), result[2].Position0);

        set.Apply(baseVertices, new float[] { 0.5f, 0.5f }, result);
        Assert.Equal(new Vector3(0.5f, 0, 0.5f), result[0].Position0);
        Assert.Equal(new Vector3(1, 1, 0.5f), result[1].Position0);
        Assert.Equal(new Vector3(0, 1, 2), result[2].Position0);

        set.Apply(baseVertices, new float[] { 0f, 1f }, result);
        Assert.Equal(new Vector3(0, 0, 1), result[0].Position0);
        Assert.Equal(new Vector3(1, 0, 1), result[1].Position0);
        Assert.Equal(new Vector3(0, 1, 1), result[2].Position0);

        // Position-only deltas preserve base normals (first C6 proof policy).
        Assert.Equal(Vector3.UnitZ, result[0].Normal0);
        Assert.Equal(Vector3.UnitZ, result[1].Normal0);
        Assert.Equal(Vector3.UnitZ, result[2].Normal0);
    }

    [Fact]
    public void Apply_ValidatesWeightLengthAndVertexCount()
    {
        var set = ModelMorphTargetSet.Create(CreateImportedSet(), vertexCount: 3);
        var baseVertices = CreateBaseVertices();
        var result = new MeshShaderResources.MeshVertex.Basic[3];

        Assert.Throws<ArgumentException>(() => set.Apply(baseVertices, new float[] { 1f }, result));
        Assert.Throws<ArgumentException>(() =>
            set.Apply(baseVertices.AsSpan(0, 2).ToArray(), new float[] { 1f, 1f }, result));
    }

    [Fact]
    public void Create_RejectsNormalAndTangentDeltas()
    {
        var normalTarget = new ImportedMorphTarget(null, normalDeltas: new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero });
        var importedSet = new ImportedMorphTargetSet(
            "morph_primitive",
            new[] { "Target1" },
            new[] { 0f },
            new[] { new ImportedMorphWeightRange(0f, 1f) },
            new[] { normalTarget });

        var exception = Assert.Throws<NotSupportedException>(
            () => ModelMorphTargetSet.Create(importedSet, vertexCount: 3));
        Assert.Contains("position deltas only", exception.Message);
    }

    [Fact]
    public void Create_ValidatesDeltaCountAgainstVertexCount()
    {
        var shortTarget = new ImportedMorphTarget(new[] { Vector3.One, Vector3.One });
        var importedSet = new ImportedMorphTargetSet(
            "morph_primitive",
            new[] { "Target1" },
            new[] { 0f },
            new[] { new ImportedMorphWeightRange(0f, 1f) },
            new[] { shortTarget });

        Assert.Throws<ArgumentException>(() => ModelMorphTargetSet.Create(importedSet, vertexCount: 3));
    }

    // Headless device tests for the per-instance runtime path.

    [Fact]
    public void MorphEnabledInstance_OwnsDynamicBufferAndDefaultWeights()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateMorphModelInstance(context, withGate: true);

        var meshInstance = instance.MeshInstances[0];
        Assert.NotNull(meshInstance.MorphVertexBuffer);
        Assert.NotNull(meshInstance.MorphedVertices);

        // Default weights (0) leave the base positions; the static model buffer stays intact.
        Assert.Equal(new Vector3(0, 0, 0), meshInstance.MorphedVertices![0].Position0);
        Assert.Equal(new Vector3(1, 0, 0), meshInstance.MorphedVertices[1].Position0);
        Assert.Equal(new Vector3(0, 1, 0), meshInstance.MorphedVertices[2].Position0);
    }

    [Fact]
    public void SetMorphWeights_RebuildsCpuVertices()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateMorphModelInstance(context, withGate: true);
        var meshInstance = instance.MeshInstances[0];

        meshInstance.SetMorphWeights(new float[] { 1f, 0f });
        Assert.Equal(new Vector3(1, 0, 0), meshInstance.MorphedVertices![0].Position0);
        Assert.Equal(new Vector3(1, 2, 0), meshInstance.MorphedVertices[1].Position0);
        Assert.Equal(new Vector3(0, 1, 3), meshInstance.MorphedVertices[2].Position0);

        meshInstance.SetMorphWeights(new float[] { 0.5f, 0.5f });
        Assert.Equal(new Vector3(0.5f, 0, 0.5f), meshInstance.MorphedVertices[0].Position0);
        Assert.Equal(new Vector3(1, 1, 0.5f), meshInstance.MorphedVertices[1].Position0);
        Assert.Equal(new Vector3(0, 1, 2), meshInstance.MorphedVertices[2].Position0);
    }

    [Fact]
    public void SetMorphWeights_ValidatesLengthAndRequiresMorphMesh()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = CreateMorphModelInstance(context, withGate: true);
        Assert.Throws<ArgumentException>(() => instance.MeshInstances[0].SetMorphWeights(new float[] { 1f }));

        var plainInstance = new ModelInstance(
            ImportedModelMaterializer.MaterializeModel(CreateImportedModel(withMorph: false), context.Context),
            context.Context);
        Assert.Throws<InvalidOperationException>(() => plainInstance.MeshInstances[0].SetMorphWeights(new float[] { 1f }));
    }

    [Fact]
    public void FeatureDisabledMaterialization_BlocksMorphModelNamingTheGate()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var exception = Assert.Throws<NotSupportedException>(
            () => ImportedModelMaterializer.MaterializeModel(CreateImportedModel(withMorph: true), context.Context));

        Assert.Contains("MorphTargets", exception.Message);
    }

    [Fact]
    public void NoMorphMesh_KeepsStaticBufferPathOnly()
    {
        if (!GraphicsDeviceUtility.CanCreateHeadlessDevice())
        {
            return;
        }

        using var context = new TestGraphicsContext();
        var instance = new ModelInstance(
            ImportedModelMaterializer.MaterializeModel(CreateImportedModel(withMorph: false), context.Context),
            context.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        Assert.Null(instance.MeshInstances[0].MorphVertexBuffer);
        Assert.Null(instance.MeshInstances[0].MorphedVertices);
    }

    private static ModelInstance CreateMorphModelInstance(TestGraphicsContext context, bool withGate)
    {
        var model = ImportedModelMaterializer.MaterializeModel(
            CreateImportedModel(withMorph: true, withGate: withGate),
            context.Context);
        var instance = new ModelInstance(model, context.Context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));
        return instance;
    }

    private static ImportedModel CreateImportedModel(bool withMorph, bool withGate = false)
    {
        var skeleton = new ImportedSkeleton(
            "TestSkeleton",
            new[] { new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity) });

        return new ImportedModel(
            "TestModel",
            skeleton,
            new[]
            {
                new ImportedSubObject(
                    "TestModel.Mesh",
                    "Mesh",
                    parentBoneIndex: 0,
                    renderObject: CreateMesh(withMorph)),
            },
            ImportedSkeletonOrigin.Embedded,
            withGate ? new[] { ImportedFeatureGates.MorphTargets } : null);
    }

    private static ImportedMesh CreateMesh(bool withMorph)
    {
        var material = new ImportedMaterial(
            ImportedMaterial.FixedFunctionFamily,
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: new ImportedFixedFunctionShading(
                ImportedDiffuseLightingType.Modulate,
                specularEnabled: false,
                texturingEnabled: false,
                secondaryTextureColorBlend: ImportedSecondaryTextureBlend.Disable,
                secondaryTextureAlphaBlend: ImportedSecondaryTextureBlend.Disable,
                alphaTest: false,
                ambient: Vector3.One,
                diffuse: Vector3.One,
                specular: Vector3.Zero,
                emissive: Vector3.Zero,
                shininess: 0.0f,
                opacity: 1.0f,
                textureStageCount: 0,
                CreateDefaultTextureMapping(),
                CreateDefaultTextureMapping()),
            properties: Array.Empty<ImportedMaterialProperty>());

        return new ImportedMesh(
            new[]
            {
                new ImportedVertex(new Vector3(0, 0, 0), Vector3.UnitZ),
                new ImportedVertex(new Vector3(1, 0, 0), Vector3.UnitZ),
                new ImportedVertex(new Vector3(0, 1, 0), Vector3.UnitZ),
            },
            new ushort[] { 0, 1, 2 },
            new AxisAlignedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
            new BoundingSphere(Vector3.Zero, 1.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: false,
            materials: new[] { material },
            meshParts: new[]
            {
                new ImportedMeshPart(
                    new Vector2[] { new(0, 0), new(1, 0), new(0, 1) },
                    null,
                    startIndex: 0,
                    indexCount: 3,
                    blendEnabled: false,
                    materialIndex: 0),
            },
            primitiveId: withMorph ? "TestModel.Mesh" : null,
            morphTargetSets: withMorph ? new[] { CreateImportedSet() } : null);
    }

    private static ImportedMorphTargetSet CreateImportedSet()
    {
        return new ImportedMorphTargetSet(
            "TestModel.Mesh",
            new[] { "Target1", "Target2" },
            new[] { 0f, 0f },
            new[] { new ImportedMorphWeightRange(0f, 1f), new ImportedMorphWeightRange(0f, 1f) },
            new[]
            {
                new ImportedMorphTarget(new[]
                {
                    new Vector3(1, 0, 0),
                    new Vector3(0, 2, 0),
                    new Vector3(0, 0, 3),
                }),
                new ImportedMorphTarget(new[]
                {
                    new Vector3(0, 0, 1),
                    new Vector3(0, 0, 1),
                    new Vector3(0, 0, 1),
                }),
            });
    }

    private static MeshShaderResources.MeshVertex.Basic[] CreateBaseVertices()
    {
        return new[]
        {
            new MeshShaderResources.MeshVertex.Basic { Position0 = new Vector3(0, 0, 0), Normal0 = Vector3.UnitZ },
            new MeshShaderResources.MeshVertex.Basic { Position0 = new Vector3(1, 0, 0), Normal0 = Vector3.UnitZ },
            new MeshShaderResources.MeshVertex.Basic { Position0 = new Vector3(0, 1, 0), Normal0 = Vector3.UnitZ },
        };
    }

    private static ImportedTextureMapping CreateDefaultTextureMapping()
    {
        return new ImportedTextureMapping(
            ImportedTextureMappingType.Uv,
            Vector2.Zero,
            Vector2.One,
            Vector2.Zero,
            Vector2.One,
            Vector2.One,
            Vector2.Zero,
            Vector2.One,
            speed: 0.1f,
            fps: 1.0f,
            log2Width: 1,
            stepsPerSecond: 0.0f);
    }
}
