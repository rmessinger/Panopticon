using Panopticon.Mathematics;

namespace Panopticon.Gui.Wnd.Images;

/// <summary>
/// Procedural replacement for pre-rendered WND chrome bitmaps (WI-G9-03). Draws fills with
/// stretched solid white and pixel-snapped one-pixel borders, so the chrome stays sharp at any
/// viewport scale instead of blurring upscaled 800x600 art. Colours are authored sRGB values;
/// the linear preset decodes them at the sprite level like any other 2D tint.
/// </summary>
internal static class HudChromePainter
{
    private static readonly ColorRgbaF PanelFill = new(0.055f, 0.075f, 0.105f, 0.85f);
    private static readonly ColorRgbaF PanelBorder = new(0.42f, 0.47f, 0.54f, 0.90f);

    private static readonly ColorRgbaF ButtonFill = new(0.09f, 0.12f, 0.16f, 0.75f);
    private static readonly ColorRgbaF ButtonBorder = new(0.36f, 0.41f, 0.48f, 0.85f);
    private static readonly ColorRgbaF ButtonHoverFill = new(0.14f, 0.19f, 0.25f, 0.82f);
    private static readonly ColorRgbaF ButtonHoverBorder = new(0.95f, 0.83f, 0.45f, 0.95f);
    private static readonly ColorRgbaF ButtonPressedFill = new(0.04f, 0.05f, 0.07f, 0.88f);
    private static readonly ColorRgbaF ButtonPressedBorder = new(0.30f, 0.33f, 0.38f, 0.90f);
    private static readonly ColorRgbaF ButtonDisabledFill = new(0.07f, 0.08f, 0.09f, 0.60f);
    private static readonly ColorRgbaF ButtonDisabledBorder = new(0.26f, 0.27f, 0.29f, 0.60f);

    private static readonly ColorRgbaF OverlayHoverBorder = new(1.00f, 0.88f, 0.50f, 0.95f);
    private static readonly ColorRgbaF OverlayPushedBorder = new(0.90f, 0.55f, 0.25f, 0.90f);
    private static readonly ColorRgbaF OverlayPushedWash = new(0.00f, 0.00f, 0.00f, 0.18f);

    private static readonly ColorRgbaF FrameBorder = new(0.03f, 0.035f, 0.045f, 0.90f);
    private static readonly ColorRgbaF FrameInnerBorder = new(0.30f, 0.32f, 0.36f, 0.45f);

    private static readonly ColorRgbaF ThumbFill = new(0.16f, 0.19f, 0.23f, 0.92f);
    private static readonly ColorRgbaF ThumbBorder = new(0.45f, 0.50f, 0.56f, 0.90f);

    private static readonly ColorRgbaF CheckboxCheckedFill = new(0.90f, 0.85f, 0.50f, 0.90f);

    /// <summary>Draws the procedural equivalent of one chrome element into its destination rect.</summary>
    internal static void DrawChrome(DrawingContext2D drawingContext, in Rectangle rect, HudChromeKind kind)
    {
        var r = rect.ToRectangleF();

        switch (kind)
        {
            case HudChromeKind.Panel:
                drawingContext.FillRectangle(r, PanelFill);
                drawingContext.DrawPixelSnappedRectangle(r, PanelBorder, 1f);
                break;

            case HudChromeKind.ButtonNormal:
                drawingContext.FillRectangle(r, ButtonFill);
                drawingContext.DrawPixelSnappedRectangle(r, ButtonBorder, 1f);
                break;

            case HudChromeKind.ButtonHover:
                drawingContext.FillRectangle(r, ButtonHoverFill);
                drawingContext.DrawPixelSnappedRectangle(r, ButtonHoverBorder, 1f);
                break;

            case HudChromeKind.ButtonPressed:
                drawingContext.FillRectangle(r, ButtonPressedFill);
                drawingContext.DrawPixelSnappedRectangle(r, ButtonPressedBorder, 1f);
                break;

            case HudChromeKind.ButtonDisabled:
                drawingContext.FillRectangle(r, ButtonDisabledFill);
                drawingContext.DrawPixelSnappedRectangle(r, ButtonDisabledBorder, 1f);
                break;

            case HudChromeKind.ButtonOverlayHover:
                drawingContext.DrawPixelSnappedRectangle(r, OverlayHoverBorder, 1f);
                break;

            case HudChromeKind.ButtonOverlayPushed:
                drawingContext.FillRectangle(r, OverlayPushedWash);
                drawingContext.DrawPixelSnappedRectangle(r, OverlayPushedBorder, 1f);
                break;

            case HudChromeKind.Frame:
            case HudChromeKind.CheckboxUnchecked:
                drawingContext.DrawPixelSnappedRectangle(r, FrameBorder, 1f);
                drawingContext.DrawPixelSnappedRectangle(Inset(r, 1f), FrameInnerBorder, 1f);
                break;

            case HudChromeKind.Thumb:
                drawingContext.FillRectangle(r, ThumbFill);
                drawingContext.DrawPixelSnappedRectangle(r, ThumbBorder, 1f);
                break;

            case HudChromeKind.CheckboxChecked:
                drawingContext.DrawPixelSnappedRectangle(r, FrameBorder, 1f);
                drawingContext.FillRectangle(Inset(r, 2f), CheckboxCheckedFill);
                break;
        }
    }

    private static RectangleF Inset(in RectangleF rect, float amount)
    {
        return new RectangleF(
            rect.X + amount,
            rect.Y + amount,
            rect.Width - amount * 2,
            rect.Height - amount * 2);
    }
}
