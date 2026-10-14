using System;
using System.Numerics;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Tests.Content.Importing;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Rendering.Instancing;

/// <summary>
/// B5-2: the instancing seam. Shader families that ship instanced variants expose them on
/// their materials with a distinct pipeline and item layout, blended materials never do,
/// and eligible render items with the same mesh-part identity sort contiguously so runs
/// form. GPU-backed cases run on the headless device; sort semantics are pure CPU.
/// </summary>
[Collection("GraphicsTests")]
public sealed class InstancingSeamTests
{
    [GraphicsFact]
    public void FixedFunction_OpaqueMaterial_ExposesInstancedVariant()
    {
        using var graphics = new TestGraphicsContext();
        var fixedFunction = graphics.ShaderResources.FixedFunction;

        var opaque = fixedFunction.GetCachedMaterial(
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

        Assert.NotNull(opaque.InstancedPipeline);
        Assert.NotSame(opaque.Pipeline, opaque.InstancedPipeline);
        Assert.NotNull(opaque.InstancedItemLayout);
    }

    [GraphicsFact]
    public void FixedFunction_BlendedMaterial_HasNoInstancedVariant()
    {
        using var graphics = new TestGraphicsContext();
        var fixedFunction = graphics.ShaderResources.FixedFunction;

        var blended = fixedFunction.GetCachedMaterial(
            FaceCullMode.Back,
            depthWriteEnabled: false,
            ComparisonKind.LessEqual,
            blendEnabled: true,
            BlendFactor.SourceAlpha,
            BlendFactor.InverseSourceAlpha,
            BlendFactor.InverseSourceAlpha,
            default,
            graphics.StandardResources.SolidWhiteTexture,
            graphics.StandardResources.SolidWhiteTexture);

        Assert.Null(blended.InstancedPipeline);
        Assert.Null(blended.InstancedItemLayout);
    }

    [GraphicsFact]
    public void MeshDepth_Material_ExposesInstancedVariant()
    {
        using var graphics = new TestGraphicsContext();

        var material = graphics.ShaderResources.MeshDepth.Material;

        Assert.NotNull(material.InstancedPipeline);
        Assert.NotSame(material.Pipeline, material.InstancedPipeline);
        Assert.NotNull(material.InstancedItemLayout);
    }

    /// <summary>
    /// The seam is not W3D-specific: the PBR family (glTF and StarCraft II imports) flows
    /// through the same instanced pipeline exposure.
    /// </summary>
    [GraphicsFact]
    public void PbrMetallicRoughness_OpaqueMaterial_ExposesInstancedVariant()
    {
        using var graphics = new TestGraphicsContext();
        var pbr = graphics.ShaderResources.PbrMetallicRoughness;

        var material = pbr.GetCachedMaterial(
            new Panopticon.Content.Importing.ImportedPbrMetallicRoughness(),
            graphics.StandardResources);

        Assert.NotNull(material.InstancedPipeline);
        Assert.NotSame(material.Pipeline, material.InstancedPipeline);
        Assert.NotNull(material.InstancedItemLayout);
    }

    [GraphicsFact]
    public void EligibleItems_WithSamePartIdentity_SortContiguously()
    {
        using var graphics = new TestGraphicsContext();
        var material = graphics.ShaderResources.MeshDepth.Material;

        var items = new[]
        {
            MakeItem(material, partId: 7, eligible: true, ordering: 0),
            MakeItem(material, partId: 3, eligible: true, ordering: 1),
            MakeItem(material, partId: 7, eligible: true, ordering: 2),
            MakeItem(material, partId: 3, eligible: true, ordering: 3),
        };

        Array.Sort(items);

        // Same material key: the part-identity bits group part 7 together and part 3
        // together, independent of insertion order.
        Assert.Equal(3, items[0].Key & 0xFFFF);
        Assert.Equal(3, items[1].Key & 0xFFFF);
        Assert.Equal(7, items[2].Key & 0xFFFF);
        Assert.Equal(7, items[3].Key & 0xFFFF);
    }

    [GraphicsFact]
    public void IneligibleItems_KeepZeroPartBits()
    {
        using var graphics = new TestGraphicsContext();
        var material = graphics.ShaderResources.MeshDepth.Material;

        var item = MakeItem(material, partId: 7, eligible: false, ordering: 0);

        Assert.Equal(0, item.Key & 0xFFFF);
    }

    private static RenderItem MakeItem(Panopticon.Rendering.Material material, int partId, bool eligible, int ordering) =>
        new(
            $"item{ordering}",
            material,
            default,
            Matrix4x4.Identity,
            startIndex: 0,
            indexCount: 6,
            indexBuffer: null!,
            BeforeRenderDelegateStub,
            eligible,
            default,
            meshConstantsBuffer: null,
            partId);

    private static void BeforeRenderDelegateStub(CommandList commandList, in RenderItem renderItem)
    {
    }
}
