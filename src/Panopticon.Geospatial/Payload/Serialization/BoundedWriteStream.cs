using System;
using System.IO;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>
/// Forward-only write wrapper that rejects a write before emitting any byte past the
/// configured limit, so a destination stream never receives oversized output.
/// </summary>
internal sealed class BoundedWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private long _written;

    public BoundedWriteStream(Stream inner, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _inner = inner;
        _maxBytes = maxBytes;
    }

    /// <summary>Total bytes passed through to the inner stream.</summary>
    public long WrittenBytes => _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Invariant: _written <= _maxBytes, so the subtraction never overflows for any
        // valid limit, including long.MaxValue.
        if (buffer.Length > _maxBytes - _written)
        {
            throw new PayloadJsonLimitException(
                $"Writing the JSON document would exceed the maxPayloadMetadataBytes limit of {_maxBytes} bytes.");
        }

        _inner.Write(buffer);
        _written += buffer.Length;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
}
