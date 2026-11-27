#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Terrain;

/// <summary>
/// Planar extent of one generated-bridge corridor in route coordinates: the graded
/// lateral band between the two approach toes, plus the distance over which the tyre
/// pose blends back to legacy beyond every edge. The pose is a render-only tyre fit;
/// the corridor never changes movement or collision.
/// </summary>
public sealed record BridgeCorridor
{
    public BridgeRoute Route { get; }

    public double LateralMin { get; }

    public double LateralMax { get; }

    public double ToeStartS { get; }

    public double ToeEndS { get; }

    public double BlendDistance { get; }

    public BridgeCorridor(BridgeRoute route, double lateralMin, double lateralMax, double toeStartS, double toeEndS, double blendDistance)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lateralMin, lateralMax);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(toeStartS, toeEndS);
        ArgumentOutOfRangeException.ThrowIfLessThan(blendDistance, 0);

        if (!route.IsValid)
        {
            throw new ArgumentException("Route must be a non-degenerate bridge route.", nameof(route));
        }

        Route = route;
        LateralMin = lateralMin;
        LateralMax = lateralMax;
        ToeStartS = toeStartS;
        ToeEndS = toeEndS;
        BlendDistance = blendDistance;
    }

    /// <summary>
    /// Weight in [0, 1] for one vehicle's XY sample points (its tyre contact points):
    /// 1 while the sample set's extent overlaps the corridor in both axes, otherwise
    /// falling off over BlendDistance with the gap between the set's extent and the
    /// band in each axis, measured from the nearest sample. Returns 0 for no samples.
    /// </summary>
    public float PoseWeight(ReadOnlySpan<Vector2> samples)
    {
        if (samples.IsEmpty)
        {
            return 0f;
        }

        var minS = double.MaxValue;
        var maxS = double.MinValue;
        var minN = double.MaxValue;
        var maxN = double.MinValue;

        foreach (var sample in samples)
        {
            var (s, n) = Route.Project(sample);
            minS = Math.Min(minS, s);
            maxS = Math.Max(maxS, s);
            minN = Math.Min(minN, n);
            maxN = Math.Max(maxN, n);
        }

        // Gap between the sample set's extent and the band on each axis, 0 while they
        // overlap. The nearest sample decides on both axes, so the weight falls off
        // continuously as individual tyres leave the band.
        var dS = ExtentGap(minS, maxS, ToeStartS, ToeEndS);
        var dN = ExtentGap(minN, maxN, LateralMin, LateralMax);

        return (float)(SmoothStep(EdgeFactor(dS)) * SmoothStep(EdgeFactor(dN)));
    }

    private double ExtentGap(double min, double max, double bandMin, double bandMax)
    {
        if (min <= bandMax && max >= bandMin)
        {
            return 0;
        }

        return min > bandMax ? min - bandMax : bandMin - max;
    }

    private double EdgeFactor(double distance)
    {
        if (BlendDistance <= 0)
        {
            return distance > 0 ? 0d : 1d;
        }

        return Math.Clamp(1 - distance / BlendDistance, 0d, 1d);
    }

    private static double SmoothStep(double t)
    {
        return t * t * (3 - 2 * t);
    }
}
