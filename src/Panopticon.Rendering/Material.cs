using Veldrid;

namespace Panopticon.Rendering;

public sealed class Material : DisposableBase
{
    public readonly ushort Id;
    public readonly ShaderSet ShaderSet;
    public readonly Pipeline Pipeline;

    public ResourceSet MaterialResourceSet { get; private set; }

    /// <summary>
    /// Variant of <see cref="Pipeline"/> targeting the single-sample output description, for
    /// passes that render into Count1 framebuffers while the store runs multisampled (water
    /// reflection/refraction maps). Null when the owner provides no such variant.
    /// </summary>
    public readonly Pipeline? SingleSamplePipeline;

    public readonly int RenderKey;

    public Material(
        ShaderSet shaderSet,
        Pipeline pipeline,
        ResourceSet materialResourceSet,
        SurfaceType surfaceType)
        : this(shaderSet, pipeline, null, materialResourceSet, surfaceType)
    {
    }

    public Material(
        ShaderSet shaderSet,
        Pipeline pipeline,
        Pipeline? singleSamplePipeline,
        ResourceSet materialResourceSet,
        SurfaceType surfaceType)
    {
        Id = shaderSet.GetNextMaterialId();

        ShaderSet = shaderSet;
        Pipeline = pipeline;
        SingleSamplePipeline = singleSamplePipeline;
        MaterialResourceSet = materialResourceSet;

        // Bit 31: SurfaceType
        RenderKey |= ((int)surfaceType) << 31;

        // Bit 23-30: ShaderSet
        RenderKey |= (ShaderSet.Id << 23);

        // Bit 15-22: Material
        RenderKey |= (Id) << 15;
    }

    /// <summary>
    /// Swaps the bound resource set after a world-sampler rebuild (WI-G7-02). Material identity
    /// stays stable so mesh parts and render lists keep working across settings changes.
    /// </summary>
    public void UpdateMaterialResourceSet(ResourceSet materialResourceSet)
    {
        MaterialResourceSet = materialResourceSet;
    }
}

public enum SurfaceType : byte
{
    Opaque = 0,
    Transparent = 1,
}
