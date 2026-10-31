// Oodle1 (Granny GR2 section compression type 2) decoder. Original C#
// written from the liboodle written specification (github.com/LunaticInAHat/
// liboodle@7837c253f8ca505a3ba3cd8e35d8bc3f5ab76a30, Unlicense), which also
// documents the Granny three-stream section wrapper; cross-checked against
// blendergranny oodle1.py (github.com/Rasetsuu/blendergranny@aec91bb, MIT).
// No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Decodes one Oodle1-compressed GR2 section body: three 12-byte stream
/// parameter headers (36 bytes total) followed by one shared arithmetic
/// stream. Each stream decodes until its stop offset in the section output;
/// the stops are the section table's first-16-bit and first-8-bit element
/// offsets followed by the uncompressed size.
/// </summary>
public static class Oodle1Decoder
{
    private const int StreamHeaderSize = 12;
    private const int StreamCount = 3;

    /// <summary>Decodes an Oodle1 section payload to exactly <paramref name="outputSize"/> bytes.</summary>
    /// <param name="input">Compressed section body, starting at the three stream headers.</param>
    /// <param name="outputSize">Declared uncompressed size of the section.</param>
    /// <param name="first16Bit">
    /// Section table stop: end of the first stream (the "first 16-bit element" field).
    /// </param>
    /// <param name="first8Bit">
    /// Section table stop: end of the second stream (the "first 8-bit element" field).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a negative size or stops that are out of order or beyond the output size.
    /// </exception>
    /// <exception cref="InvalidDataException">Thrown for a corrupt or truncated payload.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> input, int outputSize, uint first16Bit, uint first8Bit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputSize);
        if (first16Bit > first8Bit || first8Bit > (uint)outputSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(first16Bit),
                $"Oodle1 stops ({first16Bit}, {first8Bit}) must satisfy 0 <= first16Bit <= first8Bit <= {outputSize}.");
        }

        var output = new byte[outputSize];
        if (outputSize == 0)
        {
            return output;
        }

        var headerBytes = StreamCount * StreamHeaderSize;
        if (input.Length < headerBytes)
        {
            throw new InvalidDataException(
                $"Oodle1 payload is {input.Length} bytes; at least {headerBytes} stream header bytes are required.");
        }

        var stream = new Oodle1Bitstream(input[headerBytes..]);
        var position = 0;
        for (var index = 0; index < StreamCount; index++)
        {
            var stop = index switch
            {
                0 => first16Bit,
                1 => first8Bit,
                _ => (uint)outputSize,
            };

            if (position >= (int)stop)
            {
                continue;
            }

            var dictionary = new Oodle1LzDictionary(input.Slice(index * StreamHeaderSize, StreamHeaderSize));
            dictionary.DecodeToStop(ref stream, output, ref position, (int)stop);
        }

        if (position < outputSize)
        {
            throw new InvalidDataException(
                $"Oodle1 streams produced {position} bytes; the section declares {outputSize}.");
        }

        return output;
    }
}
