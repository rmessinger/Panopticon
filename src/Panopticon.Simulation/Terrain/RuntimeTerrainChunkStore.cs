#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;

namespace Panopticon.Terrain;

/// <summary>
/// Bounded chunk residency over an <see cref="ITerrainChunkSource"/>. Eviction removes the least recently
/// used clean chunk and never discards dirty data. Access order uses a monotonic counter, not wall time.
/// </summary>
public sealed class RuntimeTerrainChunkStore
{
    private const int IntervalsPerChunkAxis = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int ApronSamples = MapChunkGeometry.ApronSamplesPerSide;

    // Stored arrays overlap only with the eight lattice-neighbour chunks.
    private static readonly (int Dx, int Dy)[] NeighbourOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    ];

    private readonly ITerrainChunkSource _source;

    private readonly Dictionary<ChunkKey, (TerrainChunkData Data, long LastAccess)> _resident = new();

    private long _accessCounter;

    public RuntimeTerrainChunkStore(ITerrainChunkSource source, int maxResidentChunks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResidentChunks);
        _source = source;
        MaxResidentChunks = maxResidentChunks;
    }

    public int MaxResidentChunks { get; }

    public int ResidentCount => _resident.Count;

    public IReadOnlyCollection<ChunkKey> ResidentKeys => SortedKeys(_resident.Keys);

    public IReadOnlyCollection<ChunkKey> DirtyKeys =>
        SortedKeys(_resident.Where(entry => entry.Value.Data.IsDirty).Select(entry => entry.Key));

    public bool IsResident(ChunkKey key) => _resident.ContainsKey(key);

    public bool IsDirty(ChunkKey key) => _resident.TryGetValue(key, out var entry) && entry.Data.IsDirty;

    public TerrainChunkData GetChunk(ChunkKey key)
    {
        if (_resident.TryGetValue(key, out var entry))
        {
            _resident[key] = (entry.Data, ++_accessCounter);
            return entry.Data;
        }

        var loaded = _source.LoadChunk(key);
        MakeRoom(1, protectedKeys: null);
        SynchronizeSeams(loaded);
        _resident[key] = (loaded, ++_accessCounter);
        return loaded;
    }

    public IReadOnlyList<TerrainChunkData> LoadRegion(int minChunkX, int minChunkY, int maxChunkX, int maxChunkY)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minChunkX);
        ArgumentOutOfRangeException.ThrowIfNegative(minChunkY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxChunkX, _source.ChunksX);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxChunkY, _source.ChunksY);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkX, minChunkX);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkY, minChunkY);

        var regionCount = (long)(maxChunkX - minChunkX + 1) * (maxChunkY - minChunkY + 1);

        if (regionCount > MaxResidentChunks)
        {
            throw new TerrainChunkStoreCapacityException(
                $"Region of {regionCount} chunks exceeds the resident cap of {MaxResidentChunks}.");
        }

        var keys = new List<ChunkKey>((int)regionCount);

        for (var cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (var cx = minChunkX; cx <= maxChunkX; cx++)
            {
                keys.Add(new ChunkKey(cx, cy));
            }
        }

        var region = new HashSet<ChunkKey>(keys);
        var fetched = new Dictionary<ChunkKey, TerrainChunkData>();

        foreach (var key in keys)
        {
            if (!_resident.ContainsKey(key))
            {
                fetched[key] = _source.LoadChunk(key);
            }
        }

        MakeRoom(fetched.Count, region);

        var result = new List<TerrainChunkData>(keys.Count);

        foreach (var key in keys)
        {
            if (fetched.TryGetValue(key, out var loaded))
            {
                SynchronizeSeams(loaded);
                _resident[key] = (loaded, ++_accessCounter);
                result.Add(loaded);
                continue;
            }

            var resident = _resident[key].Data;
            _resident[key] = (resident, ++_accessCounter);
            result.Add(resident);
        }

        return result;
    }

    public float GetHeight(int latticeX, int latticeY)
    {
        var (key, coreI, coreJ) = ResolveLattice(latticeX, latticeY);
        var chunk = GetChunk(key);
        return chunk.StoredHeights[StoredIndex(chunk, coreI + ApronSamples, coreJ + ApronSamples)];
    }

    public bool TryGetMaterial(int latticeX, int latticeY, out byte material)
    {
        var (key, coreI, coreJ) = ResolveLattice(latticeX, latticeY);
        var chunk = GetChunk(key);

        if (chunk.CoreMaterials is null)
        {
            material = 0;
            return false;
        }

        material = chunk.CoreMaterials[(coreJ * chunk.CoreWidth) + coreI];
        return true;
    }

    public bool TryGetMask(int latticeX, int latticeY, out byte mask)
    {
        var (key, coreI, coreJ) = ResolveLattice(latticeX, latticeY);
        var chunk = GetChunk(key);

        if (chunk.CoreMasks is null)
        {
            mask = 0;
            return false;
        }

        mask = chunk.CoreMasks[(coreJ * chunk.CoreWidth) + coreI];
        return true;
    }

    public float GetStoredHeight(ChunkKey key, int storedX, int storedY)
    {
        var chunk = GetChunk(key);
        ArgumentOutOfRangeException.ThrowIfNegative(storedX);
        ArgumentOutOfRangeException.ThrowIfNegative(storedY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(storedX, StoredExtentX(chunk));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(storedY, StoredExtentY(chunk));
        return chunk.StoredHeights[StoredIndex(chunk, storedX, storedY)];
    }

    public void SetHeight(int latticeX, int latticeY, float value)
    {
        var (key, _, _) = ResolveLattice(latticeX, latticeY);
        var owner = GetChunk(key);

        foreach (var entry in _resident.Values)
        {
            if (TryLocateStored(entry.Data, latticeX, latticeY, out var storedX, out var storedY))
            {
                WriteStoredCell(entry.Data, storedX, storedY, value);
            }
        }

        owner.IsDirty = true;
    }

    public Chunk ExportChunk(ChunkKey key) => GetChunk(key).ToChunk();

    public void MarkClean(ChunkKey key)
    {
        if (_resident.TryGetValue(key, out var entry))
        {
            entry.Data.IsDirty = false;
        }
    }

    public bool Evict(ChunkKey key)
    {
        if (!_resident.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.Data.IsDirty)
        {
            throw new TerrainChunkStoreCapacityException(
                $"Chunk ({key.Cx}, {key.Cy}) is dirty; evicting it would discard unsaved edits.");
        }

        _resident.Remove(key);
        return true;
    }

    private (ChunkKey Key, int CoreI, int CoreJ) ResolveLattice(int latticeX, int latticeY)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(latticeX);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(latticeX, _source.LatticeWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(latticeY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(latticeY, _source.LatticeHeight);

        var cx = Math.Min(latticeX / IntervalsPerChunkAxis, _source.ChunksX - 1);
        var cy = Math.Min(latticeY / IntervalsPerChunkAxis, _source.ChunksY - 1);
        return (new ChunkKey(cx, cy), latticeX - (cx * IntervalsPerChunkAxis), latticeY - (cy * IntervalsPerChunkAxis));
    }

    private void MakeRoom(int incoming, HashSet<ChunkKey>? protectedKeys)
    {
        var overflow = _resident.Count + incoming - MaxResidentChunks;

        if (overflow <= 0)
        {
            return;
        }

        var victims = _resident
            .Where(entry => !entry.Value.Data.IsDirty && (protectedKeys is null || !protectedKeys.Contains(entry.Key)))
            .OrderBy(entry => entry.Value.LastAccess)
            .Take(overflow)
            .Select(entry => entry.Key)
            .ToList();

        if (victims.Count < overflow)
        {
            throw new TerrainChunkStoreCapacityException(
                $"Chunk store is full: {_resident.Count} of {MaxResidentChunks} resident, {incoming} incoming, " +
                $"{victims.Count} evictable clean chunks, {overflow} required.");
        }

        foreach (var victim in victims)
        {
            _resident.Remove(victim);
        }
    }

    // Resident core wins, then the incoming core, then the first resident apron copy; neighbours follow NeighbourOffsets order.
    private void SynchronizeSeams(TerrainChunkData incoming)
    {
        var neighbours = ResidentNeighbours(incoming.Key);

        if (neighbours.Count == 0)
        {
            return;
        }

        var originX = (incoming.Key.Cx * IntervalsPerChunkAxis) - ApronSamples;
        var originY = (incoming.Key.Cy * IntervalsPerChunkAxis) - ApronSamples;

        for (var storedY = 0; storedY < StoredExtentY(incoming); storedY++)
        {
            for (var storedX = 0; storedX < StoredExtentX(incoming); storedX++)
            {
                var latticeX = originX + storedX;
                var latticeY = originY + storedY;
                var seamCovered = false;
                float? residentCore = null;
                float? residentApron = null;

                foreach (var neighbour in neighbours)
                {
                    if (!TryLocateStored(neighbour, latticeX, latticeY, out var neighbourX, out var neighbourY))
                    {
                        continue;
                    }

                    seamCovered = true;
                    var sample = neighbour.StoredHeights[StoredIndex(neighbour, neighbourX, neighbourY)];

                    if (IsCore(neighbour, neighbourX, neighbourY))
                    {
                        residentCore ??= sample;
                    }
                    else
                    {
                        residentApron ??= sample;
                    }
                }

                if (!seamCovered)
                {
                    continue;
                }

                var target = residentCore
                    ?? (IsCore(incoming, storedX, storedY)
                        ? incoming.StoredHeights[StoredIndex(incoming, storedX, storedY)]
                        : residentApron!.Value);

                WriteStoredCell(incoming, storedX, storedY, target);

                foreach (var neighbour in neighbours)
                {
                    if (TryLocateStored(neighbour, latticeX, latticeY, out var neighbourX, out var neighbourY))
                    {
                        WriteStoredCell(neighbour, neighbourX, neighbourY, target);
                    }
                }
            }
        }
    }

    private List<TerrainChunkData> ResidentNeighbours(ChunkKey key)
    {
        var neighbours = new List<TerrainChunkData>(NeighbourOffsets.Length);

        foreach (var (dx, dy) in NeighbourOffsets)
        {
            if (_resident.TryGetValue(new ChunkKey(key.Cx + dx, key.Cy + dy), out var entry))
            {
                neighbours.Add(entry.Data);
            }
        }

        return neighbours;
    }

    private static bool TryLocateStored(TerrainChunkData chunk, int latticeX, int latticeY, out int storedX, out int storedY)
    {
        storedX = latticeX - ((chunk.Key.Cx * IntervalsPerChunkAxis) - ApronSamples);
        storedY = latticeY - ((chunk.Key.Cy * IntervalsPerChunkAxis) - ApronSamples);
        return (uint)storedX < (uint)StoredExtentX(chunk) && (uint)storedY < (uint)StoredExtentY(chunk);
    }

    private static bool IsCore(TerrainChunkData chunk, int storedX, int storedY) =>
        storedX >= ApronSamples && storedX < ApronSamples + chunk.CoreWidth
        && storedY >= ApronSamples && storedY < ApronSamples + chunk.CoreHeight;

    private static void WriteStoredCell(TerrainChunkData chunk, int storedX, int storedY, float value)
    {
        var index = StoredIndex(chunk, storedX, storedY);

        if (BitConverter.SingleToInt32Bits(chunk.StoredHeights[index]) == BitConverter.SingleToInt32Bits(value))
        {
            return;
        }

        chunk.StoredHeights[index] = value;
        chunk.IsDirty = true;
    }

    private static int StoredExtentX(TerrainChunkData chunk) => chunk.CoreWidth + (2 * ApronSamples);

    private static int StoredExtentY(TerrainChunkData chunk) => chunk.CoreHeight + (2 * ApronSamples);

    private static int StoredIndex(TerrainChunkData chunk, int storedX, int storedY) =>
        (storedY * StoredExtentX(chunk)) + storedX;

    private static ChunkKey[] SortedKeys(IEnumerable<ChunkKey> keys) =>
        keys.OrderBy(key => key.Cx).ThenBy(key => key.Cy).ToArray();
}
