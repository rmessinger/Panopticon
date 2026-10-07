using System;
using Panopticon.Mathematics;

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

    public static Point2D ToPixels(in Point2D point, float scale)
    {
        return new Point2D((int)MathF.Round(point.X * scale), (int)MathF.Round(point.Y * scale));
    }

    public static Rectangle ToPixels(in Rectangle rectangle, float scale)
    {
        return new Rectangle(
            (int)MathF.Round(rectangle.X * scale),
            (int)MathF.Round(rectangle.Y * scale),
            (int)MathF.Round(rectangle.Width * scale),
            (int)MathF.Round(rectangle.Height * scale));
    }
}
