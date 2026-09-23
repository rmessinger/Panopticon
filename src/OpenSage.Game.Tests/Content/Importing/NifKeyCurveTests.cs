using System;
using System.IO;
using System.Numerics;
using OpenSage.Content.Importing.Nif;
using OpenSage.FileFormats.Nif;
using OpenSage.Tests.Data.Nif;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public sealed class NifKeyCurveTests
{
    private static NifKeyframeData ReadKeyframeData(byte[] dataBody)
    {
        var builder = new NifTestDataBuilder();
        var controllerType = builder.AddBlockType("NiKeyframeController");
        var dataType = builder.AddBlockType("NiKeyframeData");
        builder.AddBlock(controllerType, NifTestKeyframe.KeyframeController(data: 1));
        builder.AddBlock(dataType, dataBody);
        builder.AddTrailing(NifTestBlocks.Footer());

        using var stream = new MemoryStream(builder.Build());
        var file = NifFile.Read(stream);
        return (NifKeyframeData)file.Blocks[1];
    }

    private static NifKeyGroup<NifKey<float>> Group(
        NifKeyType type,
        params (float Time, float Value, float? Forward, float? Backward)[] keys)
    {
        var parsed = new NifKey<float>[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            parsed[i] = new NifKey<float>(keys[i].Time, keys[i].Value, keys[i].Forward, keys[i].Backward);
        }

        return new NifKeyGroup<NifKey<float>>(type, parsed);
    }

    [Fact]
    public void EulerToMatrixMatchesStoredOarRestMatrix()
    {
        // Stored R_OAR14 rest rotation, dumped from nwg1_galley.nifcache.
        // The deep dive locks this triple to the matrix within 1.1e-4.
        var matrix = NifRotationSampler.EulerToMatrix(2.3865f, 0.5080f, -1.1135f);

        AssertEqual(matrix.M11, 0.38575035f);
        AssertEqual(matrix.M12, -0.5062548f);
        AssertEqual(matrix.M13, -0.7712994f);
        AssertEqual(matrix.M21, -0.7839769f);
        AssertEqual(matrix.M22, -0.6206029f);
        AssertEqual(matrix.M23, 0.015251815f);
        AssertEqual(matrix.M31, -0.48639184f);
        AssertEqual(matrix.M32, 0.5987975f);
        AssertEqual(matrix.M33, -0.63628995f);

        static void AssertEqual(float actual, float expected)
        {
            Assert.True(
                MathF.Abs(actual - expected) <= 2e-4f,
                $"Expected {expected}, got {actual}.");
        }
    }

    [Fact]
    public void HermiteEvaluationReproducesKeysAndMidpoint()
    {
        // Quadratic segment with out0 = first.Backward = 2 and
        // in1 = second.Forward = -2. At the midpoint s = 0.5:
        // 0.5 * 0 + 0.125 * 2 + 0.5 * 2 + (-0.125) * (-2) = 1.5.
        var group = Group(
            NifKeyType.Quadratic,
            (0.0f, 0.0f, null, 2.0f),
            (2.0f, 2.0f, -2.0f, null));

        Assert.Equal(0.0f, NifKeyCurve.Evaluate(group, 0.0f), 5);
        Assert.Equal(2.0f, NifKeyCurve.Evaluate(group, 2.0f), 5);
        Assert.Equal(1.5f, NifKeyCurve.Evaluate(group, 1.0f), 5);
    }

    [Fact]
    public void LinearKeysInterpolateAndHoldEndValues()
    {
        var group = Group(
            NifKeyType.Linear,
            (0.0f, 0.0f, null, null),
            (2.0f, 2.0f, null, null));

        Assert.Equal(0.0f, NifKeyCurve.Evaluate(group, -5.0f), 5);
        Assert.Equal(1.0f, NifKeyCurve.Evaluate(group, 1.0f), 5);
        Assert.Equal(2.0f, NifKeyCurve.Evaluate(group, 9.0f), 5);
    }

    [Fact]
    public void SingleKeyCurveIsConstant()
    {
        var group = Group(NifKeyType.Linear, (0.25f, 7.0f, null, null));

        Assert.Equal(7.0f, NifKeyCurve.Evaluate(group, 0.0f), 5);
        Assert.Equal(7.0f, NifKeyCurve.Evaluate(group, 0.25f), 5);
        Assert.Equal(7.0f, NifKeyCurve.Evaluate(group, 1.0f), 5);
    }

    [Fact]
    public void ThreeTurnSpinYieldsThirtySixDegreeStepsInOneDirection()
    {
        var data = ReadKeyframeData(NifTestKeyframe.KeyframeData(
            xyzRotationOrder: 0.0f,
            yRotationKeys: [(0.0f, 0.0f, null, null), (1.0f, 18.849556f, null, null)]));

        var samples = NifRotationSampler.SampleXyz(data, 1.0f);

        Assert.Equal(31, samples.Count);
        AssertEqual(samples[0].Rotation, Quaternion.Identity);
        // Three full turns end on the identity again; only the resampled
        // interior preserves the motion.
        AssertEqual(samples[^1].Rotation, Quaternion.Identity);
        for (var i = 1; i < samples.Count; i++)
        {
            Assert.Equal(i / 30f, samples[i].Time, 6);

            var previous = samples[i - 1].Rotation;
            var current = samples[i].Rotation;
            var dot = MathF.Min(1f, Quaternion.Dot(previous, current));
            var step = 2f * MathF.Acos(dot);
            Assert.True(
                MathF.Abs(step - MathF.PI / 5f) <= 1e-4f,
                $"Step {i} turned {step * 180f / MathF.PI} degrees, expected 36.");

            var delta = current * Quaternion.Conjugate(previous);
            Assert.True(
                delta.Y < 0f,
                $"Step {i} turned the wrong way.");
        }
    }

    [Fact]
    public void SampleTimesCoverGridDurationAndKeyTimesExactlyOnce()
    {
        var data = ReadKeyframeData(NifTestKeyframe.KeyframeData(
            xyzRotationOrder: 0.0f,
            xRotationKeys: [(0.1f, 0.0f, null, null)],
            yRotationKeys: [(0.35f, 0.0f, null, null)]));

        var samples = NifRotationSampler.SampleXyz(data, 0.5f);

        // 16 grid steps (0 to 15/30 = 0.5), 0.1 deduplicates against 3/30,
        // and 0.35 falls between two grid points.
        Assert.Equal(17, samples.Count);
        Assert.Equal(0.0f, samples[0].Time, 6);
        Assert.Equal(0.5f, samples[^1].Time, 6);
        var offGrid = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            if (i > 0)
            {
                Assert.True(samples[i].Time > samples[i - 1].Time, "Sample times must increase.");
            }

            if (MathF.Abs(samples[i].Time - 0.35f) <= 1e-6f)
            {
                offGrid++;
            }
        }

        Assert.Equal(1, offGrid);
    }

    [Fact]
    public void StepOfNinetyDegreesOrMoreIsRejected()
    {
        var data = ReadKeyframeData(NifTestKeyframe.KeyframeData(
            xyzRotationOrder: 0.0f,
            yRotationKeys: [(0.0f, 0.0f, null, null), (1f / 30f, MathF.PI, null, null)]));

        var ex = Assert.Throws<NotSupportedException>(
            () => NifRotationSampler.SampleXyz(data, 1f / 30f));
        Assert.Contains("90 degrees", ex.Message);
    }

    private static void AssertEqual(Quaternion actual, Quaternion expected)
    {
        // Equal up to hemisphere: q and -q are the same rotation.
        Assert.True(
            MathF.Abs(MathF.Abs(Quaternion.Dot(actual, expected)) - 1f) <= 1e-5f,
            $"Expected {expected}, got {actual}.");
    }
}
