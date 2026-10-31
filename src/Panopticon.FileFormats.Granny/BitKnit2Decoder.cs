// BitKnit2 (Granny GR2 section compression type 4) decoder. Original C#
// written from the libbg3 written specification
// (github.com/eiz/libbg3@23c701a943a189a789163af40b0398e290d2681a, MIT);
// cross-checked against pybg3 rans.h (github.com/eiz/pybg3@3b02e59, MIT).
// No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Decodes one BitKnit2-compressed GR2 section body: a 0x75B1 marker word
/// followed by 64 KiB quantums of interleaved rANS LZ data, ending exactly at
/// the declared output size.
/// </summary>
public static class BitKnit2Decoder
{
    private const ushort Magic = 0x75B1;

    /// <summary>Decodes a BitKnit2 section payload to exactly <paramref name="outputSize"/> bytes.</summary>
    /// <param name="input">Compressed section body, starting at the 0x75B1 marker word.</param>
    /// <param name="outputSize">Declared uncompressed size of the section.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a negative size.</exception>
    /// <exception cref="InvalidDataException">Thrown for a corrupt or truncated payload.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> input, int outputSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputSize);
        var output = new byte[outputSize];
        if (outputSize == 0)
        {
            return output;
        }

        if (input.Length < sizeof(ushort))
        {
            throw new InvalidDataException("BitKnit2 payload is too short to hold its marker word.");
        }

        var stream = new BitKnit2WordStream(input);
        if (stream.Pop() != Magic)
        {
            throw new InvalidDataException("BitKnit2 payload does not start with the 0x75B1 marker word.");
        }

        new BitKnit2Decompressor(output).Run(ref stream);
        return output;
    }
}
