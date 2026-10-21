using System;

namespace Panopticon.Tools.TestHarness;

public static class TerrainZoomPath
{
    public const int FrameCount = 33;
    public const float NearHeight = 260f;
    public const float FarHeight = 520f;

    public static float HeightAt(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameCount);
        var t = frame / (float)(FrameCount - 1);
        var distance = 1f - MathF.Abs(2f * t - 1f);
        return NearHeight + (FarHeight - NearHeight) * distance;
    }
}
