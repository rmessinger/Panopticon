#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.FileFormats.Nif;

namespace OpenSage.Content.Importing.Nif;

/// <summary>
/// One resampled source-frame rotation sample at a fixed time.
/// </summary>
internal readonly record struct NifQuaternionSample(float Time, Quaternion Rotation);

/// <summary>
/// Resamples NIF XYZ Euler rotation keys into ordered source-frame
/// quaternion samples (deep dive 6.3). Quaternion keys are used as
/// parsed and do not pass through here.
/// </summary>
internal static class NifRotationSampler
{
    private const float SamplesPerSecond = 30f;
    private const float TimeEpsilon = 1e-6f;
    private const float MaximumStepRadians = MathF.PI / 2f;

    /// <summary>
    /// The stored-matrix layout for the column-vector product
    /// Rz(z) * Ry(y) * Rx(x), locked by the deep dive's R_OAR14 parity.
    /// </summary>
    internal static NifMatrix33 EulerToMatrix(float x, float y, float z)
    {
        var sx = MathF.Sin(x);
        var cx = MathF.Cos(x);
        var sy = MathF.Sin(y);
        var cy = MathF.Cos(y);
        var sz = MathF.Sin(z);
        var cz = MathF.Cos(z);

        return new NifMatrix33(
            cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx,
            sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx,
            -sy, cy * sx, cy * cx);
    }

    /// <summary>
    /// Builds the sample-time set: every k/30 step, the duration, and
    /// every source key time, deduplicated within 1e-6 s. Evaluates the
    /// three axes, composes them as Rz * Ry * Rx, converts to quaternions,
    /// and flips consecutive samples into one hemisphere. Throws
    /// NotSupportedException when any step reaches 90 degrees.
    /// </summary>
    internal static IReadOnlyList<NifQuaternionSample> SampleXyz(NifKeyframeData data, float duration)
    {
        var times = BuildSampleTimes(data, duration);
        var samples = new List<NifQuaternionSample>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            var time = times[i];
            var rotation = ToQuaternion(EulerToMatrix(
                EvaluateAxis(data.XRotationKeys, time),
                EvaluateAxis(data.YRotationKeys, time),
                EvaluateAxis(data.ZRotationKeys, time)));

            if (i > 0)
            {
                var previous = samples[i - 1].Rotation;
                if (Quaternion.Dot(previous, rotation) < 0f)
                {
                    rotation = -rotation;
                }

                var step = 2f * MathF.Acos(MathF.Min(1f, Quaternion.Dot(previous, rotation)));
                if (step >= MaximumStepRadians)
                {
                    throw new NotSupportedException(
                        $"NIF XYZ rotation resampling produced a step of {step * 180f / MathF.PI:F1} degrees "
                        + $"at {time:F4} s; steps must stay below 90 degrees.");
                }
            }

            samples.Add(new NifQuaternionSample(time, rotation));
        }

        return samples;
    }

    private static List<float> BuildSampleTimes(NifKeyframeData data, float duration)
    {
        var times = new List<float>();
        var stepCount = (int)MathF.Floor(SamplesPerSecond * duration);
        for (var k = 0; k <= stepCount; k++)
        {
            times.Add(k / SamplesPerSecond);
        }

        times.Add(duration);
        AddKeyTimes(times, data.XRotationKeys);
        AddKeyTimes(times, data.YRotationKeys);
        AddKeyTimes(times, data.ZRotationKeys);
        times.Sort();

        var deduplicated = new List<float>(times.Count);
        foreach (var time in times)
        {
            if (deduplicated.Count == 0 || time - deduplicated[^1] > TimeEpsilon)
            {
                deduplicated.Add(time);
            }
        }

        return deduplicated;
    }

    private static void AddKeyTimes(List<float> times, NifKeyGroup<NifKey<float>>? group)
    {
        if (group is null)
        {
            return;
        }

        foreach (var key in group.Keys)
        {
            times.Add(key.Time);
        }
    }

    private static float EvaluateAxis(NifKeyGroup<NifKey<float>>? group, float time)
    {
        return group is null ? 0f : NifKeyCurve.Evaluate(group, time);
    }

    private static Quaternion ToQuaternion(in NifMatrix33 rotation)
    {
        // Positional copy matching NifTransformConverter.CreateLocalMatrix;
        // System.Numerics quaternions are internally consistent with this
        // row-vector layout.
        var engine = Matrix4x4.Identity;
        engine.M11 = rotation.M11;
        engine.M12 = rotation.M12;
        engine.M13 = rotation.M13;
        engine.M21 = rotation.M21;
        engine.M22 = rotation.M22;
        engine.M23 = rotation.M23;
        engine.M31 = rotation.M31;
        engine.M32 = rotation.M32;
        engine.M33 = rotation.M33;
        return Quaternion.CreateFromRotationMatrix(engine);
    }
}
