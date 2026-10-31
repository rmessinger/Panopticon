// Oodle1 arithmetic bitstream layer. Original C# written from the liboodle
// written specification (github.com/LunaticInAHat/liboodle@7837c25, Unlicense);
// cross-checked against blendergranny oodle1.py (MIT, ciupix 2026). No code
// copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Bitstream layer of the Oodle1 codec: a shift register, a shift register
/// modulus, a pending least-significant bit held out of the register, and the
/// read position into the compressed bytes. Reads past the input read as zero
/// only up to the codec's padding margin (the input length rounded up to a
/// multiple of four, plus four lookahead bytes); anything beyond that is a
/// truncated payload.
/// </summary>
internal ref struct Oodle1Bitstream
{
    private const uint IngestLimit = 0x800000;
    private const int ZeroPadHeadroom = 4;

    private readonly ReadOnlySpan<byte> _input;
    private readonly int _readLimit;
    private uint _register;
    private uint _modulus;
    private int _pendingLowBit;
    private int _position;

    public Oodle1Bitstream(ReadOnlySpan<byte> input)
    {
        _input = input;
        _readLimit = (input.Length + 3) & ~3;
        _readLimit += ZeroPadHeadroom;
        _register = 0;
        _modulus = 0x80;
        _pendingLowBit = 0;
        _position = 0;
        if (!input.IsEmpty)
        {
            _register = (uint)(input[0] >> 1);
            _pendingLowBit = input[0] & 1;
            _position = 1;
        }
    }

    /// <summary>Peeks the register projected onto [0, scale) without consuming.</summary>
    public uint Peek(uint scale, out uint unit)
    {
        Ingest();
        unit = Unit(scale);
        return Math.Min(_register / unit, scale - 1);
    }

    /// <summary>Consumes the [offset, offset + span) part of the [0, scale) interval.</summary>
    public void Consume(uint offset, uint span, uint scale)
    {
        var unit = Unit(scale);
        var scaled = offset * unit;
        _register -= scaled;
        if (offset < scale - span)
        {
            _modulus = span * unit;
        }
        else
        {
            _modulus -= scaled;
        }
    }

    /// <summary>Decodes one raw value in [0, scale).</summary>
    public uint Get(uint scale)
    {
        Ingest();
        var unit = Unit(scale);
        var value = Math.Min(_register / unit, scale - 1);
        var scaled = value * unit;
        _register -= scaled;
        if (value < scale - 1)
        {
            _modulus = unit;
        }
        else
        {
            _modulus -= scaled;
        }

        return value;
    }

    private uint Unit(uint scale)
    {
        if (scale < 1 || scale > _modulus)
        {
            throw new InvalidDataException(
                $"Oodle1 arithmetic scale {scale} is outside the current modulus {_modulus}.");
        }

        return _modulus / scale;
    }

    private void Ingest()
    {
        while (_modulus <= IngestLimit)
        {
            var word = ReadByte();
            _register = (_register << 1) | (uint)_pendingLowBit;
            _register = (_register << 7) | (uint)(word >> 1);
            _pendingLowBit = word & 1;
            _modulus <<= 8;
        }
    }

    private byte ReadByte()
    {
        if (_position >= _readLimit)
        {
            throw new InvalidDataException(
                "Oodle1 payload is truncated: the decoder read past its zero-padding margin.");
        }

        var value = _position < _input.Length ? _input[_position] : (byte)0;
        _position++;
        return value;
    }
}
