// BitKnit2 recent-offset cache. Original C# written from the libbg3 written
// specification (github.com/eiz/libbg3@23c701a, MIT) and the public
// register-swizzle design description; cross-checked against pybg3 rans.h
// (github.com/eiz/pybg3@3b02e59, MIT, Copyright 2024 Mackenzie Straight).
// No code copied; see CODEC-NOTICES.md.
using System;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Eight-entry LRU of recent copy offsets. The entries never move; the
/// logical order lives in one nibble per slot of a 32-bit register, and hits
/// rotate the affected low nibbles so a slot id moves to the front.
/// </summary>
internal sealed class BitKnit2OffsetCache
{
    private readonly uint[] _entries = new uint[8];
    private uint _entryOrder = 0x76543210;

    public BitKnit2OffsetCache()
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            _entries[i] = 1;
        }
    }

    /// <summary>Inserts the offset in the second-to-last position of the logical order.</summary>
    public void Insert(uint value)
    {
        _entries[_entryOrder >> 28] = _entries[(_entryOrder >> 24) & 15];
        _entries[(_entryOrder >> 24) & 15] = value;
    }

    /// <summary>Returns the offset at the logical index and moves it to the front.</summary>
    public uint Hit(int index)
    {
        var slot = (int)((_entryOrder >> (index * 4)) & 15);
        var rotateMask = (16u << (index * 4)) - 1;
        var rotated = ((_entryOrder << 4) | (uint)slot) & rotateMask;
        _entryOrder = (_entryOrder & ~rotateMask) | rotated;
        return _entries[slot];
    }
}
