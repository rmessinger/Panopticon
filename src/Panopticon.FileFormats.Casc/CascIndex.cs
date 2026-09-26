using System.Buffers.Binary;
using System.Globalization;

namespace Panopticon.FileFormats.Casc;

public sealed class CascIndex
{
    private const int SupportedVersion = 7;
    private const int ExpectedSpanSizeBytes = 4;
    private const int ExpectedSpanOffsetBytes = 5;
    private const int ExpectedKeyBytes = 9;
    private const int ExpectedArchiveOffsetBits = 30;

    private readonly Dictionary<CascKeyPrefix, CascSpan> _entries;

    private CascIndex(Dictionary<CascKeyPrefix, CascSpan> entries)
    {
        _entries = entries;
    }

    public int Count => _entries.Count;

    public static CascIndex Load(string dataDirectory)
    {
        var entries = new Dictionary<CascKeyPrefix, CascSpan>();
        foreach (var path in SelectLatestBucketFiles(dataDirectory))
        {
            LoadFile(path, entries);
        }

        return new CascIndex(entries);
    }

    public bool TryGetSpan(CascKey key, out CascSpan span)
    {
        return _entries.TryGetValue(key.Prefix, out span);
    }

    public bool TryGetSpan(CascKeyPrefix prefix, out CascSpan span)
    {
        return _entries.TryGetValue(prefix, out span);
    }

    private static IEnumerable<string> SelectLatestBucketFiles(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            throw new DirectoryNotFoundException(dataDirectory);
        }

        var buckets = new Dictionary<int, (uint Version, string Path)>();
        foreach (var path in Directory.EnumerateFiles(dataDirectory, "*.idx"))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Length != 14 ||
                !int.TryParse(fileName.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bucket) ||
                !uint.TryParse(fileName.AsSpan(2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var version))
            {
                continue;
            }

            if (!buckets.TryGetValue(bucket, out var current) || version > current.Version)
            {
                buckets[bucket] = (version, path);
            }
        }

        return buckets.OrderBy(static b => b.Key).Select(static b => b.Value.Path);
    }

    private static void LoadFile(string path, Dictionary<CascKeyPrefix, CascSpan> entries)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 16)
        {
            throw new InvalidDataException($"CASC index '{path}' is truncated.");
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2));
        var spanSizeBytes = data[12];
        var spanOffsetBytes = data[13];
        var keyBytes = data[14];
        var archiveOffsetBits = data[15];
        if (version != SupportedVersion ||
            spanSizeBytes != ExpectedSpanSizeBytes ||
            spanOffsetBytes != ExpectedSpanOffsetBytes ||
            keyBytes != ExpectedKeyBytes ||
            archiveOffsetBits != ExpectedArchiveOffsetBits)
        {
            throw new NotSupportedException(
                $"CASC index '{path}' uses unsupported layout version {version} " +
                $"({spanSizeBytes}, {spanOffsetBytes}, {keyBytes}, {archiveOffsetBits}).");
        }

        var entriesOffset = Align16(8 + checked((int)headerSize));
        if (entriesOffset + 8 > data.Length)
        {
            throw new InvalidDataException($"CASC index '{path}' has a truncated entry header.");
        }

        var entriesSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entriesOffset, 4));
        var recordSize = keyBytes + spanOffsetBytes + spanSizeBytes;
        var recordsOffset = entriesOffset + 8;
        if (entriesSize % recordSize != 0 || recordsOffset + entriesSize > data.Length)
        {
            throw new InvalidDataException($"CASC index '{path}' has an invalid entry section.");
        }

        for (var offset = recordsOffset; offset < recordsOffset + entriesSize; offset += recordSize)
        {
            var prefix = new CascKeyPrefix(data.AsSpan(offset, keyBytes));
            if (entries.ContainsKey(prefix))
            {
                continue;
            }

            var rawOffset = ReadUInt40BigEndian(data.AsSpan(offset + keyBytes, spanOffsetBytes));
            var archive = (int)(rawOffset >> archiveOffsetBits);
            var archiveOffset = (long)(rawOffset & ((1L << archiveOffsetBits) - 1));
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + keyBytes + spanOffsetBytes, spanSizeBytes));
            entries[prefix] = new CascSpan(archive, archiveOffset, size);
        }
    }

    private static int Align16(int value)
    {
        return (value + 15) & ~15;
    }

    private static long ReadUInt40BigEndian(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            value = (value << 8) | bytes[i];
        }

        return value;
    }
}
