using System;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Graphics.ParticleSystems;

internal static class ParticleSystemUtility
{
    private static Random Random = new Random(1000);

    /// <summary>
    /// Scopes all particle randomness to a fresh seed and restores the previous generator on
    /// disposal, so repeated capture setups with the same seed sample identical sequences.
    /// </summary>
    internal static IDisposable BeginSeedScope(int seed)
    {
        var previous = Random;
        Random = new Random(seed);
        return new SeedScope(previous);
    }

    /// <summary>
    /// Process-stable seed derivation (string.GetHashCode is randomized per process) for
    /// effect keys plus a sample index, per the C4h determinism contract.
    /// </summary>
    internal static int ComputeDeterministicSeed(string effectKey, int sampleIndex)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in effectKey)
            {
                hash = (hash ^ c) * 16777619u;
            }

            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            return (int)(hash | 1u);
        }
    }

    private sealed class SeedScope : IDisposable
    {
        private Random _previous;

        public SeedScope(Random previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            Random = _previous;
            _previous = null;
        }
    }

    public static float GetRandomFloat(float min, float max)
    {
        return GetRandomFloat(new RandomVariable(min, max, DistributionType.Uniform));
    }

    public static float GetRandomFloat(this RandomVariable variable)
    {
        var min = variable.Low;
        var max = variable.High;

        if (min == max)
        {
            return min;
        }

        // This is how the original engine behaves.
        if (max < min)
        {
            return max;
        }

        return variable.DistributionType switch
        {
            DistributionType.Uniform => min + ((float)Random.NextDouble() * (max - min)),
            _ => throw new NotSupportedException(),
        };
    }

    public static int GetRandomInt(this RandomVariable variable)
    {
        // TODO: I don't think this will ever give us the max value.
        return (int)variable.GetRandomFloat();
    }

    public static float GetRandomAngle()
    {
        return MathUtility.TwoPi * (float)Random.NextDouble();
    }

    public static Vector2 GetRandomDirection2D()
    {
        float azimuth = GetRandomAngle();
        return new Vector2(
            MathF.Cos(azimuth),
            MathF.Sin(azimuth));
    }

    public static Vector3 GetRandomDirection3D()
    {
        float z = (2 * (float)Random.NextDouble()) - 1; // z is in the range [-1,1]
        var planar = GetRandomDirection2D() * MathF.Sqrt(1 - z * z);
        return new Vector3(planar.X, planar.Y, z);
    }
}
