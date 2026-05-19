using System.Text.Json.Serialization;
using Veldrid;

namespace OpenSage.Tools.ScenarioOrchestrator;

/// <summary>
/// Persistent configuration for the ScenarioOrchestrator tool.
/// Loaded from <c>ScenarioOrchestrator.json</c> in the executable directory.
/// </summary>
public sealed class ScenarioOrchestratorConfig
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

    /// <summary>Whether to start in fullscreen mode.</summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// Keep the exported .rep file on disk after the run instead of deleting it.
    /// </summary>
    public bool KeepReplay { get; set; } = false;

    /// <summary>
    /// Override the game installation path instead of auto-detecting from the registry
    /// or standard environment variables.
    /// </summary>
    public string? GamePath { get; set; } = null;
}
