using System;
using System.Numerics;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Graphics.ParticleSystems;

public class ParticleSystemUtilityTests
{
    [Fact]
    public void SeedScopeRepeatsIdenticalSamples()
    {
        float[] first;
        float[] second;

        using (ParticleSystemUtility.BeginSeedScope(12345))
        {
            first = Sample();
        }

        using (ParticleSystemUtility.BeginSeedScope(12345))
        {
            second = Sample();
        }

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentSequences()
    {
        float[] first;
        float[] second;

        using (ParticleSystemUtility.BeginSeedScope(1))
        {
            first = Sample();
        }

        using (ParticleSystemUtility.BeginSeedScope(2))
        {
            second = Sample();
        }

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DisposingSeedScopeRestoresThePreviousSequence()
    {
        var before = new RandomVariable(0, 1000, DistributionType.Uniform);

        float actualThirdDraw;
        using (ParticleSystemUtility.BeginSeedScope(100))
        {
            _ = before.GetRandomFloat();
            _ = before.GetRandomFloat();

            using (ParticleSystemUtility.BeginSeedScope(999))
            {
                _ = before.GetRandomFloat();
                _ = before.GetRandomFloat();
            }

            // Must be the seed-100 generator's third draw: the nested scope consumed nothing.
            actualThirdDraw = before.GetRandomFloat();
        }

        float expectedThirdDraw;
        using (ParticleSystemUtility.BeginSeedScope(100))
        {
            _ = before.GetRandomFloat();
            _ = before.GetRandomFloat();
            expectedThirdDraw = before.GetRandomFloat();
        }

        Assert.Equal(expectedThirdDraw, actualThirdDraw);
    }

    [Fact]
    public void DeterministicSeedIsStableAcrossCallsAndVariesByKeyAndSample()
    {
        var a1 = ParticleSystemUtility.ComputeDeterministicSeed("fx_explosion_1", 0);
        var a1Again = ParticleSystemUtility.ComputeDeterministicSeed("fx_explosion_1", 0);
        var a2 = ParticleSystemUtility.ComputeDeterministicSeed("fx_explosion_1", 1);
        var b1 = ParticleSystemUtility.ComputeDeterministicSeed("fx_muzzle_tank", 0);

        Assert.Equal(a1, a1Again);
        Assert.NotEqual(a1, a2);
        Assert.NotEqual(a1, b1);
    }

    private static float[] Sample()
    {
        var samples = new float[16];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = i % 3 switch
            {
                0 => ParticleSystemUtility.GetRandomFloat(0, 100),
                1 => ParticleSystemUtility.GetRandomDirection3D().X,
                _ => ParticleSystemUtility.GetRandomAngle(),
            };
        }

        return samples;
    }
}
