namespace OpenSage;

public enum MouseClickMode
{
    /// <summary>
    /// Modern RTS convention: right-click issues smart orders (move, attack),
    /// left-click is used exclusively for selection and drag-selection.
    /// </summary>
    Standard,

    /// <summary>
    /// Classic Generals convention: left-click issues orders, right-click cancels
    /// the active order generator.
    /// </summary>
    Classic,
}
