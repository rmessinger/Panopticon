#nullable enable

using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Samples one Granny animation curve. AoE3 clips store every animated channel as a
/// CurveDataHeader_DaK32fC32f keyframe list: one control per knot, where the
/// controls-per-knot ratio selects the component layout (3 position, 4 rotation,
/// 9 scale-shear). Values between knots interpolate linearly (rotation via slerp).
/// Constant formats (3, 4, 5) carry a single control set repeated for all time.
/// </summary>
internal sealed class GrannyCurve
{
    public const uint FormatDaK32fC32f = 1;

    public GrannyCurve(float[] knots, float[] controls, int components)
    {
        Knots = knots;
        Controls = controls;
        Components = components;
    }

    public float[] Knots { get; }

    public float[] Controls { get; }

    public int Components { get; }

    public bool IsConstant
    {
        get => Knots.Length <= 1;
    }

    /// <summary>
    /// Builds a curve from a transform-track curve wrapper object, or returns null when the
    /// wrapper is absent, the curve carries no samples, or the file stores no knot list.
    /// Unsupported and malformed curves throw with the clip, track, and curve names.
    /// </summary>
    public static GrannyCurve? FromWrapper(
        GrannyObject? wrapper,
        string sourceKey,
        string trackName,
        string curveName)
    {
        var curve = wrapper?.GetObject("CurveData");
        if (curve is null)
        {
            return null;
        }

        var format = curve.GetUInt32("Format").GetValueOrDefault();
        if (format is not (FormatDaK32fC32f or 3 or 4 or 5))
        {
            throw new InvalidDataException(
                $"Granny animation '{sourceKey}': track '{trackName}' {curveName} uses unsupported " +
                $"curve format {format} ('{curve.GetString("CurveDataKind")}'), which has no keyframe list.");
        }

        var knots = format == FormatDaK32fC32f ? ToFloats(curve.GetArray("Knots")) : [0f];
        var controls = ToFloats(curve.GetArray("Controls"));
        if (controls.Length == 0)
        {
            // An empty animated curve (the empty scale-shear curves most tracks carry) adds
            // no channel; the runtime keeps the bind-pose value.
            return null;
        }

        var components = controls.Length % knots.Length == 0 ? controls.Length / knots.Length : 0;
        if (components is not (3 or 4 or 9))
        {
            throw new InvalidDataException(
                $"Granny animation '{sourceKey}': track '{trackName}' {curveName} carries " +
                $"{controls.Length} controls over {knots.Length} knots, matching no known component layout.");
        }

        return new GrannyCurve(knots, controls, components);
    }

    private static float[] ToFloats(object? array)
    {
        return array is float[] values ? values : [];
    }

    public Vector3 SamplePosition(float time)
    {
        if (Knots.Length == 1)
        {
            return new Vector3(Controls[0], Controls[1], Controls[2]);
        }

        var (index, alpha) = FindSpan(time);
        var from = new Vector3(Controls[index * Components], Controls[index * Components + 1], Controls[index * Components + 2]);
        var to = new Vector3(
            Controls[(index + 1) * Components],
            Controls[(index + 1) * Components + 1],
            Controls[(index + 1) * Components + 2]);
        return Vector3.Lerp(from, to, alpha);
    }

    public Quaternion SampleQuaternion(float time)
    {
        if (Knots.Length == 1)
        {
            return Quaternion.Normalize(ReadQuaternion(0));
        }

        var (index, alpha) = FindSpan(time);
        var from = ReadQuaternion(index);
        var to = ReadQuaternion(index + 1);
        return Quaternion.Normalize(Quaternion.Slerp(from, to, alpha));
    }

    /// <summary>
    /// Row-major 3x3 scale-shear controls at the given time.
    /// </summary>
    public float[] SampleScaleShear(float time)
    {
        if (Knots.Length == 1)
        {
            var single = new float[Components];
            Array.Copy(Controls, single, Components);
            return single;
        }

        var (index, alpha) = FindSpan(time);
        var values = new float[Components];
        for (var c = 0; c < Components; c++)
        {
            values[c] = Controls[index * Components + c] +
                (Controls[(index + 1) * Components + c] - Controls[index * Components + c]) * alpha;
        }

        return values;
    }

    private Quaternion ReadQuaternion(int knotIndex)
    {
        return new Quaternion(
            Controls[knotIndex * Components],
            Controls[knotIndex * Components + 1],
            Controls[knotIndex * Components + 2],
            Controls[knotIndex * Components + 3]);
    }

    /// <summary>
    /// Locates the knot span covering the time and the blend factor within it. Times before
    /// the first knot snap to it; times past the last knot clamp to the final span.
    /// </summary>
    private (int Index, float Alpha) FindSpan(float time)
    {
        if (time <= Knots[0])
        {
            return (0, 0f);
        }

        for (var i = 1; i < Knots.Length; i++)
        {
            if (time <= Knots[i])
            {
                var span = Knots[i] - Knots[i - 1];
                var alpha = span > 0 ? (time - Knots[i - 1]) / span : 0f;
                return (i - 1, alpha);
            }
        }

        return (Knots.Length - 2, 1f);
    }
}
