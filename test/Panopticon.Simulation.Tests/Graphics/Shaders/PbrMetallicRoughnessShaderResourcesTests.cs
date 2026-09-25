using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;
using Panopticon.Tests.Content.Importing;
using Xunit;

namespace Panopticon.Tests.Graphics.Shaders;

public sealed class PbrMetallicRoughnessShaderResourcesTests
{
    [GraphicsFact]
    public void ShaderSet_CompilesAndExposesCompleteMaterialLayout()
    {
        using var graphics = new TestGraphicsContext();

        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);

        var expectedBindings = new (string Name, int Binding)[]
        {
            ("MaterialConstants", 0),
            ("BaseColorTexture", 1),
            ("MetallicRoughnessTexture", 2),
            ("NormalTexture", 3),
            ("OcclusionTexture", 4),
            ("EmissiveTexture", 5),
            ("BaseColorSampler", 6),
            ("MetallicRoughnessSampler", 7),
            ("NormalSampler", 8),
            ("OcclusionSampler", 9),
            ("EmissiveSampler", 10),
        };

        foreach (var (name, binding) in expectedBindings)
        {
            var resourceBinding = resources.MaterialResourceBindings[name];
            Assert.Equal(binding, (int)resourceBinding.Binding);
        }

        Assert.Equal(PbrMetallicRoughnessShaderResources.MaterialConstantsBufferSize,
            (int)resources.MaterialResourceBindings["MaterialConstants"].Type.Size);
    }

    [GraphicsFact]
    public void EquivalentPayloads_ReuseOneMaterialAndPipeline()
    {
        using var graphics = new TestGraphicsContext();
        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);

        var first = resources.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(metallicFactor: 0.25f),
            graphics.StandardResources);
        var second = resources.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(metallicFactor: 0.25f),
            graphics.StandardResources);

        Assert.Same(first, second);
        Assert.Same(first.Pipeline, second.Pipeline);
    }

    [GraphicsFact]
    public void EveryOutputAffectingDimension_SeparatesTheCache()
    {
        using var graphics = new TestGraphicsContext();
        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);
        var standard = graphics.StandardResources;

        var white = new PbrMetallicRoughnessShaderResources.RoleBinding(
            standard.SolidWhiteTexture, standard.LinearClampSampler);
        var black = new PbrMetallicRoughnessShaderResources.RoleBinding(
            standard.SolidBlackTexture, standard.LinearClampSampler);
        var point = new PbrMetallicRoughnessShaderResources.RoleBinding(
            standard.SolidWhiteTexture, standard.PointClampSampler);

        var baseColorTexture = new ImportedTextureBinding(
            ImportedImageSource.CreateVirtualPath("textures/base.png"), 0, new ImportedSampler(), ImportedTextureRole.BaseColor);
        var baseColorTextureUv1 = new ImportedTextureBinding(
            ImportedImageSource.CreateVirtualPath("textures/base.png"), 1, new ImportedSampler(), ImportedTextureRole.BaseColor);
        var normalTexture = new ImportedTextureBinding(
            ImportedImageSource.CreateVirtualPath("textures/normal.png"), 0, new ImportedSampler(), ImportedTextureRole.Normal);

        Material Material(ImportedPbrMetallicRoughness payload,
            PbrMetallicRoughnessShaderResources.RoleBinding? baseColor = null,
            PbrMetallicRoughnessShaderResources.RoleBinding? normal = null)
        {
            return resources.GetCachedMaterial(payload, standard, baseColor, normal: normal);
        }

        var reference = Material(new ImportedPbrMetallicRoughness(), white);

        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(baseColorFactor: new Vector4(0.5f, 1, 1, 1)), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(metallicFactor: 0.5f), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(roughnessFactor: 0.5f), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(normalScale: 2.0f), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(occlusionStrength: 0.5f), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(emissiveFactor: new Vector3(1, 0, 0)), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(alphaCutoff: 0.75f), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(doubleSided: true), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), white));
        Assert.NotSame(reference, Material(new ImportedPbrMetallicRoughness(normalTexture: normalTexture), white));

        Assert.NotSame(
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), white),
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), black));
        Assert.NotSame(
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), white),
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), point));
        Assert.NotSame(
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTexture), white),
            Material(new ImportedPbrMetallicRoughness(baseColorTexture: baseColorTextureUv1), white));
    }

    [GraphicsFact]
    public void AlphaMode_DrivesPipelineBlendDepthWriteAndSurfaceType()
    {
        using var graphics = new TestGraphicsContext();
        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);
        var standard = graphics.StandardResources;

        var opaque = resources.GetCachedMaterial(new ImportedPbrMetallicRoughness(), standard);
        var masked = resources.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(alphaMode: ImportedAlphaMode.Mask, alphaCutoff: 0.5f), standard);
        var blended = resources.GetCachedMaterial(
            new ImportedPbrMetallicRoughness(alphaMode: ImportedAlphaMode.Blend), standard);

        // Opaque and masked share depth-writing pipelines; blend gets a distinct pipeline and
        // sorts into the transparent bucket via its render key.
        Assert.Same(opaque.Pipeline, masked.Pipeline);
        Assert.NotSame(opaque.Pipeline, blended.Pipeline);
        Assert.NotEqual(opaque.RenderKey, blended.RenderKey);
    }
}
