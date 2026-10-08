using System;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Tests.Content.Importing;
using Panopticon.Tests.Graphics.Proportions;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class PresentationScaleTests
{
    [Fact]
    public void AircraftOverrides_MatchManifestDerivation()
    {
        var manifest = ReferenceScaleManifest.Load();

        foreach (var expected in new[] { "ChinaJetMiG", "AmericaVehicleComanche" })
        {
            var entry = manifest.Objects.Find(o => o.ObjectName == expected);
            Assert.NotNull(entry);

            var renderedMeters = (double)(entry.Extents.X / manifest.Baseline.UnitsPerMeter);
            var multiplier = (double)entry.ReferenceMeters!.Value / renderedMeters;

            Assert.Equal(multiplier, VisualScaleOverrides.GetScale(expected), 3);
        }
    }

    [Fact]
    public void UnknownOrNullNames_ReturnIdentity()
    {
        Assert.Equal(1f, VisualScaleOverrides.GetScale("AmericaTankCrusader"));
        Assert.Equal(1f, VisualScaleOverrides.GetScale("SomeFutureAircraft"));
        Assert.Equal(1f, VisualScaleOverrides.GetScale(null));
        Assert.Equal(1f, VisualScaleOverrides.GetScale(string.Empty));
    }

    [Fact]
    public void Keys_AreCaseInsensitive()
    {
        Assert.Equal(VisualScaleOverrides.GetScale("ChinaJetMiG"), VisualScaleOverrides.GetScale("chinajetmig"));
    }

    [GraphicsFact]
    public void IdentityBuild_MatchesBoneAbsoluteMatrixExactly()
    {
        using var context = new TestGraphicsContext();
        using var instance = CreateModelInstance(context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        var renderList = new RenderList();
        instance.BuildRenderList(
            renderList,
            CreateCamera(),
            castsShadow: false,
            CreateItemConstants(),
            visualScale: 1f);

        Assert.Equal(1, renderList.Opaque.RenderItems.Length);
        Assert.Equal(instance.AbsoluteBoneTransforms[0], renderList.Opaque.RenderItems[0].World);
    }

    [GraphicsFact]
    public void ScaledBuild_ScalesWorldAndCullingBounds_LeavesSharedMatricesUnchanged()
    {
        using var context = new TestGraphicsContext();
        using var instance = CreateModelInstance(context);
        instance.Update(new TimeInterval(TimeSpan.Zero, TimeSpan.Zero));

        var boneAbsolute = instance.AbsoluteBoneTransforms[0];
        var unscaledBounds = renderBounds(instance);

        var renderList = new RenderList();
        instance.BuildRenderList(
            renderList,
            CreateCamera(),
            castsShadow: false,
            CreateItemConstants(),
            visualScale: 2f);

        Assert.Equal(1, renderList.Opaque.RenderItems.Length);
        var item = renderList.Opaque.RenderItems[0];

        // Presentation scale composes about the model origin before the world matrix.
        Assert.Equal(Matrix4x4.CreateScale(2f) * boneAbsolute, item.World);

        // Culling bounds follow the scaled world, so enlarged aircraft stay visible.
        Assert.Equal(unscaledBounds.Max.X - unscaledBounds.Min.X, (item.BoundingBox.Max.X - item.BoundingBox.Min.X) / 2f, 5);

        // Shared matrices are untouched: gameplay queries and later identity builds are unaffected.
        Assert.Equal(boneAbsolute, instance.AbsoluteBoneTransforms[0]);

        var identityList = new RenderList();
        instance.BuildRenderList(
            identityList,
            CreateCamera(),
            castsShadow: false,
            CreateItemConstants(),
            visualScale: 1f);
        Assert.Equal(boneAbsolute, identityList.Opaque.RenderItems[0].World);
    }

    private static AxisAlignedBoundingBox renderBounds(ModelInstance instance)
    {
        var renderList = new RenderList();
        instance.BuildRenderList(
            renderList,
            CreateCamera(),
            castsShadow: false,
            CreateItemConstants(),
            visualScale: 1f);

        return renderList.Opaque.RenderItems[0].BoundingBox;
    }

    private static Camera CreateCamera()
    {
        return new Camera(() => new Viewport(0, 0, 1024, 768, 0, 1));
    }

    private static MeshShaderResources.RenderItemConstantsPS CreateItemConstants()
    {
        return new MeshShaderResources.RenderItemConstantsPS { Opacity = 1f };
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

    private static ModelInstance CreateModelInstance(TestGraphicsContext context)
    {
        var mesh = CreateImportedMesh();
        var model = new ImportedModel(
            "PresentationScaleModel",
            new ImportedSkeleton(
                "Skeleton",
                new[] { new ImportedBone("Root", -1, new Vector3(5f, 0f, 3f), Quaternion.Identity) }),
            new[]
            {
                new ImportedSubObject(
                    "PresentationScaleModel.Mesh",
                    "Mesh",
                    parentBoneIndex: 0,
                    renderObject: mesh),
            },
            ImportedSkeletonOrigin.Embedded,
            enabledFeatureGates: null);

        return new ModelInstance(
            ImportedModelMaterializer.MaterializeModel(model, context.Context),
            context.Context);
    }

    private static ImportedMesh CreateImportedMesh()
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
            primitiveId: null);
    }

}
