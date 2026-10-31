// Oodle1 LZ layer. Original C# written from the liboodle written
// specification (github.com/LunaticInAHat/liboodle@7837c25, Unlicense);
// parameterization cross-checked against blendergranny oodle1.py (MIT,
// ciupix 2026). No code copied; see CODEC-NOTICES.md.
using System;
using System.Buffers.Binary;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// LZ layer of the Oodle1 codec for one stop-delimited stream: four literal
/// coders selected by output position modulo 4, 65 repeat-length coders
/// selected by the previous length code, and a three-field repeat offset
/// (1K, 4-byte, and 1-byte components). The effective window is bounded by the
/// bytes this stream has produced; one instance decodes one stream.
/// </summary>
internal sealed class Oodle1LzDictionary
{
    private static readonly int[] LongRepeatLengths = [128, 192, 256, 512];

    /// <summary>Spec hard cap on the LZ window; the corpus only ever declares 0x3FFFF.</summary>
    private const uint MaxWindowSize = 0x40000;

    private readonly Oodle1SymbolCoder[] _literalCoders = new Oodle1SymbolCoder[4];
    private readonly Oodle1SymbolCoder[] _lengthCoders = new Oodle1SymbolCoder[65];
    private readonly Oodle1SymbolCoder _lowOffsetCoder;
    private readonly Oodle1SymbolCoder[] _midOffsetCoders;
    private readonly Oodle1SymbolCoder _highOffsetCoder;
    private readonly uint _windowSize;
    private readonly uint _literalAlphabet;
    private readonly uint _lowOffsetAlphabet;
    private int _producedBytes;
    private int _lastLengthCode;

    public Oodle1LzDictionary(ReadOnlySpan<byte> header)
    {
        var word0 = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var word1 = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var word2 = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        _windowSize = word0 >> 9;
        _literalAlphabet = word0 & 0x1FF;
        if (_literalAlphabet < 1)
        {
            throw new InvalidDataException("Oodle1 stream declares an empty literal alphabet.");
        }

        if (_windowSize > MaxWindowSize)
        {
            throw new InvalidDataException(
                $"Oodle1 stream declares a {MaxWindowSize:#x}-exceeding window of {_windowSize} bytes.");
        }

        var uniqueLiterals = word1 & 0x1FF;
        var largestHighOffset = word1 >> 19;
        for (var i = 0; i < 4; i++)
        {
            _literalCoders[i] = new Oodle1SymbolCoder((int)_literalAlphabet, (int)uniqueLiterals);
        }

        for (var group = 0; group < 4; group++)
        {
            var uniqueLengths = (int)((word2 >> ((3 - group) * 8)) & 0xFF);
            for (var entry = 0; entry < 16; entry++)
            {
                _lengthCoders[group * 16 + entry] = new Oodle1SymbolCoder(65, uniqueLengths);
            }
        }

        _lengthCoders[64] = new Oodle1SymbolCoder(65, (int)(word2 & 0xFF));

        _lowOffsetAlphabet = Math.Min(4, _windowSize + 1);
        _lowOffsetCoder = new Oodle1SymbolCoder((int)_lowOffsetAlphabet, (int)_lowOffsetAlphabet);
        var midOffsetAlphabet = Math.Min(256, (_windowSize >> 2) + 1);
        var highOffsetAlphabet = (int)((_windowSize >> 10) + 1);
        _midOffsetCoders = new Oodle1SymbolCoder[highOffsetAlphabet];
        for (var i = 0; i < highOffsetAlphabet; i++)
        {
            _midOffsetCoders[i] = new Oodle1SymbolCoder((int)midOffsetAlphabet, (int)midOffsetAlphabet);
        }

        _highOffsetCoder = new Oodle1SymbolCoder(highOffsetAlphabet, (int)largestHighOffset + 1);
    }

    /// <summary>Decodes literals and repeats until the stop offset or the end of the output.</summary>
    public void DecodeToStop(ref Oodle1Bitstream stream, Span<byte> output, ref int position, int stop)
    {
        while (position < stop && position < output.Length)
        {
            var lengthCode = _lengthCoders[Math.Min(_lastLengthCode, 64)].Decode(65, ref stream);
            _lastLengthCode = lengthCode;
            if (lengthCode == 0)
            {
                var literal = _literalCoders[position & 3].Decode(_literalAlphabet, ref stream);
                if (literal > byte.MaxValue)
                {
                    throw new InvalidDataException(
                        $"Oodle1 literal symbol {literal} exceeds the byte alphabet.");
                }

                output[position++] = (byte)literal;
                _producedBytes++;
                continue;
            }

            var length = lengthCode < 61 ? lengthCode + 1 : LongRepeatLengths[lengthCode - 61];
            var window = (int)Math.Min(_windowSize, (uint)_producedBytes);
            var low = _lowOffsetCoder.Decode(_lowOffsetAlphabet, ref stream);
            var high = _highOffsetCoder.Decode((uint)((window >> 10) + 1), ref stream);
            var mid = _midOffsetCoders[high].Decode((uint)Math.Min(256, (window >> 2) + 1), ref stream);
            var distance = low + 1 + (mid << 2) + (high << 10);
            if (distance > _producedBytes)
            {
                throw new InvalidDataException(
                    $"Oodle1 repeat offset {distance} reaches before the {_producedBytes}-byte stream start.");
            }

            if (length > output.Length - position)
            {
                throw new InvalidDataException(
                    $"Oodle1 repeat length {length} overflows the {output.Length - position} remaining section bytes.");
            }

            // A repeat may cross this stream's stop into the next stream's
            // output region; both reference decoders allow that, and the next
            // dictionary skips itself when its stop is already passed.
            while (length > 0)
            {
                output[position] = output[position - distance];
                position++;
                _producedBytes++;
                length--;
            }
        }
    }
}
