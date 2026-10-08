using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;
using Panopticon.Tests.Content.Importing;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Shaders;

/// <summary>
/// Water reflection/refraction framebuffers stay single-sample under MSAA (G4), so every
/// producer feeding the Opaque bucket must expose a pipeline variant matching those targets.
/// Veldrid pipelines do not expose their output description publicly; the variant is asserted
/// structurally and by the headless water run.
/// </summary>
[Collection("GraphicsTests")]
public sealed class SingleSamplePipelineVariantTests
{
    [GraphicsFact]
    public void EnhancedStore_DerivesSingleSampleOutputDescription()
    {
        using var graphics = new TestGraphicsContext();

        using var enhancedStore = new ShaderSetStore(
            graphics.Device,
            RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Enhanced()));

        Assert.Equal(TextureSampleCount.Count4, enhancedStore.OutputDescription.SampleCount);
        Assert.Equal(TextureSampleCount.Count1, enhancedStore.SingleSampleOutputDescription.SampleCount);
        Assert.Equal(enhancedStore.OutputDescription.DepthAttachment, enhancedStore.SingleSampleOutputDescription.DepthAttachment);
        Assert.Equal(enhancedStore.OutputDescription.ColorAttachments[0], enhancedStore.SingleSampleOutputDescription.ColorAttachments[0]);
    }

    [GraphicsFact]
    public void EnhancedStore_FixedFunctionAndPbrMaterials_ExposeSingleSampleVariants()
    {
        using var graphics = new TestGraphicsContext();

        using var enhancedStore = new ShaderSetStore(
            graphics.Device,
            RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Enhanced()));

        using var fixedFunction = new FixedFunctionShaderResources(enhancedStore);

        var fixedFunctionMaterial = fixedFunction.GetCachedMaterial(
            FaceCullMode.Back,
            depthWriteEnabled: true,
            ComparisonKind.LessEqual,
            blendEnabled: false,
            BlendFactor.One,
            BlendFactor.Zero,
            BlendFactor.Zero,
            default,
            graphics.StandardResources.SolidWhiteTexture,
            graphics.StandardResources.SolidWhiteTexture);

        Assert.NotSame(fixedFunctionMaterial.Pipeline, fixedFunctionMaterial.SingleSamplePipeline);

        using var pbr = new PbrMetallicRoughnessShaderResources(enhancedStore);

        var pbrMaterial = pbr.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(),
            graphics.StandardResources);

        Assert.NotSame(pbrMaterial.Pipeline, pbrMaterial.SingleSamplePipeline);
    }

    [GraphicsFact]
    public void EnhancedStore_ShaderMaterialFamily_BuildsDistinctSingleSamplePipeline()
    {
        using var graphics = new TestGraphicsContext();

        using var enhancedStore = new ShaderSetStore(
            graphics.Device,
            RenderPipeline.CreateGameOutputDescription(GraphicsSettings.Enhanced()));

        using var normalMapped = new NormalMappedShaderResources(enhancedStore);

        Assert.NotSame(normalMapped.Pipeline, normalMapped.SingleSamplePipeline);
    }

    [GraphicsFact]
    public void SingleSampleStore_ReusesTheMainPipelineAsVariant()
    {
        using var graphics = new TestGraphicsContext();

        using var pbr = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);

        var material = pbr.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(),
            graphics.StandardResources);

        Assert.Same(material.Pipeline, material.SingleSamplePipeline);
    }
}
