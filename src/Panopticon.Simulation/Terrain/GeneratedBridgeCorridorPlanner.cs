#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Terrain;

/// <summary>
/// The corridor plan for one generated bridge, computed from original terrain heights; the deck ends
/// sit on rows one elevation notch below the deck, the benches are level at the datum, the approaches meet the native
/// terrain exactly at their toes, and the terrain under the deck is lowered to keep clearance; targets
/// are floored to the elevation grid so the clearance bound still holds after quantization. The corridor
/// is anchored on the bridge's endpoints, the ends of the deck model.
/// </summary>
internal static class GeneratedBridgeCorridorPlanner
{
    /// <summary>
    /// Level bench between a deck end and its approach.
    /// </summary>
    public const double BenchLength = 40d;

    /// <summary>
    /// Clearance kept below the datum under the deck.
    /// </summary>
    public const double UnderDeckClearance = 0.98d;

    /// <summary>
    /// The first under-deck row inward of each flush pair: the lattice interpolates in a straight
    /// line between the flush row and this row over one lattice step, so this row must be deep
    /// enough that <see cref="UnderDeckClearance"/> holds by 18 units inward of the deck end
    /// (measured on the supported model: 0.98 gives 19.7 units, 1.30 gives 17.4).
    /// </summary>
    public const double FirstCutRowClearance = 1.30d;

    /// <summary>
    /// Clearance kept below the roadway plane on flush rows. The datum lies on the elevation grid, so an
    /// un-margined flush target stores the deck height itself wherever the roadway plane rounds onto it;
    /// the value is small enough to stay within one elevation notch and large enough that flooring never
    /// lands on the deck.
    /// </summary>
    public const double FlushSeamClearance = 0.01d;

    /// <summary>
    /// The measured continuous blend width of the tyre pose.
    /// </summary>
    public const double PoseBlendDistance = 40d;

    /// <summary>
    /// The smallest feather width at which all four tyres stay within 0.5 of support across both
    /// lateral edges of the measured embankment, with the rendered tilt at or below 28.5 degrees
    /// (one to three columns leave 2 to 79 readings outside; none leaves 79 and 68).
    /// </summary>
    public const int ShoulderColumns = 4;

    /// <summary>
    /// Mean endpoint height floored to the elevation grid.
    /// </summary>
    public static double Datum(HeightMap heightMap, in Vector2 start, in Vector2 end)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        var mean = ((double)heightMap.GetHeight(start.X, start.Y) + heightMap.GetHeight(end.X, end.Y)) / 2d;

        return Math.Floor(mean / BridgeLandingBand.ElevationStep) * BridgeLandingBand.ElevationStep;
    }

    /// <summary>
    /// Planned corridor from original heights, or null with a reason when unsupported.
    /// </summary>
    public static GeneratedBridgeCorridorPlan? TryPlan(HeightMap heightMap, GeneratedBridgeLayout layout, out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(heightMap);
        ArgumentNullException.ThrowIfNull(layout);

        unsupportedReason = null;

        if (Math.Abs(heightMap.VerticalScale - BridgeLandingBand.ElevationStep) > 1e-9)
        {
            unsupportedReason = $"height-map-is-not-on-16-bit-grid (vertical scale {heightMap.VerticalScale} differs from {BridgeLandingBand.ElevationStep})";
            return null;
        }

        var step = (double)HeightMap.HorizontalScale;
        var h = step / 2d;
        var route = layout.Route;
        var s0 = 0d;
        var s1 = route.Length;
        var datum = layout.DatumZ;
        var lateralMin = layout.RoadwayMinN - step;
        var lateralMax = layout.RoadwayMaxN + step;
        var benchStartS = s0 - BenchLength;
        var benchEndS = s1 + BenchLength;

        Func<double, double> nativeCentre = s => NativeCentreHeight(heightMap, route, s);

        var roomStart = RoomFor(route, benchStartS, true, lateralMin, lateralMax, step, heightMap.MaxXCoordinate, heightMap.MaxYCoordinate);
        var approachStart = GeneratedBridgeApproachProfile.TryCreate(benchStartS, true, datum, nativeCentre, step, roomStart, out var reasonStart);

        if (approachStart == null)
        {
            unsupportedReason = reasonStart;
            return null;
        }

        var roomEnd = RoomFor(route, benchEndS, false, lateralMin, lateralMax, step, heightMap.MaxXCoordinate, heightMap.MaxYCoordinate);
        var approachEnd = GeneratedBridgeApproachProfile.TryCreate(benchEndS, false, datum, nativeCentre, step, roomEnd, out var reasonEnd);

        if (approachEnd == null)
        {
            unsupportedReason = reasonEnd;
            return null;
        }

        var context = new CorridorContext(datum, layout.Roadway, approachStart, approachEnd, nativeCentre, s0, s1, step, h);
        var targets = new List<GeneratedBridgeCorridorTarget>();
        var westEdges = new Dictionary<int, (double N, double Target)>();
        var eastEdges = new Dictionary<int, (double N, double Target)>();

        for (var i = 0; i < heightMap.Width; i++)
        {
            for (var j = 0; j < heightMap.Height; j++)
            {
                var position = heightMap.GetPosition(i, j);
                var (s, n) = route.Project(position.X, position.Y);

                if (n < lateralMin || n > lateralMax)
                {
                    continue;
                }

                var native = (double)heightMap.GetHeight(i, j);

                if (!TryCorridorTarget(context, s, position.X, position.Y, native, out var height, out var region))
                {
                    continue;
                }

                targets.Add(new GeneratedBridgeCorridorTarget(i, j, height, region));

                if (!westEdges.TryGetValue(j, out var west) || n < west.N)
                {
                    westEdges[j] = (n, height);
                }

                if (!eastEdges.TryGetValue(j, out var east) || n > east.N)
                {
                    eastEdges[j] = (n, height);
                }
            }
        }

        var shoulderReach = (ShoulderColumns * step) + h;
        var toeStartS = approachStart.ToeS;
        var toeEndS = approachEnd.ToeS;

        for (var i = 0; i < heightMap.Width; i++)
        {
            for (var j = 0; j < heightMap.Height; j++)
            {
                var position = heightMap.GetPosition(i, j);
                var (s, n) = route.Project(position.X, position.Y);

                if (n >= lateralMin && n <= lateralMax)
                {
                    continue;
                }

                var beyond = n < lateralMin ? lateralMin - n : n - lateralMax;

                if (beyond <= 0d || beyond > shoulderReach)
                {
                    continue;
                }

                if (s < toeStartS - h || s > toeEndS + h)
                {
                    continue;
                }

                var west = n < lateralMin;
                var edges = west ? westEdges : eastEdges;

                if (!edges.TryGetValue(j, out var edge))
                {
                    continue;
                }

                var m = (int)Math.Round(beyond / step, MidpointRounding.AwayFromZero);

                if (m < 1)
                {
                    m = 1;
                }

                if (m > ShoulderColumns)
                {
                    m = ShoulderColumns;
                }

                var weight = 1d - (m / (double)(ShoulderColumns + 1));
                var native = (double)heightMap.GetHeight(i, j);
                var height = native + (weight * (edge.Target - native));
                targets.Add(new GeneratedBridgeCorridorTarget(i, j, height, GeneratedBridgeCorridorRegion.Shoulder));
            }
        }

        var maxHeight = 65535d * BridgeLandingBand.ElevationStep;

        foreach (var target in targets)
        {
            if (!double.IsFinite(target.Height) || target.Height < 0d || target.Height > maxHeight)
            {
                unsupportedReason = $"target-height-outside-16-bit-range ({target.Height} at ({target.X},{target.Y}))";
                return null;
            }
        }

        return new GeneratedBridgeCorridorPlan
        {
            Route = route,
            DatumZ = datum,
            LateralMin = lateralMin,
            LateralMax = lateralMax,
            PoseLateralMin = lateralMin - (ShoulderColumns * step),
            PoseLateralMax = lateralMax + (ShoulderColumns * step),
            ToeStartS = approachStart.ToeS,
            ToeEndS = approachEnd.ToeS,
            ApproachStart = approachStart,
            ApproachEnd = approachEnd,
            Targets = targets,
        };
    }

    /// <summary>
    /// Writes floored targets once; earlier plans win shared vertices. No normals refresh.
    /// </summary>
    public static (int MinX, int MinY, int MaxX, int MaxY, int Changed) Apply(HeightMap heightMap, IReadOnlyList<GeneratedBridgeCorridorPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(heightMap);
        ArgumentNullException.ThrowIfNull(plans);

        var written = new HashSet<(int X, int Y)>();
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var changed = 0;

        foreach (var plan in plans)
        {
            foreach (var target in plan.Targets)
            {
                if (written.Contains((target.X, target.Y)))
                {
                    continue;
                }

                var wanted = (int)Math.Floor(target.Height / BridgeLandingBand.ElevationStep);
                var stored = (int)Math.Round((double)heightMap.GetHeight(target.X, target.Y) / heightMap.VerticalScale);

                if (wanted == stored)
                {
                    continue;
                }

                heightMap.SetElevation(target.X, target.Y, wanted);
                written.Add((target.X, target.Y));
                minX = Math.Min(minX, target.X);
                maxX = Math.Max(maxX, target.X);
                minY = Math.Min(minY, target.Y);
                maxY = Math.Max(maxY, target.Y);
                changed++;
            }
        }

        return (minX, minY, maxX, maxY, changed);
    }

    private sealed record CorridorContext(double Datum, IReadOnlyList<BridgeSurface.BridgeRawFace> Roadway, GeneratedBridgeApproachProfile ApproachStart, GeneratedBridgeApproachProfile ApproachEnd, Func<double, double> NativeCentre, double S0, double S1, double Step, double H);

    private static bool TryCorridorTarget(CorridorContext context, double s, float worldX, float worldY, double native, out double target, out GeneratedBridgeCorridorRegion region)
    {
        var s0 = context.S0;
        var s1 = context.S1;
        var step = context.Step;
        var h = context.H;

        if ((s >= s0 - h && s < s0 + step + h) || (s >= s1 - step - h && s < s1 + h))
        {
            var min = double.PositiveInfinity;

            foreach (var face in context.Roadway)
            {
                var z = (double)face.PlaneZ(worldX, worldY);

                if (z < min)
                {
                    min = z;
                }
            }

            target = min - FlushSeamClearance;
            region = GeneratedBridgeCorridorRegion.Flush;

            return true;
        }

        if ((s >= s0 - BenchLength - h && s < s0 - h) || (s > s1 + h && s <= s1 + BenchLength + h))
        {
            target = context.Datum;
            region = GeneratedBridgeCorridorRegion.Bench;

            return true;
        }

        if (s >= context.ApproachStart.ToeS - h && s < s0 - BenchLength - h)
        {
            target = context.ApproachStart.TargetHeight(s, native, context.NativeCentre(s));
            region = GeneratedBridgeCorridorRegion.Approach;

            return true;
        }

        if (s > s1 + BenchLength + h && s <= context.ApproachEnd.ToeS + h)
        {
            target = context.ApproachEnd.TargetHeight(s, native, context.NativeCentre(s));
            region = GeneratedBridgeCorridorRegion.Approach;

            return true;
        }

        if (s >= s0 + step + h && s < s1 - step - h)
        {
            var clearance = s < s0 + step + h + step || s >= s1 - step - h - step
                ? FirstCutRowClearance
                : UnderDeckClearance;
            target = Math.Min(native, context.Datum - clearance);
            region = GeneratedBridgeCorridorRegion.UnderDeckCut;

            return true;
        }

        target = 0d;
        region = GeneratedBridgeCorridorRegion.Flush;

        return false;
    }

    private static double NativeCentreHeight(HeightMap heightMap, BridgeRoute route, double s)
    {
        var world = route.ToWorld(s, 0d, 0d);

        return heightMap.GetHeight(world.X, world.Y);
    }

    private static bool InBounds(float x, float y, int maxX, int maxY) => x >= 0f && x <= maxX && y >= 0f && y <= maxY;

    private static double RoomFor(BridgeRoute route, double benchStartS, bool outwardIsNegativeS, double lateralMin, double lateralMax, double step, int maxX, int maxY)
    {
        var diagonal = Math.Sqrt((double)maxX * maxX + (double)maxY * maxY);
        var maxMultiple = (int)Math.Ceiling(diagonal / step);

        for (var multiple = maxMultiple; multiple >= 0; multiple--)
        {
            var length = multiple * step;
            var toeS = outwardIsNegativeS ? benchStartS - length : benchStartS + length;
            var cornerMin = route.ToWorld(toeS, lateralMin, 0d);
            var cornerMax = route.ToWorld(toeS, lateralMax, 0d);

            if (InBounds(cornerMin.X, cornerMin.Y, maxX, maxY) && InBounds(cornerMax.X, cornerMax.Y, maxX, maxY))
            {
                return length;
            }
        }

        return 0d;
    }
}
