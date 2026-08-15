using OpenSage.Graphics.Cameras;

namespace OpenSage;

// TODO: Should this be immutable?
// TODO: Should there be a way of merging Configuration instances?
/// <summary>
/// Contains configuration for a game instance, typically gathered from
/// command line parameters and configuration files.
/// </summary>
public sealed class Configuration
{
    public bool LoadShellMap { get; set; } = true;
    public bool UseRenderDoc { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to use a unique port for each client in a multiplayer game.
    /// Normally, <see cref="Network.Ports.SkirmishGame"/> is used, but when we want to run multiple game
    /// instances on the same machine (for debugging purposes), each client needs a different port.
    /// </summary>
    /// <value>
    ///   <c>true</c> if [use unique ports]; otherwise, <c>false</c>.
    /// </value>
    public bool UseUniquePorts { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, all audio submixer volumes are set to zero at startup.
    /// </summary>
    public bool NoAudio { get; set; } = false;

    /// <summary>
    /// Determines how mouse and keyboard input maps to camera movement during gameplay.
    /// </summary>
    public CameraControlScheme CameraControlScheme { get; set; } = CameraControlScheme.TotalWar;

    /// <summary>
    /// Determines which mouse button issues movement and attack orders.
    /// <see cref="MouseClickMode.Standard"/> uses right-click (modern RTS convention);
    /// <see cref="MouseClickMode.Classic"/> uses left-click (original Generals convention).
    /// </summary>
    public MouseClickMode MouseClickMode { get; set; } = MouseClickMode.Standard;

    /// <summary>
    /// When set, the skirmish match started via the launcher is recorded and saved
    /// to this filename inside the game's <c>Replays</c> user-data folder.
    /// </summary>
    public string SaveReplayFile { get; set; } = null;

    /// <summary>
    /// Skips the per-player skirmish starting-building (and starting-unit) spawn and the
    /// initial camera placement on it. For sandboxes that place their own objects and aim
    /// the camera themselves (e.g. the Unit Validation Ground).
    /// </summary>
    public bool SkipStartingBuildings { get; set; } = false;
}
