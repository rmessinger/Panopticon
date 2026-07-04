namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The lifecycle phase of a predecessor action that can serve as a trigger for a subsequent action.
/// </summary>
public enum ActionPhase
{
    /// <summary>
    /// The predecessor's order has been dispatched to the game engine (i.e. construction started).
    /// </summary>
    Dispatched,

    /// <summary>
    /// The predecessor's game effect has been fully observed (e.g. a building finished construction).
    /// </summary>
    Completed,
}
