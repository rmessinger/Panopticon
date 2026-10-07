namespace Panopticon;

/// <summary>
/// Conversions between logical points (OS window and mouse coordinates) and physical pixels
/// (render surfaces). See docs/roadmap/deep-dives/g1-high-dpi-resolution.md.
/// </summary>
internal static class DpiScaling
{
    public static float GetScale(int pixelWidth, int logicalWidth)
    {
        return logicalWidth > 0 && pixelWidth > 0
            ? pixelWidth / (float)logicalWidth
            : 1.0f;
    }
}
