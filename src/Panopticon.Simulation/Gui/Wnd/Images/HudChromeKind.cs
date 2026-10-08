namespace Panopticon.Gui.Wnd.Images;

/// <summary>
/// What a WND image is used for. Dynamic HUD chrome (WI-G9-03): images tagged with a kind
/// other than <see cref="None"/> draw through <see cref="HudChromePainter"/> instead of their
/// texture when the active preset selects <c>UiMode.DynamicHud</c>. Content images (cameos,
/// portraits, icons, rank chevrons) stay <see cref="None"/> and always sample their texture.
/// </summary>
public enum HudChromeKind
{
    /// <summary>Content image: never replaced by dynamic chrome.</summary>
    None,

    /// <summary>Window or control background panel.</summary>
    Panel,

    /// <summary>Button resting-state background.</summary>
    ButtonNormal,

    /// <summary>Button hover-state background.</summary>
    ButtonHover,

    /// <summary>Button pressed-state background.</summary>
    ButtonPressed,

    /// <summary>Button disabled-state background.</summary>
    ButtonDisabled,

    /// <summary>Border-only hover highlight drawn over content (command button hilite).</summary>
    ButtonOverlayHover,

    /// <summary>Border-plus-wash pressed highlight drawn over content (command button push).</summary>
    ButtonOverlayPushed,

    /// <summary>Sunken outline: empty command slots and container frames.</summary>
    Frame,

    /// <summary>Small solid knob: scroll arrows, scroll thumbs, slider pips.</summary>
    Thumb,

    /// <summary>Checkbox box outline, unchecked.</summary>
    CheckboxUnchecked,

    /// <summary>Checkbox box outline with the checked fill inside.</summary>
    CheckboxChecked,
}

/// <summary>
/// Draw counters backing the WI-G9-03 chrome audit. <see cref="ChromeBitmapDraws"/> counts
/// chrome-tagged images that sampled their texture (a violation under dynamic HUD);
/// <see cref="DynamicChromePaints"/> counts procedural chrome paints (zero means the dynamic
/// path never engaged). The visual regression harness resets and reads these around full-HUD
/// captures.
/// </summary>
public static class HudChromeAudit
{
    /// <summary>Chrome-tagged images that drew through the texture path.</summary>
    public static long ChromeBitmapDraws;

    /// <summary>Dynamic chrome paints issued by <see cref="HudChromePainter"/>.</summary>
    public static long DynamicChromePaints;

    /// <summary>Zeroes both counters before a captured run.</summary>
    public static void Reset()
    {
        ChromeBitmapDraws = 0;
        DynamicChromePaints = 0;
    }
}
