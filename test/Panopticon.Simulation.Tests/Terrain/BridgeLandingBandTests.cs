#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Portable spec tests for the shared landing band rule: a 33 x 33 lattice with spacing
/// 10 over the linear plane height = 100 + 0.5x + 0.25y, so every planned height is
/// recomputable in the test with plain arithmetic.
/// </summary>
public sealed class BridgeLandingBandTests
{
    private const int LatticeWidth = 33;

    private const int LatticeHeight = 33;

    private const double Spacing = 10d;

    private static double SamplePlane(int x, int y)
    {
        return 100d + (0.5d * x) + (0.25d * y);
    }

    [Fact]
    public void Plan_SingleLine_TargetsStrictlyOutward_WithSmoothstepHeights()
    {
        // Line (50,50) to (150,50) in world units: start landing plans left of lattice
        // x 5, end landing right of lattice x 15, both within cross-width rows 2..8.
        var targets = BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, Spacing, [(50d, 50d, 150d, 50d)]);

        var startDeck = Plane(5, 5) + BridgeLandingBand.ApproachDeckOffset;
        var endDeck = Plane(15, 5) + BridgeLandingBand.ApproachDeckOffset;
        var expected = new List<BridgeLandingBand.Target>();
        expected.AddRange(ExpectedLanding(5, 5, 15, 5, startDeck));
        expected.AddRange(ExpectedLanding(15, 5, 5, 5, endDeck));

        Assert.Equal(expected.Count, targets.Count);

        // The endpoint cells anchor the runtime bridge and must have no target.
        Assert.DoesNotContain(targets, target => target.X == 5 && target.Y == 5);
        Assert.DoesNotContain(targets, target => target.X == 15 && target.Y == 5);

        // Same cells in the same order, each with the exact smoothstep blend height.
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].X, targets[index].X);
            Assert.Equal(expected[index].Y, targets[index].Y);
            Assert.Equal(expected[index].Height, targets[index].Height);
        }

        // Start landing first: every target left of the line midpoint precedes every
        // target right of it.
        var lastStart = -1;
        var firstEnd = targets.Count;

        for (var index = 0; index < targets.Count; index++)
        {
            if (targets[index].X <= 14)
            {
                lastStart = Math.Max(lastStart, index);
            }
            else
            {
                firstEnd = Math.Min(firstEnd, index);
            }
        }

        Assert.True(lastStart < firstEnd);
    }

    [Fact]
    public void Plan_DegenerateLine_AndEmptyLineList_ProduceNoTargets()
    {
        Assert.Empty(BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, Spacing, [(50d, 50d, 50d, 50d)]));
        Assert.Empty(BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, Spacing, []));
    }

    [Fact]
    public void Plan_OverlappingLandings_LaterLineTargetForSharedCellComesLast()
    {
        // The second line runs the opposite way: its start landing covers exactly the
        // first line's end landing cells, with the same deck target (Plan only reads, so
        // both decks come from pre-grading heights).
        var targets = BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, Spacing, [(50d, 50d, 150d, 50d), (150d, 50d, 50d, 50d)]).ToList();

        var endDeck = Plane(15, 5) + BridgeLandingBand.ApproachDeckOffset;
        var shared = ExpectedLanding(15, 5, 5, 5, endDeck);

        foreach (var cell in shared)
        {
            var first = targets.IndexOf(cell);
            var last = targets.LastIndexOf(cell);

            Assert.True(first >= 0);
            Assert.True(last > first);
            Assert.Equal(cell.Height, targets[first].Height);
            Assert.Equal(cell.Height, targets[last].Height);

            // The first occurrence is the first line's end landing; the later one is the
            // second line's start landing, so it lands in the tail of the list.
            Assert.True(first < targets.Count / 2);
            Assert.True(last >= targets.Count / 2);
        }
    }

    [Fact]
    public void ToElevation_QuantizesWithRoundToEven_AndRejectsOffGridValues()
    {
        Assert.Equal(0, BridgeLandingBand.ToElevation(0d));
        Assert.Equal(1, BridgeLandingBand.ToElevation(BridgeLandingBand.ElevationStep));
        Assert.Equal(0, BridgeLandingBand.ToElevation(0.5d * BridgeLandingBand.ElevationStep));
        Assert.Equal(2, BridgeLandingBand.ToElevation(1.5d * BridgeLandingBand.ElevationStep));
        Assert.Equal(0, BridgeLandingBand.ToElevation(-0.01d));
        Assert.Null(BridgeLandingBand.ToElevation(-0.3d));
        Assert.Null(BridgeLandingBand.ToElevation(65535.5d * BridgeLandingBand.ElevationStep));
        Assert.Null(BridgeLandingBand.ToElevation(double.NaN));
    }

    [Fact]
    public void Plan_ValidatesSamplerLinesWidthHeightAndSpacing()
    {
        Assert.Throws<ArgumentNullException>(() => BridgeLandingBand.Plan(
            null!, LatticeWidth, LatticeHeight, Spacing, []));
        Assert.Throws<ArgumentNullException>(() => BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, Spacing, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeLandingBand.Plan(
            SamplePlane, 0, LatticeHeight, Spacing, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, 0, Spacing, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeLandingBand.Plan(
            SamplePlane, LatticeWidth, LatticeHeight, 0d, []));
    }

    private static double Plane(int x, int y)
    {
        return 100d + (0.5d * x) + (0.25d * y);
    }

    /// <summary>
    /// Spec mirror of one landing: smoothstep blend from the plane height toward
    /// (deck - 1) over 40 world units outward along the axis, clamped into the
    /// [current, deck] band, cross-width ±30, endpoint cell untouched.
    /// </summary>
    private static IReadOnlyList<BridgeLandingBand.Target> ExpectedLanding(int endpointX, int endpointY, int inwardX, int inwardY, double deckZ)
    {
        var targets = new List<BridgeLandingBand.Target>();
        var endpointWorldX = endpointX * Spacing;
        var endpointWorldY = endpointY * Spacing;
        var outwardX = (endpointX - inwardX) * Spacing;
        var outwardY = (endpointY - inwardY) * Spacing;
        var length = Math.Sqrt((outwardX * outwardX) + (outwardY * outwardY));
        var axisX = outwardX / length;
        var axisY = outwardY / length;
        var window = (int)Math.Ceiling(Math.Sqrt((BridgeLandingBand.RampDistance * BridgeLandingBand.RampDistance) + (BridgeLandingBand.CrossWidth * BridgeLandingBand.CrossWidth)) / Spacing);

        for (var y = Math.Max(0, endpointY - window); y <= Math.Min(LatticeHeight - 1, endpointY + window); y++)
        {
            for (var x = Math.Max(0, endpointX - window); x <= Math.Min(LatticeWidth - 1, endpointX + window); x++)
            {
                var worldX = x * Spacing;
                var worldY = y * Spacing;
                var along = ((worldX - endpointWorldX) * axisX) + ((worldY - endpointWorldY) * axisY);

                if (along <= 0d || along > BridgeLandingBand.RampDistance)
                {
                    continue;
                }

                var across = Math.Abs(((worldX - endpointWorldX) * -axisY) + ((worldY - endpointWorldY) * axisX));

                if (across > BridgeLandingBand.CrossWidth)
                {
                    continue;
                }

                var current = Plane(x, y);
                var rampT = 1d - (along / BridgeLandingBand.RampDistance);
                var weight = (rampT * rampT) * (3d - (2d * rampT));
                var target = deckZ - BridgeLandingBand.RampTopClearance;
                var graded = current + ((target - current) * weight);
                graded = Math.Clamp(graded, Math.Min(current, deckZ), Math.Max(current, deckZ));

                targets.Add(new BridgeLandingBand.Target(x, y, graded));
            }
        }

        return targets;
    }
}
