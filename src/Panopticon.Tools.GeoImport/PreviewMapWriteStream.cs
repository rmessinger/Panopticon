using System;
using System.IO;
using System.Threading;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// Seekable write pass-through for <c>MapFile.WriteTo</c>, which has no cancellation API:
/// writes forward in 64 KiB blocks with one cancellation check per block, and position,
/// length, and seek operations pass through so the map writer can rewind for its asset
/// table. The inner stream's lifetime stays with its owner.
/// </summary>
internal sealed class PreviewMapWriteStream : Stream
{
    private const int BlockBytes = 64 * 1024;

    private readonly Stream _inner;
    private readonly CancellationToken _cancellationToken;

    public PreviewMapWriteStream(Stream inner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _cancellationToken = cancellationToken;
    }

    public override bool CanRead => false;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => true;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The offset and count exceed the buffer length.");
        }

        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            // One cancellation check and one underlying write per bounded block.
            _cancellationToken.ThrowIfCancellationRequested();

            var block = buffer.Slice(offset, Math.Min(BlockBytes, buffer.Length - offset));
            _inner.Write(block);
            offset += block.Length;
        }
    }
}
