using System.Text.Json.Serialization;
using Panopticon.Graphics.Cameras;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Persistent configuration for the TestHarness tool.
/// Loaded from <c>TestHarness.json</c> in the executable directory.
/// </summary>
public sealed class TestHarnessConfig
{
    /// <summary>
    /// Built-in scenario profile to run when <see cref="ScenarioPath"/> is null.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuiltInScenario Scenario { get; set; } = BuiltInScenario.AlpineAssaultUSAvGLAFullBuild;

    /// <summary>
    /// Path to a scenario JSON profile.
    /// Null uses the selected built-in <see cref="Scenario"/>.
    /// </summary>
    public string? ScenarioPath { get; set; } = null;

    /// <summary>
    /// The SAGE game installation to run against.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SageGame Game { get; set; } = SageGame.CncGenerals;

    /// <summary>
    /// Graphics backend override. Null selects the platform default.
    /// Accepted values: Direct3D11, Vulkan, OpenGL, Metal, OpenGLES.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GraphicsBackend? Renderer { get; set; } = null;

    /// <summary>Whether to start in fullscreen mode. Ignored in headless mode.</summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// Renders the full in-game control bar (buttons, money/power displays, pre-rendered
    /// background). Default false shows only the minimap. Maps to
    /// <see cref="Panopticon.Configuration.FullHud"/>.
    /// </summary>
    public bool FullHud { get; set; } = false;

    /// <summary>G9 scale validation: multiplier applied to the scenario capture viewport.</summary>
    public int ViewportScale { get; set; } = 1;

    /// <summary>
    /// G10 evidence: pins the render clock so scenario captures are deterministic and can be
    /// diffed against each other (the visual regression harness always pins it).
    /// </summary>
    public bool FixedRenderClock { get; set; } = false;

    /// <summary>
    /// Keep the exported .rep file on disk after the run instead of deleting it.
    /// </summary>
    public bool KeepReplay { get; set; } = false;

    /// <summary>
    /// Run without a window using an offscreen graphics device.
    /// In headless mode frame captures can be triggered via replay action events and are
    /// logged to stdout as <c>[CAPTURE] &lt;path&gt; | &lt;hint&gt;</c> lines for agent inspection.
    /// When false (default) a normal game window is opened.
    /// </summary>
    public bool Headless { get; set; } = false;

    /// <summary>
    /// In headless mode, capture a frame after every completed replay action and write it
    /// as a PNG under <see cref="FrameCaptureService.CaptureDirectory"/> for agent inspection.
    /// Disabled by default to avoid accumulating capture files on routine headless runs.
    /// </summary>
    public bool CaptureFrames { get; set; } = false;

    /// <summary>
    /// Override the game installation path instead of auto-detecting from the registry
    /// or standard environment variables.
    /// </summary>
    public string? GamePath { get; set; } = null;

    public string? GltfFixtureRoot { get; set; } = null;

    /// <summary>
    /// Multiplier applied to the game's default <c>MaxCameraHeight</c> INI value.
    /// 1.0 = game default; 2.0 (default) = double the zoom-out distance.
    /// </summary>
    public float MaxCameraZoomMultiplier { get; set; } = 5.0f;

    /// <summary>
    /// Camera control scheme to use in windowed mode.
    /// Generals = right-drag to pan, middle-drag to rotate.
    /// TotalWar = right-drag to rotate, middle-drag to pan, Q/E to rotate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CameraControlScheme CameraControlScheme { get; set; } = CameraControlScheme.TotalWar;

    /// <summary>
    /// Mouse click scheme for issuing unit orders.
    /// Standard (default) = right-click issues move/attack orders (modern RTS convention).
    /// Classic = left-click issues orders (original Generals convention).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MouseClickMode MouseClickMode { get; set; } = MouseClickMode.Standard;

    // ── Unit validation ground (P9) ──────────────────────────────────────────
    // All optional with defaults so an existing TestHarness.json needs no edit.

    /// <summary>Map path override for the validation ground. Empty uses a default map.</summary>
    public string? ValidationGroundMapPath { get; set; } = null;

    /// <summary>
    /// Path to a validation-ground scenario JSON (layout + placement overrides) written by the
    /// ReplaySketch editor. When set, the run loads it verbatim -- its map, layout, overrides, and
    /// target definitions all apply -- and ValidationGroundMapPath/SettleFrames/Seed are ignored.
    /// When empty, the run builds the default scenario from those knobs.
    /// </summary>
    public string? ValidationGroundScenarioPath { get; set; } = null;

    /// <summary>Frames to settle after spawn before issuing orders.</summary>
    public int ValidationGroundSettleFrames { get; set; } = 30;

    /// <summary>Seed for deterministic layout (informational; the builder is already deterministic).</summary>
    public int ValidationGroundSeed { get; set; } = 1;

    /// <summary>Path to the checked-in golden baseline JSON. Empty skips baseline comparison.</summary>
    public string? ValidationGroundBaselinePath { get; set; } = null;

    /// <summary>When true, the run refreshes (overwrites) the baseline instead of comparing.</summary>
    public bool ValidationGroundRefreshBaseline { get; set; } = false;

    /// <summary>Path to write the validation-ground JSON report to. Empty writes none.</summary>
    public string? ValidationGroundReportPath { get; set; } = null;

    /// <summary>
    /// Card WI-M2-04: path to the EE2 reference validation profile (for example the shipped
    /// <c>ee2-reference-validation.json</c>). When set, the roster imports through the shared
    /// unit-content layer before spawn and the EE2 reference contracts run beside the Generals
    /// roster in the same session, writing ee2-contract-report.json. Empty keeps the validation
    /// ground unchanged.
    /// </summary>
    public string? ValidationGroundEe2ProfilePath { get; set; } = null;

    /// <summary>
    /// When true (default), a windowed validation-ground run starts paused with the orders held so
    /// the starting conditions can be inspected; Space (or the overlay button) resumes, which runs
    /// the settle stage and issues the attack orders. Afterwards Space toggles active pause.
    /// Ignored by headless runs, which always auto-issue for byte-identical reports.
    /// </summary>
    public bool ValidationGroundHoldOrders { get; set; } = true;

    /// <summary>
    /// When true (default), the run exits once every measurable unit has resolved (its target
    /// destroyed) or the frame cap is reached, then writes the report/baseline. When false, the run
    /// does not auto-exit: the game keeps running so the roster can be observed, and the report is
    /// written when the window is closed. Only honoured in windowed mode; headless always exits on
    /// completion, since a never-exiting headless run has no window to close and would hang.
    /// </summary>
    public bool ExitAfterTargetsDestroyed { get; set; } = true;

    /// <summary>
    /// Options for <see cref="BuiltInScenario.EngagementCapture"/>. Null uses the defaults
    /// (a USA Missile Defender firing at a China BattleMaster).
    /// </summary>
    public EngagementCaptureOptions? EngagementCapture { get; set; } = null;

    // ── Mass battle deployment (M0) ─────────────────────────────────────────

    /// <summary>
    /// Path to a <see cref="MassBattleDeploymentProfile"/> JSON. Empty uses the embedded
    /// deepdivide profile shipped beside the executable.
    /// </summary>
    public string? MassBattleDeploymentProfilePath { get; set; } = null;

    /// <summary>Spawn and display the armies without running the map acceptance probes.</summary>
    public bool MassBattleDeploymentPreview { get; set; } = false;

    /// <summary>Where to write the deployment proof report JSON. Empty writes beside the executable.</summary>
    public string? MassBattleDeploymentReportPath { get; set; } = null;

    /// <summary>Optional PNG path for one deployment screenshot captured after the armies spawn.</summary>
    public string? MassBattleDeploymentScreenshotPath { get; set; } = null;

    // ── Mass battle capacity probe (M1) ────────────────────────────────────

    /// <summary>
    /// Path to a <see cref="MassBattleDeploymentProfile"/> JSON for the capacity probe. Empty
    /// uses the embedded deepdivide capacity-probe profile shipped beside the executable.
    /// </summary>
    public string? MassBattleCapacityProbeProfilePath { get; set; } = null;

    /// <summary>Where to write the engagement report JSON. Empty writes beside the executable.</summary>
    public string? MassBattleCapacityProbeReportPath { get; set; } = null;

    /// <summary>Optional PNG path for one engagement screenshot captured after the run.</summary>
    public string? MassBattleCapacityProbeScreenshotPath { get; set; } = null;

    /// <summary>
    /// Frame budget for the measured engagement. Null uses the runner default (9000 frames);
    /// setup, spawn, and settle frames are not counted against it.
    /// </summary>
    public int? MassBattleCapacityProbeFrameBudget { get; set; } = null;

    /// <summary>
    /// Enables the WI-B4-06 async-path scale anchor inside the capacity probe: on this frame
    /// cadence the runner submits squad-shaped asynchronous path requests and records their
    /// latency, queue backlog, and deadline misses for the hierarchical-pathfinding gate. Null
    /// disables the anchor.
    /// </summary>
    public int? MassBattleCapacityProbeAsyncAnchorBurstEveryFrames { get; set; } = null;

    /// <summary>Requests per anchor burst; defaults to 8 when only the cadence is set.</summary>
    public int? MassBattleCapacityProbeAsyncAnchorRequestsPerBurst { get; set; } = null;
}
