#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Panopticon.Recording;

/// <summary>A cached derived checkpoint (WI-A1-23 §6.3) with the boundary facts needed to validate it.</summary>
public sealed record CachedCheckpoint(uint Frame, string IdentityHash, ulong RecordedWorldHash, byte[] Checkpoint)
{
}

/// <summary>
/// An in-memory LRU of derived checkpoints (WI-A1-23 §6.3), keyed by run id and frame under a byte
/// budget. Entries are only valid for the identity that produced them, and only while the bundle's
/// recorded hash still matches the hash captured with the checkpoint; <see cref="TryGetNearest"/>
/// discards entries that fail either rule before they are used. Nothing is written inside the
/// immutable bundle directory.
/// </summary>
public sealed class ReconstructionCache
{
    private sealed class Entry
    {
        public required string IdentityHash { get; init; }

        public required ulong RecordedWorldHash { get; init; }

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
    public void Store(string runId, uint frame, string identityHash, ulong recordedWorldHash, byte[] checkpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityHash);
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
                IdentityHash = identityHash,
                RecordedWorldHash = recordedWorldHash,
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
    /// no valid entry remains. An entry stored under a different identity purges the whole run
    /// (identity change invalidates every derived checkpoint); an entry whose frame is missing
    /// from <paramref name="recordedWorldHashes"/>, or whose stored hash differs from it, is
    /// discarded and the search continues downward.
    /// </summary>
    public CachedCheckpoint? TryGetNearest(
        string runId,
        string identityHash,
        uint frame,
        IReadOnlyDictionary<uint, ulong>? recordedWorldHashes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityHash);

        lock (_gate)
        {
            for (var candidate = (long)frame; candidate >= 0; candidate--)
            {
                if (!_index.TryGetValue((runId, (uint)candidate), out var node))
                {
                    continue;
                }

                var entry = node.Value.Entry;
                if (!string.Equals(entry.IdentityHash, identityHash, StringComparison.Ordinal))
                {
                    PurgeRunLocked(runId);
                    return null;
                }

                if (recordedWorldHashes is not null
                    && (!recordedWorldHashes.TryGetValue((uint)candidate, out var recorded)
                        || recorded != entry.RecordedWorldHash))
                {
                    RemoveLocked(runId, (uint)candidate);
                    continue;
                }

                _lru.Remove(node);
                _lru.AddFirst(node);
                return new CachedCheckpoint((uint)candidate, entry.IdentityHash, entry.RecordedWorldHash, entry.Checkpoint);
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

    private void PurgeRunLocked(string runId)
    {
        var node = _lru.First;
        while (node is not null)
        {
            var next = node.Next;
            if (string.Equals(node.Value.RunId, runId, StringComparison.Ordinal))
            {
                TotalBytes -= node.Value.Entry.Checkpoint.LongLength;
                _index.Remove((node.Value.RunId, node.Value.Frame));
                _lru.Remove(node);
            }

            node = next;
        }
    }
}
