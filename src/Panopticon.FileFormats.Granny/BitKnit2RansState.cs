// BitKnit2 interleaved rANS decoder state. Original C# written from the
// libbg3 written specification (github.com/eiz/libbg3@23c701a, MIT);
// cross-checked against pybg3 rans.h (github.com/eiz/pybg3@3b02e59, MIT,
// Copyright 2024 Mackenzie Straight). No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// One 32-bit rANS decoder state (L = 2^16, 16-bit renormalization words)
/// consuming the shared word stream.
/// </summary>
internal struct BitKnit2RansState
{
    public const uint RefillThreshold = 1u << 16;

    public uint Bits;

    public BitKnit2RansState(uint bits)
    {
        Bits = bits;
    }

    /// <summary>Decodes nbits raw bits; the caller renormalizes with the stream.</summary>
    public uint PopBits(ref BitKnit2WordStream stream, int bitCount)
    {
        if (bitCount < 0 || bitCount > 31)
        {
            throw new InvalidDataException(
                $"BitKnit2 requested {bitCount} raw bits, outside the supported 0..31 range.");
        }

        var value = Bits & ((1u << bitCount) - 1);
        Bits >>= bitCount;
        MaybeRefill(ref stream);
        return value;
    }

    /// <summary>Decodes one symbol through the frequency table.</summary>
    public uint PopCdf(ref BitKnit2WordStream stream, BitKnit2FrequencyTable table)
    {
        var code = Bits & ((1u << table.FrequencyBits) - 1);
        var symbol = table.FindSymbol(code);
        var frequency = (uint)table.Frequency(symbol);
        Bits = (Bits >> table.FrequencyBits) * frequency + code - (uint)table.SumBelow(symbol);
        MaybeRefill(ref stream);
        return (uint)symbol;
    }

    public void MaybeRefill(ref BitKnit2WordStream stream)
    {
        if (Bits < RefillThreshold)
        {
            Bits = (Bits << 16) | stream.Pop();
        }
    }
}
