using System;
using System.IO;

namespace Panopticon.FileFormats.Bar;

public sealed class BarEntry
{
    internal BarEntry(
        BarArchive archive,
        string fullName,
        ulong offset,
        uint decompressedSize,
        uint storedSize,
        uint paddedSize,
        uint compressionFlag)
    {
        Archive = archive;
        FullName = fullName;
        Offset = offset;
        DecompressedSize = decompressedSize;
        StoredSize = storedSize;
        PaddedSize = paddedSize;
        CompressionFlag = compressionFlag;
    }

    public BarArchive Archive { get; }

    public string FullName { get; }

    public ulong Offset { get; }

    public uint DecompressedSize { get; }

    public uint StoredSize { get; }

    public uint PaddedSize { get; }

    public uint CompressionFlag { get; }

    public BarCompression Compression => (BarCompression)CompressionFlag;

    public override string ToString() => FullName;

    public Stream Open()
    {
        switch (CompressionFlag)
        {
            case (uint)BarCompression.None:
                return new BarEntryStream(Archive, this);

            case (uint)BarCompression.Alz4:
                return OpenAlz4();

            default:
                throw new NotSupportedException(
                    $"BAR entry '{FullName}' uses unsupported compression flag {CompressionFlag}.");
        }
    }

    private MemoryStream OpenAlz4()
    {
        var payload = new byte[StoredSize];
        lock (Archive.LockObject)
        {
            Archive.Stream.Seek((long)Offset, SeekOrigin.Begin);
            Archive.Stream.ReadExactly(payload, 0, payload.Length);
        }

        var decompressed = Alz4.Decompress(payload);
        if (decompressed.Length != DecompressedSize)
        {
            throw new InvalidDataException(
                $"BAR entry '{FullName}' decompressed to {decompressed.Length} bytes; header records {DecompressedSize}.");
        }

        return new MemoryStream(decompressed, writable: false);
    }
}
