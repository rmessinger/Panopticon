namespace Panopticon.Graphics;

/// <summary>
/// Immutable graphics fidelity settings owned by the game. Values mirror the pre-G2 hardcoded
/// renderer state recorded by the WI-G2-01 audit; render construction reads them instead of
/// literals once WI-G2-03 wires them in.
/// </summary>
public sealed class GraphicsSettings
{
    private GraphicsSettings(
        int sampleCount,
        AlphaTestMode alphaTestMode,
        ColourSpaceMode colourSpaceMode,
        int anisotropy,
        float mipLodBias,
        ShadowTier shadowTier,
        UiMode uiMode,
        bool realisticProportions)
    {
        SampleCount = sampleCount;
        AlphaTestMode = alphaTestMode;
        ColourSpaceMode = colourSpaceMode;
        Anisotropy = anisotropy;
        MipLodBias = mipLodBias;
        ShadowTier = shadowTier;
        UiMode = uiMode;
        RealisticProportions = realisticProportions;
    }

    /// <summary>
    /// The pre-Track G renderer, member for member. Owned by later phases until they land:
    /// G4 sample count, G5 alpha test, G6 colour space, G7 anisotropy and mip bias,
    /// G8 shadow slope bias, G9 UI, G10 proportions.
    /// </summary>
    public static GraphicsSettings Legacy() => new(
        1,
        AlphaTestMode.LegacyCutoff,
        ColourSpaceMode.LegacyGamma,
        4,
        0f,
        ShadowTier.Original,
        UiMode.LegacyArt,
        realisticProportions: false);

    /// <summary>
    /// The game default. Differs from <see cref="Legacy"/> on the shadow tier, the sample
    /// count, the alpha test, the colour space, the anisotropy and the UI mode: 4x MSAA (G4)
    /// plus the four-cascade 2048 shadow tier (G4), coverage-preserving alpha with
    /// alpha-to-coverage (G5), linear shading with a single sRGB output transfer (G6),
    /// anisotropy raised to the 16x ceiling (G7), and role-aware UI art sampling (G9).
    /// Other members stay Legacy until their owning phase lands.
    /// </summary>
    public static GraphicsSettings Enhanced() => new(
        4,
        AlphaTestMode.CoveragePreserving,
        ColourSpaceMode.Linear,
        16,
        0f,
        ShadowTier.High,
        UiMode.DynamicHud,
        realisticProportions: false);

    /// <summary>
    /// Builds a settings value from explicit members. Used by the capability-clamping
    /// controller and tests; presets remain the public entry points.
    /// </summary>
    internal static GraphicsSettings Create(
        int sampleCount,
        AlphaTestMode alphaTestMode,
        ColourSpaceMode colourSpaceMode,
        int anisotropy,
        float mipLodBias,
        ShadowTier shadowTier,
        UiMode uiMode,
        bool realisticProportions) => new(
        sampleCount,
        alphaTestMode,
        colourSpaceMode,
        anisotropy,
        mipLodBias,
        shadowTier,
        uiMode,
        realisticProportions);

    /// <summary>Multisample count for 3D render targets; 1 is single-sample.</summary>
    public int SampleCount { get; }

    public AlphaTestMode AlphaTestMode { get; }

    public ColourSpaceMode ColourSpaceMode { get; }

    /// <summary>Maximum anisotropic filtering level; 4 is today's fixed sampler.</summary>
    public int Anisotropy { get; }

    /// <summary>Mip level-of-detail bias applied to game samplers.</summary>
    public float MipLodBias { get; }

    public ShadowTier ShadowTier { get; }

    public UiMode UiMode { get; }

    /// <summary>Render-only proportion scaling; explicit opt-in, never a default.</summary>
    public bool RealisticProportions { get; }
}

public enum AlphaTestMode
{
    /// <summary>Fixed-function style discard against a constant alpha cutoff.</summary>
    LegacyCutoff,

    /// <summary>Coverage-preserving cutout rendering via alpha-to-coverage.</summary>
    CoveragePreserving
}

public enum ColourSpaceMode
{
    /// <summary>Direct UNorm output; shading happens on encoded values.</summary>
    LegacyGamma,

    /// <summary>Linear shading with exactly one sRGB transfer at output.</summary>
    Linear
}

public enum ShadowTier
{
    /// <summary>One cascade at 1024, matching today's defaults.</summary>
    Original,

    /// <summary>Four cascades at 2048, via the existing switchable shadow path.</summary>
    High
}

public enum UiMode
{
    /// <summary>Original panel, button and frame bitmaps.</summary>
    LegacyArt,

    /// <summary>Native resolution chrome; portraits, icons and the minimap stay textures.</summary>
    DynamicHud
}
