#nullable enable annotations

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Recording;

/// <summary>
/// Caches install-archive content hashes outside the install directory, keyed by archive name,
/// size, and last-write timestamp so untouched archives are not re-hashed on every run.
/// </summary>
public sealed class ArchiveContentHashCache
{
    private static readonly Lazy<ArchiveContentHashCache> LazyShared = new(() => new ArchiveContentHashCache());

    private readonly string _cacheFilePath;
    private readonly object _gate = new();
    private Dictionary<string, ulong> _entries;

    public static ArchiveContentHashCache Shared => LazyShared.Value;

    public ArchiveContentHashCache()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Panopticon",
            "archive-hash-cache.json"))
    {
    }

    public ArchiveContentHashCache(string cacheFilePath)
    {
        _cacheFilePath = cacheFilePath;
        _entries = Load(cacheFilePath);
    }

    public int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public ulong GetOrCompute(string archiveFilePath)
    {
        var info = new FileInfo(archiveFilePath);
        var key = $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        ulong hash;
        using (var stream = File.OpenRead(archiveFilePath))
        {
            var hasher = new XxHash3();
            var buffer = new byte[81_920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, read));
            }

            hash = BinaryPrimitives.ReadUInt64BigEndian(hasher.GetCurrentHash());
        }

        lock (_gate)
        {
            _entries[key] = hash;
            Save(_cacheFilePath, _entries);
        }

        return hash;
    }

    private static Dictionary<string, ulong> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var entries = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && ulong.TryParse(property.Value.GetString(), out var hash))
                {
                    entries[property.Name] = hash;
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Save(string path, Dictionary<string, ulong> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(path);
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            foreach (var (key, hash) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                writer.WriteString(key, hash.ToString());
            }

            writer.WriteEndObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimization only; hashing still works without persistence.
        }
    }
}
