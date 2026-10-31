// BitKnit2 deferred adaptive probability model. Original C# written from the
// libbg3 written specification (github.com/eiz/libbg3@23c701a, MIT);
// cross-checked against pybg3 rans.h (github.com/eiz/pybg3@3b02e59, MIT,
// Copyright 2024 Mackenzie Straight). No code copied; see CODEC-NOTICES.md.
using System;

namespace Panopticon.FileFormats.Granny;

/// <summary>
/// Semi-adaptive probability model: symbol frequencies accumulate between
/// updates, and the distribution is rebuilt every AdaptationInterval observed
/// symbols by blending the accumulated counts into the prefix sums with a
/// halving step (an exponential moving average).
/// </summary>
internal sealed class BitKnit2AdaptiveModel
{
    private readonly ushort[] _accumulator;
    private readonly int _frequencyIncrement;
    private readonly int _lastFrequencyIncrement;
    private readonly int _adaptationInterval;
    private int _adaptationCounter;

    public BitKnit2AdaptiveModel(int adaptationInterval, int vocabSize, int minProbableCount, int frequencyBits, int lookupBits)
    {
        _adaptationInterval = adaptationInterval;
        Cdf = new BitKnit2FrequencyTable(frequencyBits, vocabSize, lookupBits);
        var totalSum = 1 << frequencyBits;
        var equiprobable = vocabSize - minProbableCount;
        _frequencyIncrement = (totalSum - vocabSize) / adaptationInterval;
        _lastFrequencyIncrement = 1 + totalSum - vocabSize - (_frequencyIncrement * adaptationInterval);
        _accumulator = new ushort[vocabSize];
        for (var i = 0; i < equiprobable; i++)
        {
            Cdf.Sums[i] = (ushort)((long)(totalSum - minProbableCount) * i / equiprobable);
        }

        for (var i = equiprobable; i <= vocabSize; i++)
        {
            Cdf.Sums[i] = (ushort)(totalSum - vocabSize + i);
        }

        for (var i = 0; i < vocabSize; i++)
        {
            _accumulator[i] = 1;
        }

        Cdf.FinishUpdate();
    }

    public BitKnit2FrequencyTable Cdf { get; }

    /// <summary>Records one observed symbol; rebuilds the distribution when the interval elapses.</summary>
    public void ObserveSymbol(int symbol)
    {
        _accumulator[symbol] += (ushort)_frequencyIncrement;
        _adaptationCounter = (_adaptationCounter + 1) % _adaptationInterval;
        if (_adaptationCounter != 0)
        {
            return;
        }

        _accumulator[symbol] += (ushort)_lastFrequencyIncrement;
        var sums = Cdf.Sums;
        uint accumulated = 0;
        for (var i = 1; i <= Cdf.VocabSize; i++)
        {
            accumulated += _accumulator[i - 1];
            sums[i] += (ushort)((accumulated - sums[i]) / 2);
            _accumulator[i - 1] = 1;
        }

        Cdf.FinishUpdate();
    }
}
