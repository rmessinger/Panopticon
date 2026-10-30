#nullable enable

using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Samples one Granny animation curve. AoE3 clips store every animated channel as a
/// CurveDataHeader_DaK32fC32f B-spline: one control per knot, where the
/// controls-per-knot ratio selects the component layout (3 position, 4 rotation,
/// 9 scale-shear). Evaluation is De Boor's algorithm at the header degree (the corpus
/// uses quadratic), blending component values linearly; knots and controls are B-spline
/// parameters, not keyframe times and on-curve values, and clamped files repeat the
/// boundary controls so clamping knot indices to the array bounds is exact at both ends.
/// Rotations blend linearly and normalize (controls ship unit-length). Constant formats
/// (3, 4, 5) carry a single control set repeated for all time.
/// </summary>
internal sealed class GrannyCurve
{
    public const uint FormatDaK32fC32f = 1;

    public GrannyCurve(float[] knots, float[] controls, int components, int degree = 2)
    {
        Knots = knots;
        Controls = controls;
        Components = components;
        Degree = degree;
    }

    public float[] Knots { get; }

    public float[] Controls { get; }

    public int Components { get; }

    public int Degree { get; }

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

        return new GrannyCurve(knots, controls, components, (int)curve.GetUInt32("Degree").GetValueOrDefault(2));
    }

    private static float[] ToFloats(object? array)
    {
        return array is float[] values ? values : [];
    }

    public Vector3 SamplePosition(float time)
    {
        return new Vector3(BlendAt(time, 0), BlendAt(time, 1), BlendAt(time, 2));
    }

    public Quaternion SampleQuaternion(float time)
    {
        return Quaternion.Normalize(new Quaternion(
            BlendAt(time, 0), BlendAt(time, 1), BlendAt(time, 2), BlendAt(time, 3)));
    }

    /// <summary>
    /// Row-major 3x3 scale-shear controls at the given time.
    /// </summary>
    public float[] SampleScaleShear(float time)
    {
        var values = new float[Components];
        for (var c = 0; c < Components; c++)
        {
            values[c] = BlendAt(time, c);
        }

        return values;
    }

    /// <summary>
    /// One B-spline component at the given time, via De Boor's algorithm over the full
    /// clamped knot vector implied by the compact Granny array (head and tail knots
    /// duplicated to degree+1 multiplicity), so the curve hits the first and last
    /// controls exactly at the knot range ends and stays inside the control hull.
    /// Falls back to the boundary control outside the knot range and to the single
    /// control of constant curves.
    /// </summary>
    private float BlendAt(float time, int component)
    {
        var knotCount = Knots.Length;
        if (knotCount == 1)
        {
            return Controls[component];
        }

        if (time <= Knots[0])
        {
            return Controls[component];
        }

        if (time >= Knots[knotCount - 1])
        {
            return Controls[(knotCount - 1) * Components + component];
        }

        var degree = Math.Clamp(Degree, 1, knotCount - 1);

        // Span: largest k in [degree, knotCount-1] with KnotAt(k) <= time.
        var span = degree;
        for (var k = degree; k < knotCount; k++)
        {
            if (KnotAt(k) <= time)
            {
                span = k;
            }
            else
            {
                break;
            }
        }

        var count = degree + 1;
        Span<float> values = stackalloc float[count];
        for (var j = 0; j < count; j++)
        {
            values[j] = Controls[(j + span - degree) * Components + component];
        }

        for (var r = 1; r <= degree; r++)
        {
            for (var j = degree; j >= r; j--)
            {
                var knotLeft = KnotAt(j + span - degree);
                var knotRight = KnotAt(j + 1 + span - r);
                var denominator = knotRight - knotLeft;
                var alpha = denominator > 0f ? (time - knotLeft) / denominator : 0f;
                values[j] = values[j - 1] + (values[j] - values[j - 1]) * alpha;
            }
        }

        return values[degree];

        // Knot of the full clamped vector at the given index: the first two entries
        // repeat Knots[0], indices 2..knotCount map to Knots[1..], the tail repeats
        // the last knot.
        float KnotAt(int index)
        {
            if (index < 2)
            {
                return Knots[0];
            }

            return index <= knotCount ? Knots[index - 1] : Knots[knotCount - 1];
        }
    }

}
