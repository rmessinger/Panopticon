using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Knobs for <see cref="EngagementCaptureRunner"/>: which two units fight, where, and how the
/// capture is framed. Defaults capture a USA Missile Defender firing at a China BattleMaster.
/// Distance and height values are meters; the defaults convert the historical legacy-unit
/// values by the source unit scale, preserving the authored behavior.
/// </summary>
public sealed class EngagementCaptureOptions
{
    /// <summary>Map to spawn the engagement on.</summary>
    public string MapPath { get; set; } = "maps/alpine assault/alpine assault.map";

    /// <summary>Object definition of the unit that fires.</summary>
    public string AttackerDefinition { get; set; } = "AmericaInfantryMissileDefender";

    /// <summary>Object definition of the unit that is fired at.</summary>
    public string TargetDefinition { get; set; } = "ChinaTankBattleMaster";

    /// <summary>Annotation label for the attacker. Empty derives one from the definition.</summary>
    public string? AttackerLabel { get; set; } = null;

    /// <summary>Annotation label for the target. Empty derives one from the definition.</summary>
    public string? TargetLabel { get; set; } = null;

    /// <summary>World X of the midpoint between the two units, in meters.</summary>
    public float CenterX { get; set; } = 600f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>World Y of the midpoint between the two units, in meters.</summary>
    public float CenterY { get; set; } = 600f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>Distance between the two units in meters. Must be inside the attacker's weapon range.</summary>
    public float Separation { get; set; } = 120f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>
    /// Compass rotation of the attacker-to-target axis around the midpoint. Firing FX such as an
    /// infantry backblast expand behind the attacker, so this controls whether they land on top of
    /// the attacker on screen or behind him.
    /// </summary>
    public float AxisAngleDegrees { get; set; } = 180f;

    /// <summary>Close every GUI window before capturing, so no HUD element intrudes on the crop.</summary>
    public bool HideHud { get; set; } = true;

    /// <summary>Max health forced onto the target, so a shot cycle produces a visible health drop.</summary>
    public float TargetHp { get; set; } = 300f;

    /// <summary>Logic frames to settle after spawn before the weapon is commanded.</summary>
    public int SettleFrames { get; set; } = 15;

    /// <summary>Logic frames to capture. One PNG per frame.</summary>
    public int Frames { get; set; } = 150;

    /// <summary>Fraction of the viewport width the two units should span after the camera fit.</summary>
    public float DesiredSpanFraction { get; set; } = 0.70f;

    /// <summary>Starting height for the camera fit iteration, in meters.</summary>
    public float InitialCameraHeight { get; set; } = 180f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>Lower bound for the fitted camera height, in meters.</summary>
    public float MinCameraHeight { get; set; } = 60f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>
    /// Upper bound for the fitted camera height, in meters. Kept below the terrain pass's fog
    /// band (roughly 274-457 m) so a fit never lands on a frame of fog gray.
    /// </summary>
    public float MaxCameraHeight { get; set; } = 800f * (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>Directory for the PNG sequence and manifest. Empty uses $TMPDIR/panopticon_engagement.</summary>
    public string? OutputDirectory { get; set; } = null;
}
