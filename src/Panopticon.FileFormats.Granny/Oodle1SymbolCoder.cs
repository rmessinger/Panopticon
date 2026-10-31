// Oodle1 adaptive multi-symbol coder. Original C# written from the liboodle
// written specification (github.com/LunaticInAHat/liboodle@7837c25, Unlicense);
// adaptation cadence cross-checked against blendergranny oodle1.py (MIT,
// ciupix 2026). No code copied; see CODEC-NOTICES.md.
using System;
using System.IO;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Adaptive multi-symbol decoder of the Oodle1 codec. Slot 0 is the escape
/// symbol; learned symbols occupy higher slots, with symbols learned since the
/// last renormalization held in a probationary alphabet and unseen symbols
/// arriving through a proportional fallback alphabet. Weights decay and
/// renormalize as symbols are decoded.
/// </summary>
internal sealed class Oodle1SymbolCoder
{
    private const ushort FullScale = 0x4000;

    private readonly int[] _symbolValues;
    private readonly ushort[] _spanLowerBounds;
    private readonly int[] _symbolWeights;
    private readonly int _uniqueExpected;
    private readonly int _decayThreshold;
    private readonly int _renormalizeInterval;
    private int _rapidRenormalizeInterval;
    private int _highestLearned;
    private int _highestNormalized;
    private int _totalWeight;
    private int _nextRenormalizeWeight;

    public Oodle1SymbolCoder(int alphabetSize, int uniqueExpected)
    {
        if (alphabetSize < 1)
        {
            throw new InvalidDataException(
                $"Oodle1 symbol coder requires a positive alphabet size, got {alphabetSize}.");
        }

        _uniqueExpected = uniqueExpected;
        _symbolValues = new int[Math.Max(alphabetSize, uniqueExpected) + 2];
        _spanLowerBounds = new ushort[_symbolValues.Length];
        _symbolWeights = new int[_symbolValues.Length];
        _symbolWeights[0] = 4;
        _totalWeight = 4;
        _nextRenormalizeWeight = 8;
        _rapidRenormalizeInterval = 4;
        _decayThreshold = Math.Clamp((alphabetSize - 1) * 32, 256, 15160);
        _renormalizeInterval = ComputeRenormalizeInterval(alphabetSize);
        for (var slot = 0; slot < _spanLowerBounds.Length; slot++)
        {
            _spanLowerBounds[slot] = FullScale;
        }

        _spanLowerBounds[0] = 0;
    }

    /// <summary>Decodes one symbol; new symbols are drawn from [0, effectiveAlphabetSize).</summary>
    public int Decode(uint effectiveAlphabetSize, ref Oodle1Bitstream stream)
    {
        if (effectiveAlphabetSize < 1)
        {
            throw new InvalidDataException(
                $"Oodle1 symbol coder was asked to decode from an empty alphabet of {effectiveAlphabetSize}.");
        }

        if (_totalWeight >= _nextRenormalizeWeight)
        {
            // Decay is keyed to the threshold fixed at the last
            // renormalization, not the current weight: the two differ in a
            // narrow window when the weight sum jumps past the decay
            // threshold between checks, and Granny follows the threshold
            // form (validated against blendergranny's DLL-matched decoder;
            // liboodle's spec text states the current-weight form).
            if (_nextRenormalizeWeight >= _decayThreshold)
            {
                Decay();
            }

            Renormalize();
        }

        var value = stream.Peek(FullScale, out _);
        var slot = FindSlot(value);
        var offset = _spanLowerBounds[slot];
        var span = (uint)(_spanLowerBounds[slot + 1] - offset);
        stream.Consume(offset, span, FullScale);
        _symbolWeights[slot]++;
        _totalWeight++;

        if (slot != 0)
        {
            return _symbolValues[slot];
        }

        if (_highestLearned != _highestNormalized && stream.Get(2) != 0)
        {
            var probationary = (int)stream.Get((uint)(_highestLearned - _highestNormalized))
                + _highestNormalized + 1;
            _symbolWeights[probationary] += 2;
            _totalWeight += 2;
            return _symbolValues[probationary];
        }

        if (_highestLearned + 1 >= _symbolValues.Length)
        {
            throw new InvalidDataException("Oodle1 symbol alphabet overflowed its declared size.");
        }

        _highestLearned++;
        _symbolValues[_highestLearned] = (int)stream.Get(effectiveAlphabetSize);
        _symbolWeights[_highestLearned] += 2;
        _totalWeight += 2;
        if (_highestLearned == _uniqueExpected)
        {
            _totalWeight -= _symbolWeights[0];
            _symbolWeights[0] = 0;
        }

        return _symbolValues[_highestLearned];
    }

    private static int ComputeRenormalizeInterval(int alphabetSize)
    {
        var interval = (alphabetSize - 1) * 2;
        if (interval < 128)
        {
            return 128;
        }

        var cap = (Math.Clamp((alphabetSize - 1) * 32, 256, 15160) / 2) - 32;
        return interval > cap ? cap : interval;
    }

    private int FindSlot(uint value)
    {
        for (var slot = 0; slot <= _highestNormalized; slot++)
        {
            if (value < _spanLowerBounds[slot + 1])
            {
                return slot;
            }
        }

        return _highestNormalized;
    }

    private void Decay()
    {
        _symbolWeights[0] >>= 1;
        _totalWeight = _symbolWeights[0];
        var heaviestWeight = 0;
        var heaviestSlot = 0;
        for (var slot = 1; slot <= _highestLearned; slot++)
        {
            while (_symbolWeights[slot] <= 1)
            {
                if (slot < _highestLearned)
                {
                    _symbolWeights[slot] = _symbolWeights[_highestLearned];
                    _symbolWeights[_highestLearned] = 0;
                    _symbolValues[slot] = _symbolValues[_highestLearned];
                    _highestLearned--;
                }
                else
                {
                    _symbolWeights[slot] = 0;
                    _highestLearned--;
                    break;
                }
            }

            if (slot > _highestLearned)
            {
                break;
            }

            _symbolWeights[slot] >>= 1;
            _totalWeight += _symbolWeights[slot];
            if (_symbolWeights[slot] > heaviestWeight)
            {
                heaviestWeight = _symbolWeights[slot];
                heaviestSlot = slot;
            }
        }

        if (heaviestWeight != 0 && heaviestSlot != _highestLearned)
        {
            (_symbolWeights[_highestLearned], _symbolWeights[heaviestSlot]) =
                (_symbolWeights[heaviestSlot], _symbolWeights[_highestLearned]);
            (_symbolValues[_highestLearned], _symbolValues[heaviestSlot]) =
                (_symbolValues[heaviestSlot], _symbolValues[_highestLearned]);
        }

        if (_highestLearned != _uniqueExpected && _symbolWeights[0] == 0)
        {
            _symbolWeights[0] = 1;
            _totalWeight++;
        }
    }

    private void Renormalize()
    {
        var weightSum = _totalWeight < 1 ? 1 : _totalWeight;
        var quantum = 0x20000 / weightSum;
        _spanLowerBounds[0] = 0;
        var cumulative = (_symbolWeights[0] * quantum) / 8;
        for (var slot = 1; slot <= _highestLearned; slot++)
        {
            // Per-symbol weights are bounded by the weight sum, so each
            // scaled term stays below 0x20000 and the running lower bound
            // never exceeds the 0x4000 full scale.
            _spanLowerBounds[slot] = (ushort)cumulative;
            cumulative += (_symbolWeights[slot] * quantum) / 8;
        }

        if (_rapidRenormalizeInterval * 2 < _renormalizeInterval)
        {
            _rapidRenormalizeInterval *= 2;
            _nextRenormalizeWeight = _totalWeight + _rapidRenormalizeInterval;
        }
        else
        {
            _nextRenormalizeWeight = _totalWeight + _renormalizeInterval;
        }

        _highestNormalized = _highestLearned;
        for (var slot = _highestLearned + 1; slot < _spanLowerBounds.Length; slot++)
        {
            _spanLowerBounds[slot] = FullScale;
        }
    }
}
