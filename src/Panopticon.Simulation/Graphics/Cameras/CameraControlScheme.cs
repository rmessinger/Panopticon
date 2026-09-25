namespace Panopticon.Graphics.Cameras;

/// <summary>
/// Determines how mouse and keyboard input maps to camera movement.
/// </summary>
public enum CameraControlScheme
{
    /// <summary>
    /// The default Generals-style scheme: right-drag to pan, middle-drag to rotate.
    /// </summary>
    Generals,

    /// <summary>
    /// Total War-style scheme: right-drag to rotate (yaw + pitch), middle-drag to pan,
    /// Q/E to rotate, arrow keys/WASD to pan.
    /// </summary>
    TotalWar,
}
