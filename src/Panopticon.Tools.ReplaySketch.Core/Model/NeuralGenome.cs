#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Logic.AI;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>One genome: exactly the v1 policy's weight vector (125 floats).</summary>
public sealed class NeuralGenome
{
    public int Index { get; }

    public float[] Weights { get; }

    public NeuralGenome(int index, IReadOnlyList<float> weights, int expectedWeightCount = NeuralNetworkAIDecisionPolicy.WeightCount)
    {
        if (weights.Count != expectedWeightCount)
        {
            throw new ArgumentException(
                $"A neural genome requires exactly {expectedWeightCount} weights; got {weights.Count}.",
                nameof(weights));
        }

        Index = index;
        Weights = [.. weights];
    }
}
