using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit for placing multiple MapObjects into the ObjectsList in a single operation.
/// </summary>
public sealed class BulkObjectPlacementEdit : IMapEdit
{
    private readonly ObjectsList _objectsList;
    private readonly MapObject[] _snapshot;

    public string Description => $"Place {_snapshot.Length} Objects";

    private BulkObjectPlacementEdit(ObjectsList objectsList, MapObject[] snapshot)
    {
        _objectsList = objectsList;
        _snapshot = snapshot;
    }

    /// <summary>
    /// Creates a bulk placement edit with a defensive snapshot of the input objects.
    /// Application is owned by the undo stack's Push (WI-A5-27).
    /// </summary>
    public static BulkObjectPlacementEdit CreatePlacement(ObjectsList objectsList, IEnumerable<MapObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objectsList);
        ArgumentNullException.ThrowIfNull(objects);

        var snapshot = objects.ToArray();
        return new BulkObjectPlacementEdit(objectsList, snapshot);
    }

    public void Apply()
    {
        _objectsList.AddObjects(_snapshot);
    }

    public void Revert()
    {
        _objectsList.RemoveObjects(_snapshot);
    }
}
