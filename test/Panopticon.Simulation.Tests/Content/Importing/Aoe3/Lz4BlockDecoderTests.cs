using System;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.FileFormats.Bar;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class Lz4BlockDecoderTests
{
    [Fact]
    public void DecodesEmptyBlock()
    {
        var written = Lz4BlockDecoder.Decode(new byte[] { 0x00 }, Span<byte>.Empty);

        Assert.Equal(0, written);
    }

    [Fact]
    public void DecodesLiteralsOnlyBlock()
    {
        var source = new byte[] { 0x40, (byte)'A', (byte)'B', (byte)'C', (byte)'D' };
        var destination = new byte[4];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(4, written);
        Assert.Equal("ABCD", Encoding.ASCII.GetString(destination));
    }

    [Fact]
    public void DecodesNonOverlappingMatch()
    {
        // Four literals 'abcd', then a length-4 match at offset 4: "abcdabcd".
        var source = new byte[] { 0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x04, 0x00 };
        var destination = new byte[8];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(8, written);
        Assert.Equal("abcdabcd", Encoding.ASCII.GetString(destination));
    }

    [Fact]
    public void DecodesOverlappingMatch()
    {
        // Two literals 'ab', then a length-6 match at offset 1: 'a' + seven 'b'.
        var source = new byte[] { 0x22, (byte)'a', (byte)'b', 0x01, 0x00 };
        var destination = new byte[8];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(8, written);
        Assert.Equal("a" + new string('b', 7), Encoding.ASCII.GetString(destination));
    }

    [Fact]
    public void DecodesLongLiteralsWithExtendedLength()
    {
        var payload = Encoding.ASCII.GetBytes(new string('x', 300));
        var source = new byte[3 + payload.Length];
        source[0] = 0xF0; // 15 literals, extended
        source[1] = 0xFF; // + 255
        source[2] = 0x1E; // + 30, total 300
        payload.CopyTo(source, 3);

        var destination = new byte[payload.Length];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(payload.Length, written);
        Assert.Equal(payload, destination);
    }

    [Fact]
    public void DecodesLongMatchWithExtendedLength()
    {
        // Two literals 'xy', then a length-272 match at offset 2: "xy" repeated.
        var source = new byte[] { 0x2F, (byte)'x', (byte)'y', 0x02, 0x00, 0xFD };
        var destination = new byte[274];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(274, written);
        Assert.Equal(string.Concat(Enumerable.Repeat("xy", 137)), Encoding.ASCII.GetString(destination));
    }

    [Fact]
    public void DecodesSequenceOfBlocks()
    {
        // Match sequence "abcdabcd", then a literals-only sequence 'e' 'f'.
        var source = new byte[]
        {
            0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x04, 0x00,
            0x20, (byte)'e', (byte)'f',
        };
        var destination = new byte[10];

        var written = Lz4BlockDecoder.Decode(source, destination);

        Assert.Equal(10, written);
        Assert.Equal("abcdabcdef", Encoding.ASCII.GetString(destination));
    }

    [Fact]
    public void ThrowsWhenLiteralsExceedSource()
    {
        var source = new byte[] { 0x50, (byte)'a', (byte)'b', (byte)'c' };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[5]));
    }

    [Fact]
    public void ThrowsWhenLiteralsExceedDestination()
    {
        var source = new byte[] { 0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d' };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[3]));
    }

    [Fact]
    public void ThrowsOnZeroMatchOffset()
    {
        var source = new byte[] { 0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x00, 0x00 };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[8]));
    }

    [Fact]
    public void ThrowsWhenMatchOffsetExceedsProducedOutput()
    {
        var source = new byte[] { 0x10, (byte)'a', 0x05, 0x00 };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[8]));
    }

    [Fact]
    public void ThrowsWhenMatchExceedsDestination()
    {
        var source = new byte[] { 0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x04, 0x00 };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[4]));
    }

    [Fact]
    public void ThrowsWhenBlockEndsInsideExtendedLength()
    {
        var source = new byte[] { 0xF0, 0xFF };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[300]));
    }

    [Fact]
    public void ThrowsWhenBlockEndsBeforeMatchOffset()
    {
        var source = new byte[] { 0x40, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x04 };

        Assert.Throws<InvalidDataException>(() => Lz4BlockDecoder.Decode(source, new byte[8]));
    }
}
