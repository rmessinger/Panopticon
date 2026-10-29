using System;
using System.IO;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// Decoder for the raw LZ4 block format (no frame, no dictionary), as used
/// inside AoE3 DE alz4 payloads.
/// </summary>
public static class Lz4BlockDecoder
{
    /// <summary>
    /// Decodes an LZ4 block into destination and returns the number of bytes written.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var sourceIndex = 0;
        var destinationIndex = 0;

        while (sourceIndex < source.Length)
        {
            var token = source[sourceIndex++];
            var literalLength = token >> 4;
            if (literalLength == 15)
            {
                literalLength += ReadExtendedLength(source, ref sourceIndex);
            }

            if (sourceIndex + literalLength > source.Length
                || destinationIndex + literalLength > destination.Length)
            {
                throw new InvalidDataException("LZ4 literals exceed the source or destination bounds.");
            }

            source.Slice(sourceIndex, literalLength).CopyTo(destination.Slice(destinationIndex));
            sourceIndex += literalLength;
            destinationIndex += literalLength;

            // The last sequence contains literals only.
            if (sourceIndex == source.Length)
            {
                break;
            }

            if (sourceIndex + 2 > source.Length)
            {
                throw new InvalidDataException("LZ4 block ends before the match offset.");
            }

            var matchOffset = source[sourceIndex] | (source[sourceIndex + 1] << 8);
            sourceIndex += 2;

            var matchLength = token & 0x0F;
            if (matchLength == 15)
            {
                matchLength += ReadExtendedLength(source, ref sourceIndex);
            }

            matchLength += 4;

            if (matchOffset == 0 || matchOffset > destinationIndex)
            {
                throw new InvalidDataException(
                    $"Invalid LZ4 match offset {matchOffset} at output position {destinationIndex}.");
            }

            if (destinationIndex + matchLength > destination.Length)
            {
                throw new InvalidDataException("LZ4 match exceeds the destination bounds.");
            }

            // Byte-by-byte copy: a match may overlap the bytes it produces.
            var matchIndex = destinationIndex - matchOffset;
            for (var i = 0; i < matchLength; i++)
            {
                destination[destinationIndex + i] = destination[matchIndex + i];
            }

            destinationIndex += matchLength;
        }

        return destinationIndex;
    }

    private static int ReadExtendedLength(ReadOnlySpan<byte> source, ref int sourceIndex)
    {
        var length = 0;
        byte extra;
        do
        {
            if (sourceIndex >= source.Length)
            {
                throw new InvalidDataException("LZ4 block ends inside an extended length.");
            }

            extra = source[sourceIndex++];
            length += extra;
        }
        while (extra == 255);

        return length;
    }
}
