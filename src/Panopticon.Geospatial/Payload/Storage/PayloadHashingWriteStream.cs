using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Write-only pass-through that forwards, SHA-256 hashes, and counts bytes in bounded
/// 64 KiB blocks with a cancellation check per block. Owns only its IncrementalHash: the
/// inner stream's lifetime stays with its owner, and the hash is finalized before disposal.
/// </summary>
internal sealed class PayloadHashingWriteStream : Stream
{
    private const int BlockBytes = 64 * 1024;

    private readonly Stream _inner;
    private readonly CancellationToken _cancellationToken;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _disposed;

    public PayloadHashingWriteStream(Stream inner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _cancellationToken = cancellationToken;
    }

    public long WrittenBytes { get; private set; }

    /// <summary>SHA-256 of all bytes written so far as 64 lowercase hex characters; consumes the accumulated hash state.</summary>
    public string Sha256Hex()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        _inner.Flush();
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        var offset = 0;

        while (offset < buffer.Length)
        {
            // One cancellation check and one underlying IO per bounded block.
            _cancellationToken.ThrowIfCancellationRequested();

            var block = buffer.Slice(offset, Math.Min(BlockBytes, buffer.Length - offset));
            _inner.Write(block);
            _hash.AppendData(block);
            WrittenBytes += block.Length;
            offset += block.Length;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
