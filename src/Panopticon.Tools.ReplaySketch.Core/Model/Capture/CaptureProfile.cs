using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model.Capture;

/// <summary>
/// Declarative description of a small scripted scenario to capture as a PNG sequence plus a
/// per-frame manifest: a handful of actors, camera configuration, frame-scheduled commands,
/// and success expectations. Loaded from JSON by
/// <see cref="Services.CaptureProfileLoader"/>; validated by <see cref="CaptureProfileValidator"/>.
/// The TestHarness <c>ScenarioCaptureRunner</c> executes it.
/// </summary>
public class CaptureProfile
{
    public List<CaptureTerrainScorch> Scorches { get; set; } = [];
    /// <summary>Scenario name; used in the manifest and in the default output directory name.</summary>
    public string Name { get; set; } = "capture";

    /// <summary>Map to run on, as a path the game's file system resolves.</summary>
    public string MapPath { get; set; } = "maps/alpine assault/alpine assault.map";

    /// <summary>Logic frames to simulate after the settle period. One command tick per frame.</summary>
    public int DurationFrames { get; set; } = 150;

    /// <summary>
    /// Logic frames to run after spawning, before the camera is fitted and capture starts.
    /// Fixed warm-up belongs here: camera fitting consumes no logic ticks.
    /// </summary>
    public int SettleFrames { get; set; } = 15;

    /// <summary>Capture one PNG every Nth logic frame. 1 captures every frame.</summary>
    public int CaptureStride { get; set; } = 1;

    /// <summary>
    /// Render every logic frame, saving only every <see cref="CaptureStride"/>th. Draw-module
    /// animations advance per rendered frame, so strided captures otherwise play clips slower than
    /// the simulation.
    /// </summary>
    public bool RenderEveryFrame { get; set; } = false;

    /// <summary>Close every GUI window before capturing so no HUD element intrudes on the crop.</summary>
    public bool HideHud { get; set; } = true;

    /// <summary>Directory for the PNG sequence and manifest. Empty uses the runner's default.</summary>
    public string? OutputDirectory { get; set; } = null;

    /// <summary>Skirmish player slots, in declaration order. Actors reference these by <see cref="CapturePlayerSlot.Id"/>.</summary>
    public List<CapturePlayerSlot> Players { get; set; } = [];

    /// <summary>Unit content published through the shared import layer before actors spawn.</summary>
    public List<CaptureContentImport> ContentImports { get; set; } = [];

    /// <summary>Actors spawned during setup, in declaration order.</summary>
    public List<CaptureActor> Actors { get; set; } = [];

    /// <summary>Camera configuration.</summary>
    public CaptureCamera Camera { get; set; } = new();

    /// <summary>Commands scheduled by capture-relative logic frame, executed in declaration order before that frame's tick.</summary>
    public List<CaptureCommand> Commands { get; set; } = [];

    /// <summary>Success expectations evaluated after the run. Unmet expectations fail the capture.</summary>
    public List<CaptureExpectation> Expectations { get; set; } = [];

    /// <summary>Stop capturing once this actor is destroyed, before <see cref="DurationFrames"/>. Null never stops early.</summary>
    public string? StopWhenActorDestroyed { get; set; } = null;

    /// <summary>
    /// Physics backend for the run: <c>Legacy</c> (default) or <c>Bepu</c>. Selects the whole
    /// game's object-dynamics backend before the game is created.
    /// </summary>
    public string? PhysicsBackend { get; set; } = null;

    /// <summary>
    /// Owner ID reserved for the scene's civilian player; actors with this owner never
    /// retaliate through player AI. Cannot be declared as a player slot ID.
    /// </summary>
    public const string CivilianOwnerId = "civilian";
}

public class CapturePlayerSlot
{
    /// <summary>Stable ID referenced by <see cref="CaptureActor.Owner"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Faction internal name, e.g. <c>FactionAmerica</c>.</summary>
    public string Side { get; set; } = "FactionAmerica";

    /// <summary>Display name, e.g. <c>USA</c>.</summary>
    public string Name { get; set; } = "USA";

    /// <summary>Player color as RRGGBB hex. Empty uses a default palette.</summary>
    public string? Color { get; set; } = null;

    /// <summary>Money deposited into this player's account after the game starts.</summary>
    public uint StartingMoney { get; set; } = 0;
}

public class CaptureActor
{
    /// <summary>Stable ID used by commands, expectations, and manifest records.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Object definition name, e.g. <c>AmericaVehicleDozer</c>, or <c>&lt;source&gt;:&lt;name&gt;</c> for a unit
    /// published by a <see cref="CaptureProfile.ContentImports"/> entry.
    /// </summary>
    public string Definition { get; set; } = "";

    /// <summary>Owning player slot ID, or <c>civilian</c>.</summary>
    public string Owner { get; set; } = CaptureProfile.CivilianOwnerId;

    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>
    /// Absolute world Z at spawn. Null places the actor on the terrain.
    /// </summary>
    public float? Z { get; set; } = null;

    /// <summary>Spawn facing in degrees; 0 faces +X, 90 faces +Y.</summary>
    public float FacingDegrees { get; set; } = 0f;

    /// <summary>Spawn pitch in degrees around the sideways (Y) axis, applied after facing.</summary>
    public float PitchDegrees { get; set; } = 0f;

    /// <summary>Spawn roll in degrees around the forward (X) axis, applied after pitch; 180 spawns upside down.</summary>
    public float RollDegrees { get; set; } = 0f;

    /// <summary>Annotation label. Empty derives one from the definition at run time.</summary>
    public string? Label { get; set; } = null;

    /// <summary>Role recorded in the manifest (e.g. <c>attacker</c>, <c>target</c>). Optional.</summary>
    public string? Role { get; set; } = null;

    /// <summary>Include the actor in the manifest's per-frame records. Default true.</summary>
    public bool Tracked { get; set; } = true;

    /// <summary>Max health forced onto the actor; 0 keeps the definition's value.</summary>
    public float MaxHealth { get; set; } = 0f;
}

public enum CaptureCameraMode
{
    AutoFit,
    Explicit,

    /// <summary>
    /// Fits like <see cref="AutoFit"/> from the subjects and framing points, then pans every
    /// logic frame so the follow subject stays at screen center. The fitted height never
    /// changes; if the subject dies, the camera freezes at its last position.
    /// </summary>
    Follow,
}

public class CaptureCamera
{
    public CaptureCameraMode Mode { get; set; } = CaptureCameraMode.AutoFit;

    // AutoFit mode.

    /// <summary>Actor IDs to frame. Empty frames every profile actor.</summary>
    public List<string> SubjectActorIds { get; set; } = [];

    /// <summary>
    /// Additional world-space points to frame (build sites, expected trajectory extents) that
    /// are not represented by initial actor positions.
    /// </summary>
    public List<CapturePoint> FramingPoints { get; set; } = [];

    /// <summary>Fraction of the viewport the subject bounding box should span after the fit.</summary>
    public float DesiredSpanFraction { get; set; } = 0.70f;

    public float InitialCameraHeight { get; set; } = 180f;

    public float MinCameraHeight { get; set; } = 60f;

    /// <summary>Upper bound for the fitted height; kept below the terrain fog band (roughly 900-1500).</summary>
    public float MaxCameraHeight { get; set; } = 800f;

    // Follow mode.

    /// <summary>
    /// Actor the camera centers every logic frame in <see cref="CaptureCameraMode.Follow"/>.
    /// Empty centers the first <see cref="SubjectActorIds"/> entry.
    /// </summary>
    public string FollowActorId { get; set; } = "";

    /// <summary>
    /// Follow the projectiles fired by this actor instead of the actor itself: the camera rides
    /// the newest live projectile from the source (shell flight, impact freeze, hand-off to the
    /// next shot). Mutually exclusive with <see cref="FollowActorId"/>.
    /// </summary>
    public string FollowProjectileSourceActorId { get; set; } = "";

    /// <summary>
    /// Projectile follow locks the FIRST projectile only: ride it to its death and freeze there,
    /// never handing off to later shots (for rapid-fire weapons where the newest hand-off would
    /// jump between rockets).
    /// </summary>
    public bool FollowFirstProjectileOnly { get; set; }

    // Explicit mode.

    public CapturePoint? LookAt { get; set; } = null;

    /// <summary>Camera height above ground. Must be set in explicit mode.</summary>
    public float Height { get; set; } = 0f;

    /// <summary>
    /// Optional camera pitch override in degrees, applied after the default orientation.
    /// Positive values tilt toward the horizon (grazing terrain views); null keeps the default.
    /// </summary>
    public float? PitchDegrees { get; set; } = null;

    /// <summary>Optional camera yaw override in degrees; null keeps the default orientation.</summary>
    public float? AngleDegrees { get; set; } = null;
}

public class CapturePoint
{
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>World Z. 0 derives the terrain height at run time.</summary>
    public float Z { get; set; } = 0f;
}
