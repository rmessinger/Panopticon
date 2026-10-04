#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Panopticon.Recording;

/// <summary>A cached derived checkpoint (WI-A1-23 §6.3).</summary>
public sealed record CachedCheckpoint(uint Frame, byte[] Checkpoint)
{
}

/// <summary>
/// An in-memory LRU of derived checkpoints (WI-A1-23 §6.3), keyed by run id and frame under a byte
/// budget. Nothing is written to disk. A derived checkpoint captures the rebuilt path, so later
/// scrubs that restore from it agree with the first reconstruction rather than with the original
/// run (A1-6 §3).
/// </summary>
public sealed class ReconstructionCache
{
    private sealed class Entry
    {
        public required byte[] Checkpoint { get; init; }
    }

    private readonly LinkedList<(string RunId, uint Frame, Entry Entry)> _lru = new();
    private readonly Dictionary<(string RunId, uint Frame), LinkedListNode<(string RunId, uint Frame, Entry Entry)>> _index = new();
    private readonly object _gate = new();

    public ReconstructionCache(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budgetBytes, 0);
        BudgetBytes = budgetBytes;
    }

    public long BudgetBytes { get; }

    public long TotalBytes { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>
    /// Stores a derived checkpoint for <paramref name="runId"/> at <paramref name="frame"/>.
    /// Replaces any entry already cached at that key; entries over the budget are not stored,
    /// and the least recently used entries are evicted until the budget is met.
    /// </summary>
    public void Store(string runId, uint frame, byte[] checkpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(checkpoint);

        lock (_gate)
        {
            RemoveLocked(runId, frame);
            if (checkpoint.LongLength > BudgetBytes)
            {
                return;
            }

            var node = new LinkedListNode<(string, uint, Entry)>((runId, frame, new Entry
            {
                Checkpoint = checkpoint,
            }));
            _lru.AddFirst(node);
            _index[(runId, frame)] = node;
            TotalBytes += checkpoint.LongLength;
            while (TotalBytes > BudgetBytes && _lru.Last is not null)
            {
                var last = _lru.Last.Value;
                TotalBytes -= last.Entry.Checkpoint.LongLength;
                _lru.RemoveLast();
                _index.Remove((last.RunId, last.Frame));
            }
        }
    }

    /// <summary>
    /// Returns the cached checkpoint nearest at or before <paramref name="frame"/>, or null when
    /// none is cached for <paramref name="runId"/>.
    /// </summary>
    public CachedCheckpoint? TryGetNearest(string runId, uint frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        lock (_gate)
        {
            for (var candidate = (long)frame; candidate >= 0; candidate--)
            {
                if (!_index.TryGetValue((runId, (uint)candidate), out var node))
                {
                    continue;
                }

                _lru.Remove(node);
                _lru.AddFirst(node);
                return new CachedCheckpoint((uint)candidate, node.Value.Entry.Checkpoint);
            }

            return null;
        }
    }

    /// <summary>Removes every cached entry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
            _index.Clear();
            TotalBytes = 0;
        }
    }

    private void RemoveLocked(string runId, uint frame)
    {
        if (_index.Remove((runId, frame), out var node))
        {
            TotalBytes -= node.Value.Entry.Checkpoint.LongLength;
            _lru.Remove(node);
        }
    }
}
