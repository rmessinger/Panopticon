#nullable enable

using System;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// Shared inference core for the per-rank networks (A4-3e): one tanh hidden layer and
/// argmax over logits, ties to the lowest index (softmax is monotone). Genomes are flat
/// [W1 (per hidden unit) | b1 | W2 (per output unit) | b2] vectors, cloned and
/// length-validated at construction. Inference is a deterministic function of the features
/// and weights; no RNG. Contract: hierarchical-neural-network-policies.md.
/// </summary>
internal sealed class NeuralRankNetwork
{
    private readonly float[] _weights;

    public int InputSize { get; }

    public int HiddenSize { get; }

    public int OutputSize { get; }

    public NeuralRankNetwork(float[]? weights, int inputSize, int hiddenSize, int outputSize)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var expected = inputSize * hiddenSize + hiddenSize + hiddenSize * outputSize + outputSize;
        if (weights.Length != expected)
        {
            throw new ArgumentException(
                $"This rank network requires exactly {expected} weights "
                + $"({inputSize}x{hiddenSize} + {hiddenSize} + {hiddenSize}x{outputSize} + {outputSize}); got {weights.Length}.",
                nameof(weights));
        }

        InputSize = inputSize;
        HiddenSize = hiddenSize;
        OutputSize = outputSize;
        _weights = (float[])weights.Clone();
    }

    /// <summary>Forward pass plus argmax (ties to the lowest output index).</summary>
    public int Decide(ReadOnlySpan<float> features)
    {
        Span<float> hidden = stackalloc float[HiddenSize];
        Span<float> logits = stackalloc float[OutputSize];
        ForwardPass(features, hidden, logits);

        var best = 0;
        for (var k = 1; k < logits.Length; k++)
        {
            if (logits[k] > logits[best])
            {
                best = k;
            }
        }

        return best;
    }

    internal void ForwardPass(ReadOnlySpan<float> features, Span<float> hidden, Span<float> logits)
    {
        for (var j = 0; j < HiddenSize; j++)
        {
            var sum = _weights[InputSize * HiddenSize + j];
            for (var i = 0; i < InputSize; i++)
            {
                sum += _weights[j * InputSize + i] * features[i];
            }

            hidden[j] = MathF.Tanh(sum);
        }

        var w2Offset = InputSize * HiddenSize + HiddenSize;
        for (var k = 0; k < OutputSize; k++)
        {
            var sum = _weights[w2Offset + HiddenSize * OutputSize + k];
            for (var j = 0; j < HiddenSize; j++)
            {
                sum += _weights[w2Offset + k * HiddenSize + j] * hidden[j];
            }

            logits[k] = sum;
        }
    }
}
