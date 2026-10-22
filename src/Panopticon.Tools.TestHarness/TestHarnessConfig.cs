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
    [HarnessParameter("Run", "Scenario.")]
    public BuiltInScenario Scenario { get; set; } = BuiltInScenario.AlpineAssaultUSAvGLAFullBuild;

    /// <summary>
    /// Path to a scenario JSON profile.
    /// Null uses the selected built-in <see cref="Scenario"/>.
    /// </summary>
    [HarnessParameter("Run", "ScenarioPath.")]
    public string? ScenarioPath { get; set; } = null;

    /// <summary>
    /// The SAGE game installation to run against.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [HarnessParameter("Install", "Game.")]
    public SageGame Game { get; set; } = SageGame.CncGenerals;

    /// <summary>
    /// Graphics backend override. Null selects the platform default.
    /// Accepted values: Direct3D11, Vulkan, OpenGL, Metal, OpenGLES.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [HarnessParameter("Display", "Renderer.")]
    public GraphicsBackend? Renderer { get; set; } = null;

    /// <summary>Whether to start in fullscreen mode. Ignored in headless mode.</summary>
    [HarnessParameter("Display", "Fullscreen.")]
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// Renders the full in-game control bar (buttons, money/power displays, pre-rendered
    /// background). Default false shows only the minimap. Maps to
    /// <see cref="Panopticon.Configuration.FullHud"/>.
    /// </summary>
    [HarnessParameter("Display", "FullHud.")]
    public bool FullHud { get; set; } = false;

    /// <summary>G9 scale validation: multiplier applied to the scenario capture viewport.</summary>
    [HarnessParameter("Display", "ViewportScale.")]
    public int ViewportScale { get; set; } = 1;

    /// <summary>
    /// G10 evidence: pins the render clock so scenario captures are deterministic and can be
    /// diffed against each other (the visual regression harness always pins it).
    /// </summary>
    [HarnessParameter("Display", "FixedRenderClock.")]
    public bool FixedRenderClock { get; set; } = false;

    /// <summary>
    /// Keep the exported .rep file on disk after the run instead of deleting it.
    /// </summary>
    [HarnessParameter("Run", "KeepReplay.")]
    public bool KeepReplay { get; set; } = false;

    /// <summary>
    /// Run without a window using an offscreen graphics device.
    /// In headless mode frame captures can be triggered via replay action events and are
    /// logged to stdout as <c>[CAPTURE] &lt;path&gt; | &lt;hint&gt;</c> lines for agent inspection.
    /// When false (default) a normal game window is opened.
    /// </summary>
    [HarnessParameter("Run", "Headless.")]
    public bool Headless { get; set; } = false;

    /// <summary>
    /// In headless mode, capture a frame after every completed replay action and write it
    /// as a PNG under <see cref="FrameCaptureService.CaptureDirectory"/> for agent inspection.
    /// Disabled by default to avoid accumulating capture files on routine headless runs.
    /// </summary>
    [HarnessParameter("Run", "CaptureFrames.")]
    public bool CaptureFrames { get; set; } = false;

    /// <summary>
    /// Override the game installation path instead of auto-detecting from the registry
    /// or standard environment variables.
    /// </summary>
    [HarnessParameter("Install", "GamePath.")]
    public string? GamePath { get; set; } = null;

    [HarnessParameter("Install", "GltfFixtureRoot.")]
    public string? GltfFixtureRoot { get; set; } = null;

    /// <summary>
    /// Multiplier applied to the game's default <c>MaxCameraHeight</c> INI value.
    /// 1.0 = game default; 2.0 (default) = double the zoom-out distance.
    /// </summary>
    [HarnessParameter("Display", "MaxCameraZoomMultiplier.")]
    public float MaxCameraZoomMultiplier { get; set; } = 5.0f;

    /// <summary>
    /// Camera control scheme to use in windowed mode.
    /// Generals = right-drag to pan, middle-drag to rotate.
    /// TotalWar = right-drag to rotate, middle-drag to pan, Q/E to rotate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [HarnessParameter("Display", "CameraControlScheme.")]
    public CameraControlScheme CameraControlScheme { get; set; } = CameraControlScheme.TotalWar;

    /// <summary>
    /// Mouse click scheme for issuing unit orders.
    /// Standard (default) = right-click issues move/attack orders (modern RTS convention).
    /// Classic = left-click issues orders (original Generals convention).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [HarnessParameter("Display", "MouseClickMode.")]
    public MouseClickMode MouseClickMode { get; set; } = MouseClickMode.Standard;

    // ── Unit validation ground (P9) ──────────────────────────────────────────
    // All optional with defaults so an existing TestHarness.json needs no edit.

    /// <summary>Map path override for the validation ground. Empty uses a default map.</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundMapPath.", nameof(BuiltInScenario.UnitValidationGround))]
    public string? ValidationGroundMapPath { get; set; } = null;

    /// <summary>
    /// Path to a validation-ground scenario JSON (layout + placement overrides) written by the
    /// ReplaySketch editor. When set, the run loads it verbatim -- its map, layout, overrides, and
    /// target definitions all apply -- and ValidationGroundMapPath/SettleFrames/Seed are ignored.
    /// When empty, the run builds the default scenario from those knobs.
    /// </summary>
    [HarnessParameter("ValidationGround", "ValidationGroundScenarioPath.", nameof(BuiltInScenario.UnitValidationGround))]
    public string? ValidationGroundScenarioPath { get; set; } = null;

    /// <summary>Frames to settle after spawn before issuing orders.</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundSettleFrames.", nameof(BuiltInScenario.UnitValidationGround))]
    public int ValidationGroundSettleFrames { get; set; } = 30;

    /// <summary>Seed for deterministic layout (informational; the builder is already deterministic).</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundSeed.", nameof(BuiltInScenario.UnitValidationGround))]
    public int ValidationGroundSeed { get; set; } = 1;

    /// <summary>Path to the checked-in golden baseline JSON. Empty skips baseline comparison.</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundBaselinePath.", nameof(BuiltInScenario.UnitValidationGround))]
    public string? ValidationGroundBaselinePath { get; set; } = null;

    /// <summary>When true, the run refreshes (overwrites) the baseline instead of comparing.</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundRefreshBaseline.", nameof(BuiltInScenario.UnitValidationGround))]
    public bool ValidationGroundRefreshBaseline { get; set; } = false;

    /// <summary>Path to write the validation-ground JSON report to. Empty writes none.</summary>
    [HarnessParameter("ValidationGround", "ValidationGroundReportPath.", nameof(BuiltInScenario.UnitValidationGround))]
    public string? ValidationGroundReportPath { get; set; } = null;

    /// <summary>
    /// Card WI-M2-04: path to the EE2 reference validation profile (for example the shipped
    /// <c>ee2-reference-validation.json</c>). When set, the roster imports through the shared
    /// unit-content layer before spawn and the EE2 reference contracts run beside the Generals
    /// roster in the same session, writing ee2-contract-report.json. Empty keeps the validation
    /// ground unchanged.
    /// </summary>
    [HarnessParameter("ValidationGround", "ValidationGroundEe2ProfilePath.", nameof(BuiltInScenario.UnitValidationGround))]
    public string? ValidationGroundEe2ProfilePath { get; set; } = null;

    /// <summary>
    /// When true (default), a windowed validation-ground run starts paused with the orders held so
    /// the starting conditions can be inspected; Space (or the overlay button) resumes, which runs
    /// the settle stage and issues the attack orders. Afterwards Space toggles active pause.
    /// Ignored by headless runs, which always auto-issue for byte-identical reports.
    /// </summary>
    [HarnessParameter("ValidationGround", "ValidationGroundHoldOrders.", nameof(BuiltInScenario.UnitValidationGround))]
    public bool ValidationGroundHoldOrders { get; set; } = true;

    /// <summary>
    /// When true (default), the run exits once every measurable unit has resolved (its target
    /// destroyed) or the frame cap is reached, then writes the report/baseline. When false, the run
    /// does not auto-exit: the game keeps running so the roster can be observed, and the report is
    /// written when the window is closed. Only honoured in windowed mode; headless always exits on
    /// completion, since a never-exiting headless run has no window to close and would hang.
    /// </summary>
    [HarnessParameter("Display", "ExitAfterTargetsDestroyed.", nameof(BuiltInScenario.UnitValidationGround))]
    public bool ExitAfterTargetsDestroyed { get; set; } = true;

    /// <summary>
    /// Options for <see cref="BuiltInScenario.EngagementCapture"/>. Null uses the defaults
    /// (a USA Missile Defender firing at a China BattleMaster).
    /// </summary>
    [HarnessParameter("EngagementCapture", "EngagementCapture.", nameof(BuiltInScenario.EngagementCapture))]
    public EngagementCaptureOptions? EngagementCapture { get; set; } = null;

    // ── Mass battle deployment (M0) ─────────────────────────────────────────

    /// <summary>
    /// Path to a <see cref="MassBattleDeploymentProfile"/> JSON. Empty uses the embedded
    /// deepdivide profile shipped beside the executable.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleDeploymentProfilePath.", nameof(BuiltInScenario.MassBattleDeployment))]
    public string? MassBattleDeploymentProfilePath { get; set; } = null;

    /// <summary>Spawn and display the armies without running the map acceptance probes.</summary>
    [HarnessParameter("MassBattle", "MassBattleDeploymentPreview.", nameof(BuiltInScenario.MassBattleDeployment))]
    public bool MassBattleDeploymentPreview { get; set; } = false;

    /// <summary>Where to write the deployment proof report JSON. Empty writes beside the executable.</summary>
    [HarnessParameter("MassBattle", "MassBattleDeploymentReportPath.", nameof(BuiltInScenario.MassBattleDeployment))]
    public string? MassBattleDeploymentReportPath { get; set; } = null;

    /// <summary>Optional PNG path for one deployment screenshot captured after the armies spawn.</summary>
    [HarnessParameter("MassBattle", "MassBattleDeploymentScreenshotPath.", nameof(BuiltInScenario.MassBattleDeployment))]
    public string? MassBattleDeploymentScreenshotPath { get; set; } = null;

    // ── Mass battle capacity probe (M1) ────────────────────────────────────

    /// <summary>
    /// Path to a <see cref="MassBattleDeploymentProfile"/> JSON for the capacity probe. Empty
    /// uses the embedded deepdivide capacity-probe profile shipped beside the executable.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeProfilePath.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public string? MassBattleCapacityProbeProfilePath { get; set; } = null;

    /// <summary>Where to write the engagement report JSON. Empty writes beside the executable.</summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeReportPath.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public string? MassBattleCapacityProbeReportPath { get; set; } = null;

    /// <summary>Optional PNG path for one engagement screenshot captured after the run.</summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeScreenshotPath.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public string? MassBattleCapacityProbeScreenshotPath { get; set; } = null;

    /// <summary>
    /// Frame budget for the measured engagement. Null uses the runner default (9000 frames);
    /// setup, spawn, and settle frames are not counted against it.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeFrameBudget.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public int? MassBattleCapacityProbeFrameBudget { get; set; } = null;

    // ── Rendering-scale probe (B5) ─────────────────────────────────────────

    /// <summary>Repeated-model unit count for the render-scale probe. Null uses 10000.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeUnitCount.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public int? RenderingScaleProbeUnitCount { get; set; } = null;

    /// <summary>Unit definition to repeat in the field. Null uses AmericaTankCrusader.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeDefinition.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public string? RenderingScaleProbeDefinition { get; set; } = null;

    /// <summary>Map path for the probe. Null uses deepdivide (the M1 capacity-probe map).</summary>
    [HarnessParameter("Display", "RenderingScaleProbeMapPath.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public string? RenderingScaleProbeMapPath { get; set; } = null;

    /// <summary>Warmup frames before measurement. Null uses 30.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeWarmupFrames.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public int? RenderingScaleProbeWarmupFrames { get; set; } = null;

    /// <summary>Measured frames. Null uses 120.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeMeasuredFrames.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public int? RenderingScaleProbeMeasuredFrames { get; set; } = null;

    /// <summary>Report directory for the probe JSON/CSV. Empty writes beside the executable.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeReportDirectory.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public string? RenderingScaleProbeReportDirectory { get; set; } = null;

    /// <summary>Optional PNG path for one probe screenshot captured after the run.</summary>
    [HarnessParameter("Display", "RenderingScaleProbeScreenshotPath.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public string? RenderingScaleProbeScreenshotPath { get; set; } = null;

    /// <summary>Run with the Original single-cascade shadow tier instead of Enhanced (B5-4).</summary>
    [HarnessParameter("Display", "RenderingScaleProbeLegacyShadows.", nameof(BuiltInScenario.RenderingScaleProbe))]
    public bool RenderingScaleProbeLegacyShadows { get; set; } = false;
    /// <summary>
    /// Enables the WI-B4-06 async-path scale anchor inside the capacity probe: on this frame
    /// cadence the runner submits squad-shaped asynchronous path requests and records their
    /// latency, queue backlog, and deadline misses for the hierarchical-pathfinding gate. Null
    /// disables the anchor.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeAsyncAnchorBurstEveryFrames.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public int? MassBattleCapacityProbeAsyncAnchorBurstEveryFrames { get; set; } = null;

    /// <summary>Requests per anchor burst; defaults to 8 when only the cadence is set.</summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeAsyncAnchorRequestsPerBurst.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public int? MassBattleCapacityProbeAsyncAnchorRequestsPerBurst { get; set; } = null;

    /// <summary>
    /// WI-M4-03: run both probe armies under the hierarchical AI decision policy instead of
    /// the scripted general attack (jets keep the scripted sortie loop until WI-M4-05).
    /// Default off, which keeps the M1 scripted behavior comparable.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleCapacityProbeArmyAi.", nameof(BuiltInScenario.MassBattleCapacityProbe))]
    public bool? MassBattleCapacityProbeArmyAi { get; set; } = null;

    /// <summary>Path to the WI-M5-02 battle profile JSON. Empty uses the embedded profile.</summary>
    [HarnessParameter("MassBattle", "MassBattleProfilePath.", nameof(BuiltInScenario.MassBattle))]
    public string? MassBattleProfilePath { get; set; } = null;

    /// <summary>Battle seed (WI-M5-04); every run of the same seed reproduces the same battle.</summary>
    [HarnessParameter("MassBattle", "MassBattleSeed.", nameof(BuiltInScenario.MassBattle))]
    public int? MassBattleSeed { get; set; } = null;

    /// <summary>
    /// Frame limit for the army-eliminated outcome (WI-M5-04): a stalled battle decides by
    /// unit count at this frame. Default 54000 (30 game minutes).
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleFrameLimit.", nameof(BuiltInScenario.MassBattle))]
    public int? MassBattleFrameLimit { get; set; } = null;

    [HarnessParameter("MassBattle", "MassBattleReportPath.", nameof(BuiltInScenario.MassBattle))]
    public string? MassBattleReportPath { get; set; } = null;

    [HarnessParameter("MassBattle", "MassBattleScreenshotPath.", nameof(BuiltInScenario.MassBattle))]
    public string? MassBattleScreenshotPath { get; set; } = null;

    /// <summary>Units in the routing-choke group move (default 40).</summary>
    [HarnessParameter("RoutingChoke", "RoutingChokeUnitCount.", nameof(BuiltInScenario.RoutingChoke))]
    public int? RoutingChokeUnitCount { get; set; } = null;

    /// <summary>Width of the wall's only opening in world units (default 120).</summary>
    [HarnessParameter("RoutingChoke", "RoutingChokeGapWidth.", nameof(BuiltInScenario.RoutingChoke))]
    public int? RoutingChokeGapWidth { get; set; } = null;

    /// <summary>Frame limit before the run reports stuck units (default 9000).</summary>
    [HarnessParameter("RoutingChoke", "RoutingChokeFrameLimit.", nameof(BuiltInScenario.RoutingChoke))]
    public int? RoutingChokeFrameLimit { get; set; } = null;

    [HarnessParameter("RoutingChoke", "Frames without movement before stopping (default 90).", nameof(BuiltInScenario.RoutingChoke))]
    public int RoutingChokeProgressTimeoutFrames { get; set; } = 90;

    [HarnessParameter("RoutingChoke", "A timed gate can reopen; wait until arrival or the frame limit.", nameof(BuiltInScenario.RoutingChoke))]
    public bool RoutingChokeHasTimedGate { get; set; }

    [HarnessParameter("RoutingChoke", "RoutingChokeReportPath.", nameof(BuiltInScenario.RoutingChoke))]
    public string? RoutingChokeReportPath { get; set; } = null;

    /// <summary>Directory for the PNG sequence; empty captures nothing.</summary>
    [HarnessParameter("RoutingChoke", "RoutingChokeCaptureDirectory.", nameof(BuiltInScenario.RoutingChoke))]
    public string? RoutingChokeCaptureDirectory { get; set; } = null;

    /// <summary>One PNG every N logic frames (default 15).</summary>
    [HarnessParameter("RoutingChoke", "RoutingChokeCaptureStride.", nameof(BuiltInScenario.RoutingChoke))]
    public int? RoutingChokeCaptureStride { get; set; } = null;

    [HarnessParameter("RoutingChoke", "RoutingChokeMapPath.", nameof(BuiltInScenario.RoutingChoke))]
    public string? RoutingChokeMapPath { get; set; } = null;

    /// <summary>First seed of a batch (WI-M5-06); the batch runs consecutive seeds from here.</summary>
    [HarnessParameter("MassBattle", "MassBattleBatchSeedStart.", nameof(BuiltInScenario.MassBattleBatch))]
    public int? MassBattleBatchSeedStart { get; set; } = null;

    /// <summary>Number of runs in a batch (WI-M5-06); default 10, WI-M5-07 sizes acceptance.</summary>
    [HarnessParameter("MassBattle", "MassBattleBatchRuns.", nameof(BuiltInScenario.MassBattleBatch))]
    public int? MassBattleBatchRuns { get; set; } = null;

    [HarnessParameter("MassBattle", "MassBattleBatchReportPath.", nameof(BuiltInScenario.MassBattleBatch))]
    public string? MassBattleBatchReportPath { get; set; } = null;

    /// <summary>
    /// WI-M5-07 batch variant: Battle (profile as authored), Mirror (side A's roster on both
    /// sides, the map-bias control), or Swap (rosters exchanged between zones, separating roster
    /// strength from zone advantage). Rewrites side rosters only.
    /// </summary>
    [HarnessParameter("MassBattle", "MassBattleBatchVariant.", nameof(BuiltInScenario.MassBattleBatch))]
    public string? MassBattleBatchVariant { get; set; } = null;
}
