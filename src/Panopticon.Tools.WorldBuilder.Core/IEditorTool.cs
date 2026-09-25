namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Interface all editor tools implement. Each tool handles pointer/key input
/// routed from the viewport and exposes a ViewModel for its left-panel settings.
/// </summary>
public interface IEditorTool
{
    string Name { get; }

    void OnPointerDown(ViewportPointerEvent e, EditorMap map);
    void OnPointerMove(ViewportPointerEvent e, EditorMap map);
    void OnPointerUp(ViewportPointerEvent e, EditorMap map);

    /// <summary>
    /// Returns a ViewModel for the tool-settings panel shown in the left column.
    /// May be null if the tool has no configurable settings.
    /// </summary>
    object? GetPropertiesViewModel();
}
