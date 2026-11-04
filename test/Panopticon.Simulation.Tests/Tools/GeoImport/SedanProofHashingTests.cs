using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// CPU tests of the constant-memory proof hasher over virtual streams and small temp files.
/// No proof root, IGN data, or game install is read.
/// </summary>
public sealed class SedanProofHashingTests
{
    private const long PastInt32Length = int.MaxValue + 64L;

    private const int PatternPeriod = 4093;

    private static readonly byte[] Pattern = BuildPattern(PatternPeriod);

    private readonly ITestOutputHelper _output;

    public SedanProofHashingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LargeLogicalStream_HashesMoreThanInt32MaxBytes()
    {
        var clock = Stopwatch.StartNew();
        var actual = SedanProofHashing.Sha256Hex(new PatternStream(PastInt32Length));
        var helperElapsed = clock.Elapsed;

        clock.Restart();
        var expected = ReferenceHashIncremental(new PatternStream(PastInt32Length));
        var referenceElapsed = clock.Elapsed;

        _output.WriteLine($"logical length {PastInt32Length} bytes; helper {helperElapsed.TotalSeconds:F2} s; incremental reference {referenceElapsed.TotalSeconds:F2} s");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LyingLengthStream_HashesActualBytesOnly()
    {
        var data = new byte[] { 0x73, 0x65, 0x64, 0x61, 0x6e, 0x2d, 0x30, 0x31, 0x32, 0x33 };
        using var stream = new ScriptedStream(data, reportedLength: 2_764_832_300L, maxReadBytes: int.MaxValue);

        Assert.Equal(Sha256Of(data), SedanProofHashing.Sha256Hex(stream));
    }

    [Fact]
    public void ShortReadStream_BoundedReads()
    {
        var data = new byte[10_000];

        for (var index = 0; index < data.Length; index++)
        {
            data[index] = (byte)(index * 31 + 7);
        }

        using var stream = new ScriptedStream(data, reportedLength: data.Length, maxReadBytes: 7);

        Assert.Equal(Sha256Of(data), SedanProofHashing.Sha256Hex(stream));
    }

    [Fact]
    public void RealSmallFile_KnownHash()
    {
        var data = SmallFileBytes();
        var path = WriteTempFile(data);

        try
        {
            Assert.Equal(Sha256Of(data), SedanProofHashing.Sha256FileHex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StreamAndPathOverloads_AgreeOnSameBytes()
    {
        var data = SmallFileBytes();
        var path = WriteTempFile(data);

        try
        {
            using var stream = new MemoryStream(data);

            Assert.Equal(SedanProofHashing.Sha256Hex(stream), SedanProofHashing.Sha256FileHex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] SmallFileBytes()
    {
        var prefix = Encoding.ASCII.GetBytes("sedan-proof-hash-check");
        var body = new byte[1024];

        for (var index = 0; index < body.Length; index++)
        {
            body[index] = (byte)(index * 13);
        }

        return Concat(prefix, body);
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var joined = new byte[first.Length + second.Length];
        first.CopyTo(joined, 0);
        second.CopyTo(joined, first.Length);
        return joined;
    }

    private static string WriteTempFile(byte[] data)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "sedan-hash-tests");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string Sha256Of(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string ReferenceHashIncremental(Stream source)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] BuildPattern(int period)
    {
        var bytes = new byte[period];
        var state = 0x2545F491u;

        for (var index = 0; index < period; index++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            bytes[index] = (byte)(state >> 24);
        }

        return bytes;
    }

    private sealed class PatternStream : Stream
    {
        private readonly long _length;
        private long _position;
        private int _phase;

        public PatternStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, _length - _position);

            for (var index = 0; index < count; index++)
            {
                buffer[index] = Pattern[_phase];
                _phase = _phase + 1 == Pattern.Length ? 0 : _phase + 1;
            }

            _position += count;
            return count;
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
            throw new NotSupportedException();
        }
    }

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private readonly long _reportedLength;
        private readonly int _maxReadBytes;
        private int _cursor;

        public ScriptedStream(byte[] data, long reportedLength, int maxReadBytes)
        {
            _data = data;
            _reportedLength = reportedLength;
            _maxReadBytes = maxReadBytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _reportedLength;

        public override long Position
        {
            get => _cursor;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, _maxReadBytes), _data.Length - _cursor);
            _data.AsSpan(_cursor, count).CopyTo(buffer);
            _cursor += count;
            return count;
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
            throw new NotSupportedException();
        }
    }
}
