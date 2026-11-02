using System;
using System.Collections.Generic;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Deterministic least-recently-used cache with a fixed capacity, bounding the terrain
/// and decoded-imagery chunk retention of one reader. Not thread-safe; the owning
/// reader is single-threaded unless callers synchronize externally.
/// </summary>
internal sealed class PayloadReaderCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;

    private readonly LinkedList<TKey> _order = [];

    private readonly Dictionary<TKey, (LinkedListNode<TKey> Node, TValue Value)> _entries;

    internal PayloadReaderCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _entries = new Dictionary<TKey, (LinkedListNode<TKey>, TValue)>(capacity);
    }

    internal int Count => _entries.Count;

    /// <summary>Returns the cached value and marks it most recently used.</summary>
    internal bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            _order.Remove(entry.Node);
            _order.AddFirst(entry.Node);
            value = entry.Value;

            return true;
        }

        value = default!;

        return false;
    }

    /// <summary>Inserts or refreshes one entry, evicting the least recently used entry beyond the capacity.</summary>
    internal void Store(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            _order.Remove(entry.Node);
            _entries[key] = (entry.Node, value);
            _order.AddFirst(entry.Node);

            return;
        }

        var node = new LinkedListNode<TKey>(key);
        _entries.Add(key, (node, value));
        _order.AddFirst(node);

        while (_entries.Count > _capacity)
        {
            var evicted = _order.Last!;
            _order.RemoveLast();
            _entries.Remove(evicted.Value);
        }
    }

    internal bool Remove(TKey key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return false;
        }

        _order.Remove(entry.Node);

        return true;
    }

    /// <summary>Drops every entry so disposal releases the retained chunk arrays.</summary>
    internal void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }
}
