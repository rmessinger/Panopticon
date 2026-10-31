// BitKnit2 word stream. Original C# written from the libbg3 written
// specification (github.com/eiz/libbg3@23c701a, MIT); cross-checked against
// pybg3 rans.h (github.com/eiz/pybg3@3b02e59, MIT, Copyright 2024 Mackenzie
// Straight). No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Forward-reading view of the BitKnit2 payload as little-endian 16-bit
/// words, with raw-byte access for uncompressed quantum copies.
/// </summary>
internal ref struct BitKnit2WordStream
{
    private readonly ReadOnlySpan<ushort> _words;
    private int _position;

    public BitKnit2WordStream(ReadOnlySpan<byte> input)
    {
        _words = MemoryMarshal.Cast<byte, ushort>(input);
        _position = 0;
    }

    public int RemainingWords => _words.Length - _position;

    public ushort Peek()
    {
        if (_position >= _words.Length)
        {
            throw new InvalidDataException("BitKnit2 stream ended before the output was complete.");
        }

        return _words[_position];
    }

    public ushort Pop()
    {
        if (_position >= _words.Length)
        {
            throw new InvalidDataException("BitKnit2 stream ended before the output was complete.");
        }

        return _words[_position++];
    }

    /// <summary>The bytes of the unread words, for raw quantum copies.</summary>
    public ReadOnlySpan<byte> RemainingBytes => MemoryMarshal.AsBytes(_words[_position..]);

    public void AdvanceBytes(int byteCount)
    {
        _position += byteCount / 2;
    }
}
