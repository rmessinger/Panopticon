using System.Text.Json.Serialization;
using OpenSage.Graphics.Cameras;
using Veldrid;

namespace OpenSage.Tools.TestHarness;

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
}
