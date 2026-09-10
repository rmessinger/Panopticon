#nullable enable

using System;
using System.Collections.Generic;
using OpenSage.Logic.AI;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One genome: exactly the v1 policy's weight vector (125 floats).</summary>
public sealed class NeuralGenome
{
    public int Index { get; }

    public float[] Weights { get; }

    public NeuralGenome(int index, IReadOnlyList<float> weights)
    {
        if (weights.Count != NeuralNetworkAIDecisionPolicy.WeightCount)
        {
            throw new ArgumentException(
                $"A neural genome requires exactly {NeuralNetworkAIDecisionPolicy.WeightCount} weights; got {weights.Count}.",
                nameof(weights));
        }

        Index = index;
        Weights = [.. weights];
    }
}
