using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit for adding or removing a MapObject from the ObjectsList.
/// </summary>
public sealed class ObjectPlacementEdit : IMapEdit
{
    private readonly ObjectsList _objectsList;
    private readonly MapObject _mapObject;
    private readonly bool _isPlacement; // true = add on Apply, false = remove on Apply

    public string Description => _isPlacement ? "Place Object" : "Delete Object";

    private ObjectPlacementEdit(ObjectsList objectsList, MapObject mapObject, bool isPlacement)
    {
        _objectsList = objectsList;
        _mapObject = mapObject;
        _isPlacement = isPlacement;
    }

    /// <summary>
    /// Creates a placement edit. The edit is not applied here: the map's
    /// <see cref="UndoRedoStack{T}"/> applies edits on Push, which is the single application
    /// owner (WI-A5-27).
    /// </summary>
    public static ObjectPlacementEdit CreatePlacement(ObjectsList objectsList, MapObject mapObject)
    {
        return new ObjectPlacementEdit(objectsList, mapObject, isPlacement: true);
    }

    /// <summary>
    /// Creates a deletion edit; application is owned by the undo stack's Push (WI-A5-27).
    /// </summary>
    public static ObjectPlacementEdit CreateDeletion(ObjectsList objectsList, MapObject mapObject)
    {
        return new ObjectPlacementEdit(objectsList, mapObject, isPlacement: false);
    }

    public void Apply()
    {
        if (_isPlacement)
        {
            AddObject();
        }
        else
        {
            RemoveObject();
        }
    }

    public void Revert()
    {
        if (_isPlacement)
        {
            RemoveObject();
        }
        else
        {
            AddObject();
        }
    }

    private void AddObject()
    {
        _objectsList.AddObject(_mapObject);
    }

    private void RemoveObject()
    {
        _objectsList.RemoveObject(_mapObject);
    }
}
