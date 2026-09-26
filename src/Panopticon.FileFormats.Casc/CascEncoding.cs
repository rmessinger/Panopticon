using System.Buffers.Binary;

namespace Panopticon.FileFormats.Casc;

public sealed class CascEncoding
{
    private const int ExpectedVersion = 1;
    private const int ExpectedKeySize = 16;

    private readonly Dictionary<CascKey, CascEncodingEntry> _entries;

    private CascEncoding(Dictionary<CascKey, CascEncodingEntry> entries)
    {
        _entries = entries;
    }

    public int Count => _entries.Count;

    public static CascEncoding Parse(byte[] decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        var data = decoded.AsSpan();
        if (data.Length < 22 || !data[..2].SequenceEqual("EN"u8))
        {
            throw new InvalidDataException("CASC encoding file has an invalid magic.");
        }

        var version = data[2];
        var contentKeySize = data[3];
        var encodingKeySize = data[4];
        if (version != ExpectedVersion ||
            contentKeySize != ExpectedKeySize ||
            encodingKeySize != ExpectedKeySize)
        {
            throw new NotSupportedException(
                $"CASC encoding layout version {version} with key sizes {contentKeySize}/{encodingKeySize} is not supported.");
        }

        var contentPageSize = BinaryPrimitives.ReadUInt16BigEndian(data[5..7]) * 1024;
        var contentPageCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[9..13]));
        var especSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[18..22]));
        var offset = 22 + especSize + contentPageCount * 32;
        if (offset > data.Length)
        {
            throw new InvalidDataException("CASC encoding file has a truncated page table.");
        }

        var entries = new Dictionary<CascKey, CascEncodingEntry>();
        for (var pageIndex = 0; pageIndex < contentPageCount; pageIndex++)
        {
            if (offset + contentPageSize > data.Length)
            {
                throw new InvalidDataException("CASC encoding file has a truncated content page.");
            }

            ReadContentPage(data.Slice(offset, contentPageSize), entries);
            offset += contentPageSize;
        }

        return new CascEncoding(entries);
    }

    public bool TryGetEntry(CascKey contentKey, out CascEncodingEntry entry)
    {
        return _entries.TryGetValue(contentKey, out entry);
    }

    private static void ReadContentPage(ReadOnlySpan<byte> page, Dictionary<CascKey, CascEncodingEntry> entries)
    {
        var offset = 0;
        while (offset < page.Length)
        {
            var keyCount = page[offset];
            if (keyCount == 0)
            {
                return;
            }

            var entrySize = 1 + 5 + CascKey.Length + keyCount * CascKey.Length;
            if (offset + entrySize > page.Length)
            {
                throw new InvalidDataException("CASC encoding content page entry is truncated.");
            }

            var size = ReadUInt40BigEndian(page.Slice(offset + 1, 5));
            var contentKey = new CascKey(page.Slice(offset + 6, CascKey.Length));
            var encodingKey = new CascKey(page.Slice(offset + 6 + CascKey.Length, CascKey.Length));
            entries[contentKey] = new CascEncodingEntry(encodingKey, size);
            offset += entrySize;
        }
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
