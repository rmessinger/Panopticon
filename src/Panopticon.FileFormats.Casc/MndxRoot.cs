using System.Buffers.Binary;

namespace Panopticon.FileFormats.Casc;

public sealed class MndxRoot
{
    private const int ExpectedHeaderVersion = 2;
    private const int ExpectedFormatVersion = 2;
    private const int CKeyEntrySize = 24;

    private MndxRoot(IReadOnlyList<MndxRootEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<MndxRootEntry> Entries { get; }

    public static MndxRoot Parse(byte[] decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var data = decoded.AsSpan();
        if (data.Length < 48 || !data[..4].SequenceEqual("MNDX"u8))
        {
            throw new NotSupportedException("Root file is not an MNDX root.");
        }

        var headerVersion = BinaryPrimitives.ReadInt32LittleEndian(data[4..8]);
        var formatVersion = BinaryPrimitives.ReadInt32LittleEndian(data[8..12]);
        if (headerVersion != ExpectedHeaderVersion || formatVersion != ExpectedFormatVersion)
        {
            throw new NotSupportedException($"MNDX version {headerVersion}/{formatVersion} is not supported.");
        }

        var marInfoOffset = BinaryPrimitives.ReadInt32LittleEndian(data[20..24]);
        var marCount = BinaryPrimitives.ReadInt32LittleEndian(data[24..28]);
        var marInfoSize = BinaryPrimitives.ReadInt32LittleEndian(data[28..32]);
        var ckeyEntryOffset = BinaryPrimitives.ReadInt32LittleEndian(data[32..36]);
        var ckeyEntryCount = BinaryPrimitives.ReadInt32LittleEndian(data[36..40]);
        var fileNameCount = BinaryPrimitives.ReadInt32LittleEndian(data[40..44]);
        var ckeyEntrySize = BinaryPrimitives.ReadInt32LittleEndian(data[44..48]);
        if (marInfoSize != 20 || ckeyEntrySize != CKeyEntrySize || marCount < 2)
        {
            throw new InvalidDataException("MNDX root uses an unexpected table layout.");
        }

        var tries = LoadTries(decoded, marInfoOffset, marCount, marInfoSize);
        if (!tries.TryGetValue(0, out var packageTrie) || !tries.TryGetValue(1, out var nameTrie))
        {
            throw new InvalidDataException("MNDX root is missing package or name tries.");
        }

        if (nameTrie.Count != fileNameCount)
        {
            throw new InvalidDataException("MNDX root name count does not match the MAR trie.");
        }

        var packages = new string[packageTrie.Count];
        for (var i = 0; i < packages.Length; i++)
        {
            packages[i] = packageTrie.Restore(i);
        }

        var entriesByPath = new Dictionary<string, MndxRootEntry>(StringComparer.OrdinalIgnoreCase);
        var entryIndex = 0;
        for (var nameIndex = 0; nameIndex < fileNameCount; nameIndex++)
        {
            var name = nameTrie.Restore(nameIndex);
            while (entryIndex < ckeyEntryCount)
            {
                var entryOffset = ckeyEntryOffset + entryIndex * CKeyEntrySize;
                if (entryOffset + CKeyEntrySize > data.Length)
                {
                    throw new InvalidDataException("MNDX content-key table is truncated.");
                }

                var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[entryOffset..(entryOffset + 4)]);
                var packageIndex = checked((int)(flags & 0x00FFFFFF));
                if ((uint)packageIndex >= packages.Length)
                {
                    throw new InvalidDataException("MNDX content-key entry references an unknown package.");
                }

                var contentKey = new CascKey(data.Slice(entryOffset + 4, CascKey.Length));
                var size = BinaryPrimitives.ReadInt32LittleEndian(data[(entryOffset + 20)..(entryOffset + 24)]);
                AddEntry(entriesByPath, new MndxRootEntry(packages[packageIndex] + "/" + name, contentKey, size));
                entryIndex++;
                if ((flags & 0x80000000) != 0)
                {
                    break;
                }
            }
        }

        return new MndxRoot(entriesByPath.Values.ToArray());
    }

    private static Dictionary<int, MarTrie> LoadTries(byte[] decoded, int marInfoOffset, int marCount, int marInfoSize)
    {
        var tries = new Dictionary<int, MarTrie>();
        for (var i = 0; i < marCount; i++)
        {
            var offset = marInfoOffset + i * marInfoSize;
            if (offset + marInfoSize > decoded.Length)
            {
                throw new InvalidDataException("MNDX MAR info table is truncated.");
            }

            var info = decoded.AsSpan(offset, marInfoSize);
            var index = BinaryPrimitives.ReadInt32LittleEndian(info[..4]);
            var sizeLow = BinaryPrimitives.ReadUInt32LittleEndian(info[4..8]);
            var sizeHigh = BinaryPrimitives.ReadUInt32LittleEndian(info[8..12]);
            var dataOffsetLow = BinaryPrimitives.ReadUInt32LittleEndian(info[12..16]);
            var dataOffsetHigh = BinaryPrimitives.ReadUInt32LittleEndian(info[16..20]);
            var size = checked((long)sizeLow | ((long)sizeHigh << 32));
            var dataOffset = checked((long)dataOffsetLow | ((long)dataOffsetHigh << 32));
            if (dataOffset < 0 || size < 0 || dataOffset + size > decoded.Length)
            {
                throw new InvalidDataException("MNDX MAR data range is invalid.");
            }

            using var stream = new MemoryStream(decoded, checked((int)dataOffset), checked((int)size), writable: false);
            using var reader = new BinaryReader(stream);
            tries[index] = MarTrie.Load(reader, isTopLevel: true);
        }

        return tries;
    }

    private static void AddEntry(Dictionary<string, MndxRootEntry> entriesByPath, MndxRootEntry entry)
    {
        if (!entriesByPath.TryGetValue(entry.Path, out var existing))
        {
            entriesByPath.Add(entry.Path, entry);
            return;
        }

        if (existing.ContentKey != entry.ContentKey)
        {
            throw new InvalidDataException($"MNDX root contains duplicate path '{entry.Path}' with different content keys.");
        }
    }
}
