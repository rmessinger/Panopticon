using System.Numerics;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Represents the mouse/touch pointer event forwarded from the platform viewport handler.
/// </summary>
public sealed class ViewportPointerEvent
{
    public required Vector2 Position { get; init; }
    public required ViewportPointerEventType EventType { get; init; }
    public required ViewportMouseButton Button { get; init; }
    public float ScrollDelta { get; init; }

    /// <summary>
    /// World-space terrain hit point, populated by <see cref="GameViewportRenderer"/>
    /// via a camera ray-cast before dispatching to the active tool. Null if no terrain
    /// is under the cursor or no scene is loaded.
    /// </summary>
    public Vector3? WorldPosition { get; set; }
}

public enum ViewportPointerEventType
{
    Down,
    Move,
    Up,
    Scroll,
}

public enum ViewportMouseButton
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>
/// Represents a keyboard event forwarded from the platform viewport handler.
/// </summary>
public sealed class ViewportKeyEvent
{
    public required string Key { get; init; }
    public required bool IsDown { get; init; }
    public bool Control { get; init; }
    public bool Shift { get; init; }
}
