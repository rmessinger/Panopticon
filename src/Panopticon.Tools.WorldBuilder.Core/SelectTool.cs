using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Arrow / Select tool — selects objects that are close to the clicked world position.
/// </summary>
public sealed class SelectTool : IEditorTool
{
    private const float SelectionRadius = 20f;

    public string Name => "Select";

    public MapObject? SelectedObject { get; private set; }

    /// <summary>Raised when the selected object changes.</summary>
    public event Action<MapObject?>? SelectionChanged;

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList?.Objects is null)
        {
            return;
        }

        var clickPos = e.WorldPosition.Value;
        MapObject? closest = null;
        float closestDist = SelectionRadius;

        foreach (var obj in objectsList.Objects)
        {
            float dist = Vector3.Distance(clickPos, obj.Position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = obj;
            }
        }

        SelectedObject = closest;
        SelectionChanged?.Invoke(SelectedObject);
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => SelectedObject;
}
