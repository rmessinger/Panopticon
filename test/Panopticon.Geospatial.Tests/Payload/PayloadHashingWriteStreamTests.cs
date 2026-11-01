#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial.Payload.Storage;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

public class PayloadHashingWriteStreamTests
{
    private const int BlockBytes = 64 * 1024;

    [Fact]
    public void LargeSpanWrite_IsForwardedInBoundedBlocksWithTheActualHash()
    {
        var payload = Fill((3 * BlockBytes) - 24);
        using var target = new RecordingStream();

        using (var hashing = new PayloadHashingWriteStream(target))
        {
            hashing.Write(payload.AsSpan());

            Assert.Equal(payload.Length, hashing.WrittenBytes);
            Assert.Equal(Sha256Hex(payload), hashing.Sha256Hex());
        }

        Assert.Equal([BlockBytes, BlockBytes, (3 * BlockBytes) - 24 - (2 * BlockBytes)], target.WriteSizes);
        Assert.True(target.Written.AsSpan().SequenceEqual(payload));
    }

    [Fact]
    public void ArrayWrite_SplitsBoundedBlocksAndValidatesBounds()
    {
        var payload = Fill((2 * BlockBytes) + 5);
        using var target = new RecordingStream();

        using (var hashing = new PayloadHashingWriteStream(target))
        {
            hashing.Write(payload, 0, payload.Length);

            Assert.Equal(payload.Length, hashing.WrittenBytes);
            Assert.Equal(Sha256Hex(payload), hashing.Sha256Hex());
        }

        Assert.Equal([BlockBytes, BlockBytes, 5], target.WriteSizes);

        using (var hashing = new PayloadHashingWriteStream(target))
        {
            Assert.Throws<ArgumentNullException>(() => hashing.Write(null!, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => hashing.Write(payload, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => hashing.Write(payload, 0, -1));
            Assert.Throws<ArgumentException>(() => hashing.Write(payload, 3, payload.Length));
        }
    }

    [Fact]
    public void CancelledToken_StopsAtTheNextBlockBoundaryAndPreservesTheTarget()
    {
        var payload = Fill(5 * BlockBytes);
        using var source = new CancellationTokenSource();
        using var target = new RecordingStream(cancelsDuringWrite: source, cancelAtWriteCount: 2);

        using (var hashing = new PayloadHashingWriteStream(target, source.Token))
        {
            Assert.Throws<OperationCanceledException>(() => hashing.Write(payload));
        }

        Assert.Equal([BlockBytes, BlockBytes], target.WriteSizes);
        Assert.True(target.Written.AsSpan().SequenceEqual(payload.AsSpan(0, 2 * BlockBytes)));
    }

    [Fact]
    public void PreCancelledToken_ForwardsNoByte()
    {
        var payload = Fill(BlockBytes / 2);
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var target = new RecordingStream();

        using (var hashing = new PayloadHashingWriteStream(target, source.Token))
        {
            Assert.Throws<OperationCanceledException>(() => hashing.Write(payload));
            Assert.Equal(0, hashing.WrittenBytes);
        }

        Assert.Empty(target.WriteSizes);
        Assert.Empty(target.Written);
    }

    [Fact]
    public void DisposedWrapper_IsNotReusableAndDoubleDisposeIsHarmless()
    {
        using var target = new RecordingStream();
        var hashing = new PayloadHashingWriteStream(target);

        hashing.Write([(byte)1, (byte)2, (byte)3]);
        var sha256Hex = hashing.Sha256Hex();

        hashing.Dispose();
        hashing.Dispose();

        var afterDispose = new byte[] { 4 };
        Assert.Throws<ObjectDisposedException>(() => hashing.Write(afterDispose));
        Assert.Throws<ObjectDisposedException>(() => hashing.Write(afterDispose, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => hashing.Flush());
        Assert.Throws<ObjectDisposedException>(() => hashing.Sha256Hex());

        // The passive counter stays readable and the finalized hash is unaffected.
        Assert.Equal(3, hashing.WrittenBytes);
        Assert.Equal(Sha256Hex([1, 2, 3]), sha256Hex);
        Assert.Equal(sha256Hex, Sha256Hex([(byte)1, (byte)2, (byte)3]));
    }

    [Fact]
    public void Constructor_RejectsNullInnerAndNeverOwnsTheInnerStream()
    {
        Assert.Throws<ArgumentNullException>(() => new PayloadHashingWriteStream(null!));

        using var inner = new MemoryStream();

        using (var hashing = new PayloadHashingWriteStream(inner))
        {
            hashing.Write([9]);
        }

        // Disposing the wrapper frees only the native hash; the inner stream stays usable.
        inner.WriteByte(8);
        Assert.Equal(2, inner.Length);
    }

    private static byte[] Fill(int length)
    {
        var payload = new byte[length];

        for (var index = 0; index < length; index++)
        {
            payload[index] = (byte)(index * 31);
        }

        return payload;
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// CPU-only recording sink: captures the size of every underlying write, the exact
    /// bytes forwarded, and can cancel a token after a configured write count.
    /// </summary>
    private sealed class RecordingStream(CancellationTokenSource? cancelsDuringWrite = null, int cancelAtWriteCount = int.MaxValue) : Stream
    {
        private readonly CancellationTokenSource? _cancelsDuringWrite = cancelsDuringWrite;
        private readonly int _cancelAtWriteCount = cancelAtWriteCount;
        private readonly MemoryStream _written = new();

        internal List<int> WriteSizes { get; } = [];

        internal byte[] Written => _written.ToArray();

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
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteSizes.Add(buffer.Length);

            if (WriteSizes.Count == _cancelAtWriteCount)
            {
                _cancelsDuringWrite!.Cancel();
            }

            _written.Write(buffer);
        }
    }
}
