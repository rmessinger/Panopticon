using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit for deleting multiple MapObjects from the ObjectsList.
/// Captures the pre-delete array reference to restore exact interleaved order on revert.
/// </summary>
public sealed class RegionObjectDeleteEdit : IMapEdit
{
    private readonly ObjectsList _objectsList;
    private readonly MapObject[] _selected;
    // Arrays in ObjectsList are replaced on edit and never mutated in place,
    // so this reference is a stable snapshot of the pre-delete list.
    private readonly MapObject[] _preDeleteSnapshot;

    public string Description => $"Delete {_selected.Length} Objects";

    public RegionObjectDeleteEdit(ObjectsList objectsList, IReadOnlyList<MapObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objectsList);
        ArgumentNullException.ThrowIfNull(objects);

        _objectsList = objectsList;
        _selected = objects.ToArray();
        _preDeleteSnapshot = objectsList.Objects;
    }

    public void Apply()
    {
        _objectsList.RemoveObjects(_selected);
    }

    public void Revert()
    {
        _objectsList.ReplaceContents(_preDeleteSnapshot);
    }
}
