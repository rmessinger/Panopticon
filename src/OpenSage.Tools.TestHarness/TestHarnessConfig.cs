using System.Text.Json.Serialization;
using Veldrid;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Persistent configuration for the TestHarness tool.
/// Loaded from <c>TestHarness.json</c> in the executable directory.
/// </summary>
public sealed class TestHarnessConfig
{
    /// <summary>
    /// Path to a scenario JSON profile.
    /// Null uses the built-in Alpine Assault USA vs GLA scenario.
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
    /// Override the game installation path instead of auto-detecting from the registry
    /// or standard environment variables.
    /// </summary>
    public string? GamePath { get; set; } = null;
}
