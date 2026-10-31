// BitKnit2 frequency table (prefix-sum CDF with reverse lookup). Original C#
// written from the libbg3 written specification
// (github.com/eiz/libbg3@23c701a, MIT); cross-checked against pybg3 rans.h
// (github.com/eiz/pybg3@3b02e59, MIT, Copyright 2024 Mackenzie Straight).
// No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Quantized probability distribution for one BitKnit2 symbol vocabulary,
/// stored as prefix sums over a 2^15 scale, plus a lookup table that maps the
/// high bits of a code to the start of a short linear search.
/// </summary>
internal sealed class BitKnit2FrequencyTable
{
    private readonly ushort[] _sums;
    private readonly ushort[] _lookup;

    public BitKnit2FrequencyTable(int frequencyBits, int vocabSize, int lookupBits)
    {
        FrequencyBits = frequencyBits;
        VocabSize = vocabSize;
        LookupShift = frequencyBits - lookupBits;
        _sums = new ushort[vocabSize + 1];
        _lookup = new ushort[1 << lookupBits];
    }

    public int FrequencyBits { get; }

    public int VocabSize { get; }

    private int LookupShift { get; }

    internal Span<ushort> Sums => _sums;

    /// <summary>Finds the symbol whose cumulative range contains the code.</summary>
    public int FindSymbol(uint code)
    {
        var symbol = _lookup[code >> LookupShift];
        while (code >= _sums[symbol + 1])
        {
            symbol++;
            if (symbol >= VocabSize)
            {
                throw new InvalidDataException(
                    "BitKnit2 frequency table is exhausted before the decoded code range ended.");
            }
        }

        return symbol;
    }

    public int Frequency(int symbol)
    {
        return _sums[symbol + 1] - _sums[symbol];
    }

    public int SumBelow(int symbol)
    {
        return _sums[symbol];
    }

    /// <summary>Rebuilds the reverse lookup table after the sums change.</summary>
    public void FinishUpdate()
    {
        var code = 0;
        var symbol = 0;
        var next = _sums[1];
        var limit = 1 << FrequencyBits;
        var step = 1 << LookupShift;
        while (code < limit)
        {
            if (code < next)
            {
                _lookup[code >> LookupShift] = (ushort)symbol;
                code += step;
            }
            else
            {
                symbol++;
                next = _sums[symbol + 1];
            }
        }
    }
}
