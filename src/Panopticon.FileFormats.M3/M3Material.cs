using System.Collections.Generic;

namespace Panopticon.FileFormats.M3;

public sealed record M3Material(
    string Name,
    uint Flags,
    uint BlendMode,
    float Specularity,
    uint AlphaTestThreshold,
    IReadOnlyList<M3Layer?> Layers)
{
    public M3Layer? GetLayer(M3MaterialLayerSlot slot)
    {
        return Layers[(int)slot];
    }
}
