using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit for moving multiple MapObjects by a delta offset.
/// Captures before-positions as value copies so Revert restores exact positions.
/// </summary>
public sealed class RegionObjectMoveEdit : IMapEdit
{
    private readonly (MapObject Object, Vector3 BeforePosition)[] _entries;
    private readonly Vector3 _delta;

    public string Description => $"Move {_entries.Length} Objects";

    public RegionObjectMoveEdit(IReadOnlyList<MapObject> objects, Vector3 delta)
    {
        ArgumentNullException.ThrowIfNull(objects);

        _delta = delta;
        _entries = new (MapObject, Vector3)[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            ArgumentNullException.ThrowIfNull(obj);
            _entries[i] = (obj, obj.Position);
        }
    }

    public void Apply()
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            _entries[i].Object.Position = _entries[i].BeforePosition + _delta;
        }
    }

    public void Revert()
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            _entries[i].Object.Position = _entries[i].BeforePosition;
        }
    }
}
