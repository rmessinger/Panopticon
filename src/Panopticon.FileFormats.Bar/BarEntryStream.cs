using System;
using System.IO;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// A bounded, read-only view of a stored (uncompressed) BAR entry. The shared
/// archive stream is not safe for concurrent use, so each access repositions
/// it under the archive lock, mirroring BigArchiveEntryStream.
/// </summary>
internal sealed class BarEntryStream : Stream
{
    private readonly BarArchive _archive;
    private readonly long _offset;
    private readonly uint _length;

    public BarEntryStream(BarArchive archive, BarEntry entry)
    {
        _archive = archive;
        _offset = (long)entry.Offset;
        _length = entry.DecompressedSize;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position { get; set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - Position;
        if (remaining <= 0)
        {
            return 0;
        }

        if (count > remaining)
        {
            count = (int)remaining;
        }

        int read;
        lock (_archive.LockObject)
        {
            _archive.Stream.Seek(_offset + Position, SeekOrigin.Begin);
            read = _archive.Stream.Read(buffer, offset, count);
        }

        Position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return origin switch
        {
            SeekOrigin.Begin => Position = offset,
            SeekOrigin.Current => Position += offset,
            SeekOrigin.End => Position = _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
        };
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
