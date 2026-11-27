#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Terrain;

/// <summary>
/// The landing band rule shared by the package converter and the runtime bridge path
/// fallback: cells within <see cref="RampDistance"/> outward of a bridge endpoint and
/// within <see cref="CrossWidth"/> of the bridge axis blend from their height toward the
/// deck target with a smoothstep curve, clamped between the current height and the deck
/// height; the endpoint cell keeps its height because the runtime bridge anchors there.
/// Deck targets come from pre-grading heights, which holds because <see cref="Plan"/>
/// only reads through its sampler; writing through <see cref="ToElevation"/> is what
/// makes the runtime fallback bit-identical to a package the converter graded.
/// </summary>
public static class BridgeLandingBand
{
    /// <summary>Ramp length outward from each bridge endpoint along the bridge axis, in game units.</summary>
    public const double RampDistance = 40d;

    /// <summary>Half-width of the graded band either side of the bridge axis, in game units.</summary>
    public const double CrossWidth = 30d;

    /// <summary>Measured road-bed offset above terrain at a bridge line endpoint (G7 witness data).</summary>
    public const double ApproachDeckOffset = 6.5d;

    /// <summary>The ramp tops out one game unit below the deck so vehicles step up onto it.</summary>
    public const double RampTopClearance = 1d;

    /// <summary>The resident 16-bit height grid step, in game height units per elevation.</summary>
    public const double ElevationStep = 0.0390625d;

    private const double LowestQuotient = -0.5d;

    private const double FirstRejectedQuotient = 65535.5d;

    /// <summary>
    /// One lattice cell and the height the band plans for it; lattice indices are
    /// border-less (index 0 is world coordinate 0).
    /// </summary>
    public readonly record struct Target(int X, int Y, double Height);

    public static IReadOnlyList<Target> Plan(
        Func<int, int, double> sampleHeight,
        int latticeWidth,
        int latticeHeight,
        double sampleSpacing,
        IReadOnlyList<(double StartX, double StartY, double EndX, double EndY)> lines)
    {
        ArgumentNullException.ThrowIfNull(sampleHeight);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(latticeWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(latticeHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleSpacing, 0d);

        var targets = new List<Target>();

        foreach (var (startX, startY, endX, endY) in lines)
        {
            if (startX == endX && startY == endY)
            {
                continue;
            }

            // Both deck targets come from the pre-grading heights (Plan only reads), so
            // landings closer than two ramp distances apart grade toward stable heights.
            var startDeckZ = SampleBilinear(sampleHeight, latticeWidth, latticeHeight, sampleSpacing, startX, startY) + ApproachDeckOffset;
            var endDeckZ = SampleBilinear(sampleHeight, latticeWidth, latticeHeight, sampleSpacing, endX, endY) + ApproachDeckOffset;

            PlanLanding(targets, sampleHeight, latticeWidth, latticeHeight, sampleSpacing, startX, startY, endX, endY, startDeckZ);
            PlanLanding(targets, sampleHeight, latticeWidth, latticeHeight, sampleSpacing, endX, endY, startX, startY, endDeckZ);
        }

        return targets;
    }

    public static int? ToElevation(double height)
    {
        var quotient = height / ElevationStep;

        if (!double.IsFinite(quotient) || quotient < LowestQuotient || quotient >= FirstRejectedQuotient)
        {
            return null;
        }

        return (int)Math.Round(quotient, MidpointRounding.ToEven);
    }

    // Literal port of the converter's former GradeLanding: the caller applies targets in
    // return order, so a later landing's target for a shared cell reproduces the
    // converter's last-write-wins.
    private static void PlanLanding(
        List<Target> targets,
        Func<int, int, double> sampleHeight,
        int latticeWidth,
        int latticeHeight,
        double sampleSpacing,
        double endpointX,
        double endpointY,
        double inwardX,
        double inwardY,
        double deckZ)
    {
        var outwardX = endpointX - inwardX;
        var outwardY = endpointY - inwardY;
        var length = Math.Sqrt((outwardX * outwardX) + (outwardY * outwardY));

        if (length <= 0d)
        {
            return;
        }

        var axisX = outwardX / length;
        var axisY = outwardY / length;

        // Any graded cell lies within sqrt(ramp^2 + crossWidth^2) of the endpoint.
        var centerLatticeX = (int)Math.Round(endpointX / sampleSpacing);
        var centerLatticeY = (int)Math.Round(endpointY / sampleSpacing);
        var window = (int)Math.Ceiling(Math.Sqrt((RampDistance * RampDistance) + (CrossWidth * CrossWidth)) / sampleSpacing);

        for (var latticeY = Math.Max(0, centerLatticeY - window); latticeY <= Math.Min(latticeHeight - 1, centerLatticeY + window); latticeY++)
        {
            for (var latticeX = Math.Max(0, centerLatticeX - window); latticeX <= Math.Min(latticeWidth - 1, centerLatticeX + window); latticeX++)
            {
                var x = latticeX * sampleSpacing;
                var y = latticeY * sampleSpacing;
                var along = ((x - endpointX) * axisX) + ((y - endpointY) * axisY);

                // Strictly outward of the endpoint: the endpoint cell keeps its height (the
                // runtime bridge anchors there), inward cells stay under the bridge.
                if (along <= 0d || along > RampDistance)
                {
                    continue;
                }

                var across = Math.Abs(((x - endpointX) * -axisY) + ((y - endpointY) * axisX));

                if (across > CrossWidth)
                {
                    continue;
                }

                var current = sampleHeight(latticeX, latticeY);
                var rampT = 1d - (along / RampDistance);
                var weight = (rampT * rampT) * (3d - (2d * rampT));
                var target = deckZ - RampTopClearance;
                var graded = current + ((target - current) * weight);

                graded = Math.Clamp(graded, Math.Min(current, deckZ), Math.Max(current, deckZ));
                targets.Add(new Target(latticeX, latticeY, graded));
            }
        }
    }

    // Literal port of the converter's former SampleLatticeHeightBilinear.
    private static double SampleBilinear(
        Func<int, int, double> sampleHeight,
        int latticeWidth,
        int latticeHeight,
        double sampleSpacing,
        double x,
        double y)
    {
        var latticeX = x / sampleSpacing;
        var latticeY = y / sampleSpacing;
        var x0 = Math.Clamp((int)Math.Floor(latticeX), 0, latticeWidth - 1);
        var y0 = Math.Clamp((int)Math.Floor(latticeY), 0, latticeHeight - 1);
        var x1 = Math.Min(x0 + 1, latticeWidth - 1);
        var y1 = Math.Min(y0 + 1, latticeHeight - 1);
        var fractionX = Math.Clamp(latticeX - x0, 0d, 1d);
        var fractionY = Math.Clamp(latticeY - y0, 0d, 1d);

        var h00 = sampleHeight(x0, y0);
        var h10 = sampleHeight(x1, y0);
        var h01 = sampleHeight(x0, y1);
        var h11 = sampleHeight(x1, y1);

        var low = (h10 * fractionX) + (h00 * (1d - fractionX));
        var high = (h11 * fractionX) + (h01 * (1d - fractionX));

        return (high * fractionY) + (low * (1d - fractionY));
    }
}
