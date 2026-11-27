#nullable enable
using System;

namespace Panopticon.Terrain;

/// <summary>
/// One approach ramp of a generated-bridge corridor: a tangent-continuous ramp from a level
/// bench down or up to the native terrain at the toe, over the shortest lattice-step length
/// whose steepest grade stays within a design maximum, with the lateral cross-slope fading
/// from native at the toe to flat at the bench. The ramp meets the native terrain exactly at
/// the toe (TargetHeight returns the native height there whatever the centre line is).
/// </summary>
public sealed record GeneratedBridgeApproachProfile
{
    /// <summary>
    /// Steepest grade an approach may reach; the measured candidate reaches 0.1760.
    /// </summary>
    public const double MaximumGrade = 0.18d;

    /// <summary>
    /// Shortest approach, so flat terrain still gets a short ramp.
    /// </summary>
    public const double MinimumLength = 40d;

    /// <summary>
    /// Route coordinate where the ramp leaves the level bench.
    /// </summary>
    public required double BenchStartS { get; init; }

    /// <summary>
    /// Route coordinate of the toe.
    /// </summary>
    public required double ToeS { get; init; }

    /// <summary>
    /// Positive distance between the bench start and the toe.
    /// </summary>
    public required double Length { get; init; }

    /// <summary>
    /// Level bench height.
    /// </summary>
    public required double BenchHeight { get; init; }

    /// <summary>
    /// Native centre height at the toe.
    /// </summary>
    public required double ToeHeight { get; init; }

    /// <summary>
    /// Native slope per unit of outward distance.
    /// </summary>
    public required double ToeSlope { get; init; }

    /// <summary>
    /// Steepest grade of the design centre line.
    /// </summary>
    public required double DesignMaxGrade { get; init; }

    /// <summary>
    /// True when the toe lies at smaller route coordinates than the bench.
    /// </summary>
    public required bool OutwardIsNegativeS { get; init; }

    /// <summary>
    /// Outward distance from the bench start, clamped to the ramp.
    /// </summary>
    public double OutwardDistance(double s)
    {
        var outward = OutwardIsNegativeS ? BenchStartS - s : s - BenchStartS;

        return Math.Clamp(outward, 0d, Length);
    }

    /// <summary>
    /// Design centre-line height: Hermite blend with slope 0 at the bench and ToeSlope at the toe.
    /// </summary>
    public double DesignHeight(double s)
    {
        var t = Math.Clamp(OutwardDistance(s) / Length, 0d, 1d);
        var heightChange = ToeHeight - BenchHeight;
        var t2 = t * t;
        var t3 = t2 * t;

        return BenchHeight + (heightChange * ((-2d * t3) + (3d * t2))) + (Length * ToeSlope * (t3 - t2));
    }

    /// <summary>
    /// Lateral blend weight: 1 at the bench (flat) and 0 at the toe (native cross-profile).
    /// </summary>
    public double CrossBlendWeight(double s)
    {
        var t = Math.Clamp(OutwardDistance(s) / Length, 0d, 1d);
        var x = 1d - t;

        return x * x * (3d - (2d * x));
    }

    /// <summary>
    /// Graded height: design height plus the faded native cross offset.
    /// </summary>
    public double TargetHeight(double s, double nativeHeight, double nativeCentreHeight)
    {
        return DesignHeight(s) + ((1d - CrossBlendWeight(s)) * (nativeHeight - nativeCentreHeight));
    }

    /// <summary>
    /// Maximum absolute design grade sampled at 10001 points.
    /// </summary>
    public static double MaximumGradeOf(double length, double heightChange, double toeSlope)
    {
        var maximum = 0d;

        for (var k = 0; k <= 10000; k++)
        {
            var t = k / 10000d;
            var grade = Math.Abs((heightChange * ((6d * t) - (6d * t * t)) / length) + (toeSlope * ((3d * t * t) - (2d * t))));

            if (grade > maximum)
            {
                maximum = grade;
            }
        }

        return maximum;
    }

    /// <summary>
    /// Shortest lattice-step length whose design grade fits MaximumGrade, or null with a reason.
    /// </summary>
    public static GeneratedBridgeApproachProfile? TryCreate(double benchStartS, bool outwardIsNegativeS, double benchHeight, Func<double, double> nativeCentreHeight, double latticeStep, double maximumLength, out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(nativeCentreHeight);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(latticeStep, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 0);

        unsupportedReason = null;

        if (maximumLength < MinimumLength)
        {
            unsupportedReason = $"no-room-for-approach (maximum {maximumLength} below minimum {MinimumLength})";
            return null;
        }

        var minMultiple = (int)Math.Ceiling((MinimumLength / latticeStep) - 1e-9);
        var maxMultiple = (int)Math.Floor((maximumLength / latticeStep) + 1e-9);

        for (var multiple = minMultiple; multiple <= maxMultiple; multiple++)
        {
            var length = multiple * latticeStep;
            var toeS = outwardIsNegativeS ? benchStartS - length : benchStartS + length;
            var toeHeight = nativeCentreHeight(toeS);
            var toeSlope = MeasureToeSlope(benchStartS, toeS, outwardIsNegativeS, nativeCentreHeight, latticeStep, length);
            var designMaxGrade = MaximumGradeOf(length, toeHeight - benchHeight, toeSlope);

            if (designMaxGrade <= MaximumGrade)
            {
                return new GeneratedBridgeApproachProfile
                {
                    BenchStartS = benchStartS,
                    ToeS = toeS,
                    Length = length,
                    BenchHeight = benchHeight,
                    ToeHeight = toeHeight,
                    ToeSlope = toeSlope,
                    DesignMaxGrade = designMaxGrade,
                    OutwardIsNegativeS = outwardIsNegativeS,
                };
            }
        }

        unsupportedReason = $"no-length-meets-grade (maximum grade {MaximumGrade} in {maximumLength})";
        return null;
    }

    private static double MeasureToeSlope(double benchStartS, double toeS, bool outwardIsNegativeS, Func<double, double> nativeCentreHeight, double latticeStep, double length)
    {
        var steps = (int)Math.Round(length / latticeStep);
        var sampleCount = Math.Max(steps + 1, 3);
        var useLatticeStep = steps + 1 >= 3;
        var direction = outwardIsNegativeS ? -1d : 1d;
        var sumX = 0d;
        var sumY = 0d;
        var sumXY = 0d;
        var sumXX = 0d;

        for (var i = 0; i < sampleCount; i++)
        {
            var x = useLatticeStep ? benchStartS + (direction * latticeStep * i) : benchStartS + ((toeS - benchStartS) * i / (sampleCount - 1));
            var y = nativeCentreHeight(x);
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        var denominator = (sampleCount * sumXX) - (sumX * sumX);

        if (denominator == 0d)
        {
            return 0d;
        }

        var routeSlope = ((sampleCount * sumXY) - (sumX * sumY)) / denominator;

        return outwardIsNegativeS ? -routeSlope : routeSlope;
    }
}
