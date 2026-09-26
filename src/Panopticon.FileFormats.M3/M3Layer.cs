namespace Panopticon.FileFormats.M3;

public enum M3MaterialLayerSlot
{
    Diffuse,
    Decal,
    Specular,
    Emissive1,
    Emissive2,
    Environment,
    EnvironmentMask,
    Alpha1,
    Alpha2,
    Normal,
    Height,
    LightMap,
    AmbientOcclusion,
}

public sealed record M3Layer(
    string Path,
    uint Flags,
    uint UvSource,
    uint ColorChannelSelector);
