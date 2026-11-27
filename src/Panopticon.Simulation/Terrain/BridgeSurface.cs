#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Panopticon.Content.Importing;
using Panopticon.Graphics;
using Panopticon.Mathematics;

namespace Panopticon.Terrain;

/// <summary>
/// Drivable roadway surface of one engine bridge. Extracted from the bridge model's
/// CPU triangle data after Scene3D builds the Bridge; TerrainLogic answers ground-height
/// queries with the surface Z for XY points inside the roadway extent, so ground vehicles
/// ride the deck instead of falling through generated-map gorge terrain.
/// </summary>
public sealed class BridgeSurface
{
    /// <summary>
    /// Roadway band floor around the interpolated anchor line: the concrete roadway is
    /// cambered along the arch and can sit around ten units below the line near landings
    /// (measured roadway relZ −10 at a span quarter point).
    /// </summary>
    public const float RoadwayBandMin = -15f;

    /// <summary>
    /// Roadway band ceiling for a flat span (no endpoint rise): the measured deck sits
    /// anchor + 5.5 with room for cross-slope and abutment geometry.
    /// </summary>
    public const float RoadwayBandBaseMax = 10f;

    /// <summary>
    /// The baked deck tilt adds a fraction of the endpoint rise on top of the slope
    /// rotation (measured: the roadway climbs ~1.5x the anchor-line rise over the span),
    /// so the band ceiling grows with the span rise to keep tracking the deck.
    /// </summary>
    public const float RoadwayBandRiseScale = 1.5f;

    /// <summary>
    /// Face-normal Z magnitude above which a triangle counts as horizontal (roadway, not
    /// walls). The magnitude works in either orientation because the bridge bones mirror
    /// the model, which flips authored normals; rendering draws the roadway two-sided.
    /// </summary>
    public const float HorizontalNormalZ = 0.5f;

    /// <summary>XY bucket edge for containment candidate lookup, in game units.</summary>
    private const float BucketSize = 20f;

    /// <summary>
    /// Runtime clearance cut depth (WP1029 remedy b): terrain under a generated bridge's
    /// roadway footprint is lowered to the fitted deck plane minus this depth, so the
    /// rendered ground can never bury the drivable deck.
    /// </summary>
    public const float ClearanceCutDepth = 1f;

    /// <summary>
    /// Safety margin added to the cut target so post-quantization in-footprint clearance
    /// stays at or above 0.98: LowerHeight truncates the stored elevation toward zero,
    /// which can only leave terrain lower than the target, so 1.0 - 0.02 is conservative.
    /// </summary>
    public const float ClearanceCutMargin = 0.02f;

    /// <summary>
    /// Width of the transition ring outside the roadway footprint where the cut target
    /// blends from plane-minus-depth back to unchanged via the smoothstep weight
    /// (t^2)(3-2t). Inside the footprint no blend applies.
    /// </summary>
    public const float ClearanceRingWidth = 10f;

    /// <summary>Minimum in-footprint deck clearance the acceptance gate requires.</summary>
    public const float MinimumDeckClearance = 0.98f;

    /// <summary>
    /// Shoulder feather: cut-only blend width (world units) beyond the RAISED lateral
    /// footprint edge, softening the single-cell bank where cut shoulder terrain meets
    /// uncut raised terrain. Never applied on the lowered side.
    /// </summary>
    public const float ApronLateralReach = 23f;

    /// <summary>Mouth apron longitudinal band half-extent per mouth (world units).</summary>
    public const float ApronLongitudinalExtent = 40f;

    /// <summary>
    /// Raised-side classification threshold: a lateral side counts as raised when the
    /// mean pre-cut terrain outside its edge exceeds the mean roadway edge height by
    /// more than this many world units (deterministic fixed stations/offset).
    /// </summary>
    public const float ShoulderRaisedThreshold = 0.5f;

    // Classification samples live OUTSIDE the maximum feather reach (never modified by
    // any application), so the raised-side classification is a pure function of roadway
    // geometry and never-modified terrain — stable across repeat applications and merge
    // orders.
    /// <summary>
    /// Apron classification samples sit beyond the apron lateral reach PLUS one full
    /// lattice stencil, so they read terrain never modified by any application — the
    /// classification is invariant across repeated loads, re-application, and merge
    /// order. If the sample points fall outside the usable lattice for a mouth side,
    /// that side stays unclassified (fail closed, no apron there).
    /// </summary>
    private const float ApronClassificationSampleOffset = ApronLateralReach + HeightMap.HorizontalScale;

    private const int ShoulderClassificationStationCount = 10;

    private readonly List<Triangle> _triangles;
    private readonly Vector2 _extentMin;
    private readonly Vector2 _extentMax;
    private readonly Dictionary<(int X, int Y), List<int>> _buckets;

    private BridgeSurface(
        List<Triangle> triangles,
        Vector2 extentMin,
        Vector2 extentMax,
        Dictionary<(int X, int Y), List<int>> buckets)
    {
        _triangles = triangles;
        _extentMin = extentMin;
        _extentMax = extentMax;
        _buckets = buckets;
    }

    public int TriangleCount => _triangles.Count;

    public Vector2 ExtentMin => _extentMin;

    public Vector2 ExtentMax => _extentMax;

    /// <summary>
    /// Extracts the roadway triangles from bridge mesh pieces transformed to world space.
    /// A triangle is kept when its smoothed vertex normal faces up and its centroid sits in
    /// the roadway band around the line between the two anchor positions. Returns null when
    /// no roadway triangle survives; every input must already be in model piece order.
    /// </summary>
    /// <summary>
    /// Measures the authored roadway pitch of a laid-out bridge at zero placement pitch
    /// (WP1029 rework part 2): replicates the Bridge.cs piece layout with no rotation
    /// around Y, extracts the roadway triangles with the same filter as the movement
    /// surface, and returns atan of the median per-triangle slope along the span axis
    /// (each triangle's longest edge projected onto the span axis). Deterministic; null
    /// when fewer than three finite slopes survive (no correction can be derived).
    /// Robustness: the median ignores outlier triangles (walls, pylons) that slip past
    /// the horizontal filter.
    /// </summary>
    public static float? MeasureIntrinsicGradeRadians(
        IReadOnlyList<(ModelSubObject SubObject, ImportedMesh Mesh)> pieces,
        Func<ModelSubObject, Matrix4x4> scaledBoneTransform,
        Vector3 anchorStart,
        Vector3 anchorEnd)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(scaledBoneTransform);

        if (pieces.Count < 3)
        {
            return null;
        }

        // Piece lengths from the bone-transformed render bounds, replicating Bridge.cs.
        var lengths = new float[pieces.Count];

        for (var index = 0; index < pieces.Count; index++)
        {
            var bounds = AxisAlignedBoundingBox.Transform(pieces[index].SubObject.RenderObject.BoundingBox, scaledBoneTransform(pieces[index].SubObject));
            lengths[index] = bounds.Max.X - bounds.Min.X;
        }

        if (lengths.Any(length => !float.IsFinite(length) || length <= 1f))
        {
            return null;
        }

        var distance = Vector3.Distance(anchorStart, anchorEnd);
        var lengthSpan = lengths[1];
        var spanLength = distance - lengths[0] - lengths[^1];

        if (lengthSpan <= 1f)
        {
            return null;
        }

        var numSpans = (int)(spanLength / lengthSpan);
        var totalLength = lengths[0] + (lengthSpan * numSpans) + lengths[^1];

        if (totalLength <= 1f)
        {
            return null;
        }

        var scaleX = distance / totalLength;

        // Axis alignment including the height difference: the minimal rotation from +X to
        // the anchor direction embeds the site pitch, exactly like Bridge.cs's
        // rotationAroundZ. The measurement happens AFTER this alignment and BEFORE the
        // Y-rotation, so it captures the roadway pitch the placement transform produces.
        var axisDirection = Vector3.Normalize(anchorEnd - anchorStart);

        if (axisDirection.Length() <= float.Epsilon || MathF.Abs(axisDirection.Y) <= float.Epsilon && MathF.Abs(axisDirection.Z) > 0.9f)
        {
            return null;
        }

        var alignmentAxis = Vector3.Cross(Vector3.UnitX, axisDirection);
        Quaternion rotationAroundZ;

        if (alignmentAxis == Vector3.Zero)
        {
            // +X already aligned (or opposed): pure yaw about Z from the planar heading.
            rotationAroundZ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(axisDirection.Y, axisDirection.X));
        }
        else
        {
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitX, axisDirection), -1f, 1f));
            rotationAroundZ = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(alignmentAxis), angle));
        }

        var spanAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, Matrix4x4.CreateFromQuaternion(rotationAroundZ)));
        var bandCeiling = 10f + (1.5f * (anchorEnd.Z - anchorStart.Z));
        var bandFloor = -15f;
        var worldZeroPitch = Matrix4x4.CreateScale(scaleX, 1, 1) * Matrix4x4.CreateFromQuaternion(rotationAroundZ) * Matrix4x4.CreateTranslation(anchorStart);

        var centroidPoints = new List<(float T, float Z)>();
        var currentOffset = 0f;

        for (var pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
        {
            var (subObject, mesh) = pieces[pieceIndex];
            var bone = scaledBoneTransform(subObject);

            // Layout contract: [0] = left landing (x1), [1] = span (repeated to fill), [^1] = right landing (x1).
            var copies = pieceIndex == 1 ? numSpans : 1;

            var localCorners = mesh.Vertices.Select(v => Vector3.Transform(v.Position, bone)).ToArray();
            var pieceMinX = localCorners.Min(c => c.X);

            for (var copy = 0; copy < copies; copy++)
            {
                var pieceMatrix =
                    bone *
                    Matrix4x4.CreateTranslation(new Vector3(currentOffset, 0, 0)) *
                    Matrix4x4.CreateTranslation(new Vector3(-pieceMinX, 0, 0)) *
                    worldZeroPitch;

                var verts = mesh.Vertices;
                var indices = mesh.Indices;

                for (var index = 0; index + 2 < indices.Count; index += 3)
                {
                    var a = Vector3.Transform(verts[indices[index]].Position, pieceMatrix);
                    var b = Vector3.Transform(verts[indices[index + 1]].Position, pieceMatrix);
                    var c = Vector3.Transform(verts[indices[index + 2]].Position, pieceMatrix);

                    var geometricNormal = Vector3.Cross(b - a, c - a);
                    var geometricLength = geometricNormal.Length();

                    if (geometricLength <= float.Epsilon || MathF.Abs(geometricNormal.Z / geometricLength) <= 0.5f)
                    {
                        continue;
                    }

                    var centroidZ = (a.Z + b.Z + c.Z) / 3f;

                    if (centroidZ - anchorStart.Z < bandFloor || centroidZ - anchorStart.Z > bandCeiling)
                    {
                        continue;
                    }

                    // Longest edge; slope = dZ per unit of span-axis projection.
                    var (p, q) = LongestEdge(a, b, c);
                    var spanDelta = MathF.Abs(Vector3.Dot(q - p, spanAxis));

                    if (spanDelta < 1f)
                    {
                        continue;
                    }

                    // Magnitude only: triangle winding flips the sign, and the roadway
                    // pitch correction needs the grade magnitude, not its direction.
                    var centroid = (a + b + c) / 3f;
                    centroidPoints.Add((Vector3.Dot(centroid - anchorStart, spanAxis), centroid.Z));
                }

                currentOffset += lengths[pieceIndex];
            }
        }

        if (centroidPoints.Count < 6)
        {
            return null;
        }

        // The rideable pitch comes from the TOP roadway surface: bucket the roadway
        // centroids along the span and take each bucket's maximum Z (the upper envelope).
        // This rejects the buried camber roadway and arch tops that sit below the deck,
        // which would otherwise drag a plain centroid fit toward their own (lower) pitch.
        var envelope = new SortedDictionary<int, float>();

        foreach (var point in centroidPoints)
        {
            var bucket = (int)MathF.Floor(point.T / 25f);

            if (!envelope.TryGetValue(bucket, out var current) || point.Z > current)
            {
                envelope[bucket] = point.Z;
            }
        }

        if (envelope.Count < 6)
        {
            return null;
        }

        var pitch = FitPitch(envelope.Select(pair => ((pair.Key * 25f) + 12.5f, pair.Value)).ToList());

        if (!float.IsFinite(pitch))
        {
            return null;
        }

        return MathF.Atan(pitch);
    }

    private static float FitPitch(IReadOnlyList<(float T, float Z)> points)
    {
        var meanT = points.Average(p => p.T);
        var meanZ = points.Average(p => p.Z);
        var covariance = 0f;
        var variance = 0f;

        foreach (var point in points)
        {
            covariance += (point.T - meanT) * (point.Z - meanZ);
            variance += (point.T - meanT) * (point.T - meanT);
        }

        return variance > float.Epsilon ? covariance / variance : float.NaN;
    }

    private static (Vector3 P, Vector3 Q) LongestEdge(Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = Vector3.Distance(a, b);
        var bc = Vector3.Distance(b, c);
        var ca = Vector3.Distance(c, a);

        if (ab >= bc && ab >= ca)
        {
            return (a, b);
        }

        return bc >= ca ? (b, c) : (c, a);
    }

    public static BridgeSurface? Create(
        IReadOnlyList<Tuple<ModelSubObject, Matrix4x4>> pieces,
        Func<ModelSubObject, ImportedMesh?> importedMesh,
        in Vector3 anchorStart,
        in Vector3 anchorEnd)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(importedMesh);

        var axis = anchorEnd - anchorStart;
        var axisLength = axis.Length();

        if (axisLength <= float.Epsilon)
        {
            return null;
        }

        var axisDirection = axis / axisLength;
        var bandMax = (MathF.Abs(anchorEnd.Z - anchorStart.Z) * RoadwayBandRiseScale) + RoadwayBandBaseMax;
        var triangles = new List<Triangle>();

        foreach (var piece in pieces)
        {
            if (importedMesh(piece.Item1) is not { } mesh)
            {
                continue;
            }

            AppendRoadwayTriangles(triangles, mesh, piece.Item2, anchorStart, anchorEnd, axisDirection, axisLength, bandMax);
        }

        if (triangles.Count == 0)
        {
            return null;
        }

        return Build(triangles);
    }

    /// <summary>
    /// Builds a surface from world-space roadway triangles directly; the unit-test seam for
    /// containment, topmost selection, and extent behavior without imported bridge meshes.
    /// </summary>
    public static BridgeSurface CreateFromTriangles(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        var list = triangles.Select(triangle => new Triangle(triangle.A, triangle.B, triangle.C)).ToList();

        if (list.Count == 0)
        {
            throw new ArgumentException("A bridge surface needs at least one triangle.", nameof(triangles));
        }

        return Build(list);
    }

    /// <summary>
    /// Robust deck-plane fit of this surface's roadway triangles: z = A*x + B*y + C via
    /// area-weighted least squares with iterative residual trimming. Rails sit above the
    /// deck plane (+3.3..+8.5 measured on the fixture), skirt bevels below (relZ -2.5),
    /// so triangles whose plane residual exceeds the trim threshold are dropped and the
    /// plane refit on the survivors; the inlier set is the roadway footprint.
    /// </summary>
    public sealed record DeckPlaneFit
    {
        public bool Success { get; init; }

        public float A { get; init; }

        public float B { get; init; }

        public float C { get; init; }

        public float RmsResidual { get; init; }

        public float MaxAbsResidual { get; init; }

        public float InlierAreaFraction { get; init; }

        public int InlierTriangleCount { get; init; }

        public int TotalTriangleCount { get; init; }

        public IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> InlierTriangles { get; init; } = [];

        public float PlaneZ(float x, float y) => (A * x) + (B * y) + C;
    }

    // Fit guard thresholds, retained for the FitDeckPlane diagnostic (the accepted
    // r3-g8 diff used it as the cut basis; the corrective design plans from actual
    // supported roadway faces instead and no longer relies on the fit for clearance).
    private const int MinimumInlierTriangles = 12;

    private const float MinimumInlierAreaFraction = 0.5f;

    private const float MaximumRmsResidual = 0.35f;

    private const float MaximumInlierAbsResidual = 1.2f;

    private const float TrimThresholdStart = 3f;

    private const float TrimThresholdSteady = 1.2f;

    /// <summary>
    /// Fits the deck plane of this surface's roadway triangles. Returns Success=false
    /// (no plane, no footprint) when the fit guard rejects the geometry: too few
    /// inlier triangles, inlier area fraction below half, or residuals that indicate
    /// nonplanar/unsupported roadway. The caller logs and skips the bridge's cut.
    /// </summary>
    public DeckPlaneFit FitDeckPlane()
    {
        var totalArea = 0f;
        var candidates = new List<int>(_triangles.Count);

        for (var index = 0; index < _triangles.Count; index++)
        {
            var area = TriangleXyArea(_triangles[index]);

            if (area <= float.Epsilon)
            {
                continue;
            }

            totalArea += area;
            candidates.Add(index);
        }

        var failure = new DeckPlaneFit
        {
            Success = false,
            InlierTriangleCount = 0,
            TotalTriangleCount = candidates.Count,
        };

        if (candidates.Count < MinimumInlierTriangles || totalArea <= float.Epsilon)
        {
            return failure;
        }

        // Seed stratum: a plain least-squares plane over roadway + rails + skirts
        // compromises between them and lands far from the roadway, which defeats the
        // residual trimming. Select the dominant Z stratum first: the area-weighted
        // median centroid height and its Tukey fence (q1 - 1.5*IQR .. q3 + 1.5*IQR)
        // isolate the roadway (the overwhelming majority of roadway area), after which
        // least squares fits the deck plane and residual trimming removes the stragglers
        // (rails that share the deck's Z range near landings).
        var seedSet = DominantZStratum(candidates);
        var (a, b, c) = FitWeightedPlane(seedSet);

        if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c))
        {
            return failure;
        }

        var survivors = seedSet;

        // Iterative trimming: two passes at the coarse threshold strip rails/skirts,
        // steady-state passes at the tight threshold clean the roadway set. Stop when
        // the survivor set is stable.
        for (var iteration = 0; iteration < 8 && survivors.Count > MinimumInlierTriangles; iteration++)
        {
            var threshold = iteration < 2 ? TrimThresholdStart : TrimThresholdSteady;
            var kept = new List<int>(survivors.Count);

            foreach (var index in survivors)
            {
                var triangle = _triangles[index];
                var centroid = (triangle.A + triangle.B + triangle.C) / 3f;
                var residual = MathF.Abs(centroid.Z - ((a * centroid.X) + (b * centroid.Y) + c));

                if (residual <= threshold)
                {
                    kept.Add(index);
                }
            }

            if (kept.Count < MinimumInlierTriangles)
            {
                break;
            }

            survivors = kept;
            (a, b, c) = FitWeightedPlane(survivors);

            if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c))
            {
                return failure;
            }
        }

        var inlierArea = 0f;
        var squaredResidualSum = 0f;
        var maxResidual = 0f;

        foreach (var index in survivors)
        {
            var triangle = _triangles[index];
            var centroid = (triangle.A + triangle.B + triangle.C) / 3f;
            var residual = MathF.Abs(centroid.Z - ((a * centroid.X) + (b * centroid.Y) + c));

            inlierArea += TriangleXyArea(triangle);
            squaredResidualSum += residual * residual;
            maxResidual = MathF.Max(maxResidual, residual);
        }

        var rms = MathF.Sqrt(squaredResidualSum / survivors.Count);
        var fit = new DeckPlaneFit
        {
            Success = true,
            A = a,
            B = b,
            C = c,
            RmsResidual = rms,
            MaxAbsResidual = maxResidual,
            InlierAreaFraction = inlierArea / totalArea,
            InlierTriangleCount = survivors.Count,
            TotalTriangleCount = candidates.Count,
            InlierTriangles = survivors.Select(index =>
            {
                var triangle = _triangles[index];
                return (triangle.A, triangle.B, triangle.C);
            }).ToList(),
        };

        // Documented guard: reject curved decks, fragmented roadways, and sets too small
        // to support a footprint. No fallback plane is ever guessed; a rejected fit
        // carries no footprint.
        var success = survivors.Count >= MinimumInlierTriangles
            && fit.InlierAreaFraction >= MinimumInlierAreaFraction
            && rms <= MaximumRmsResidual
            && maxResidual <= MaximumInlierAbsResidual;

        return fit with
        {
            Success = success,
            InlierTriangles = success ? fit.InlierTriangles : [],
        };
    }

    /// <summary>
    /// The area-weighted median centroid-Z stratum and its Tukey fence: triangles whose
    /// centroid Z lies within q1 - 1.5*IQR .. q3 + 1.5*IQR of the weighted quartiles.
    /// Deterministic and data-driven; isolates the dominant (roadway) surface before any
    /// plane exists to trim against.
    /// </summary>
    private List<int> DominantZStratum(List<int> indices)
    {
        var ordered = indices.OrderBy(index =>
        {
            var triangle = _triangles[index];
            return ((triangle.A + triangle.B + triangle.C) / 3f).Z;
        }).ToList();

        var weights = ordered.Select(index => TriangleXyArea(_triangles[index])).ToList();
        var total = weights.Sum();

        if (total <= float.Epsilon)
        {
            return indices;
        }

        float Quantile(float fraction)
        {
            var accumulated = 0f;

            for (var index = 0; index < ordered.Count; index++)
            {
                accumulated += weights[index];

                if (accumulated >= total * fraction)
                {
                    var triangle = _triangles[ordered[index]];
                    return ((triangle.A + triangle.B + triangle.C) / 3f).Z;
                }
            }

            var last = _triangles[ordered[^1]];
            return ((last.A + last.B + last.C) / 3f).Z;
        }

        var q1 = Quantile(0.25f);
        var q3 = Quantile(0.75f);
        var fence = 1.5f * (q3 - q1);
        var lower = q1 - fence;
        var upper = q3 + fence;

        var stratum = indices.Where(index =>
        {
            var triangle = _triangles[index];
            var centroidZ = ((triangle.A + triangle.B + triangle.C) / 3f).Z;
            return centroidZ >= lower && centroidZ <= upper;
        }).ToList();

        return stratum.Count >= 3 ? stratum : indices;
    }

    /// <summary>
    /// Area-weighted least-squares plane fit (normal equations solved by Gaussian
    /// elimination with partial pivoting) over the given triangle indices, weighted by
    /// each triangle's XY area so large roadway quads dominate thin rail strips.
    /// </summary>
    private (float A, float B, float C) FitWeightedPlane(List<int> indices)
    {
        float sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, s = 0, sxz = 0, syz = 0, sz = 0;

        foreach (var index in indices)
        {
            var triangle = _triangles[index];
            var centroid = (triangle.A + triangle.B + triangle.C) / 3f;
            var weight = TriangleXyArea(triangle);
            var x = centroid.X;
            var y = centroid.Y;

            s += weight;
            sx += weight * x;
            sy += weight * y;
            sxx += weight * x * x;
            sxy += weight * x * y;
            syy += weight * y * y;
            sxz += weight * x * centroid.Z;
            syz += weight * y * centroid.Z;
            sz += weight * centroid.Z;
        }

        // [Sxx Sxy Sx; Sxy Syy Sy; Sx Sy S] * [a; b; c] = [Sxz; Syz; Sz]
        var matrix = new float[3, 4]
        {
            { sxx, sxy, sx, sxz },
            { sxy, syy, sy, syz },
            { sx, sy, s, sz },
        };

        return Solve3x4(matrix);
    }

    private static (float A, float B, float C) Solve3x4(float[,] m)
    {
        for (var column = 0; column < 3; column++)
        {
            var pivot = column;

            for (var row = column + 1; row < 3; row++)
            {
                if (MathF.Abs(m[row, column]) > MathF.Abs(m[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (MathF.Abs(m[pivot, column]) < 1e-9f)
            {
                return (float.NaN, float.NaN, float.NaN);
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == column)
                {
                    continue;
                }

                var factor = m[row, column] / m[column, column];

                for (var k = column; k < 4; k++)
                {
                    m[row, k] -= factor * m[column, k];
                }
            }
        }

        return (m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2]);
    }

    /// <summary>
    /// Runtime terrain clearance cut against this surface's own triangles (the unit-test
    /// seam; production goes through Bridge.TryPlanClearance with reviewed source-face
    /// membership). Every triangle is treated as a supported roadway face: terrain under
    /// overlapping cells lowers to the minimum actual face plane minus 1.02, with the
    /// adjacent ring and edge fail-closed rules of the envelope design.
    /// </summary>
    public bool TryCutTerrainClearance(HeightMap heightMap, out RectangleF changedBounds, out DeckPlaneFit fit)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        fit = new DeckPlaneFit
        {
            Success = true,
            InlierTriangleCount = _triangles.Count,
            TotalTriangleCount = _triangles.Count,
            InlierTriangles = _triangles.Select(triangle => (triangle.A, triangle.B, triangle.C)).ToList(),
        };
        changedBounds = default;

        var faces = _triangles
            .Select((triangle, index) => new BridgeRawFace("synthetic", 0, index, 0, 0, triangle.A, triangle.B, triangle.C))
            .ToList();

        var plan = TryBuildEnvelopePlan(faces, heightMap, out var unsupportedReason);

        if (plan == null)
        {
            fit = fit with { Success = false, MaxAbsResidual = 0f, RmsResidual = 0f, InlierAreaFraction = 0f, InlierTriangleCount = 0 };
            return false;
        }

        var (bounds, changed) = TerrainClearancePlan.MergeAndApply(heightMap, [plan]);
        changedBounds = changed > 0 ? bounds : default;
        return true;
    }

    /// <summary>
    /// One bridge's clearance plan: explicit per-lattice-vertex targets computed against
    /// the pre-cut height map (footprint vertices get the pure minimum actual roadway
    /// face plane minus 1.02; ring vertices get the smoothstep blend with their pre-cut
    /// height). Plans contain no height reads at merge time, so collecting every
    /// generated bridge's plan before applying any makes the merged result independent
    /// of bridge order, and a reload computes the identical plan.
    /// </summary>
    public sealed class TerrainClearancePlan
    {
        public required IReadOnlyDictionary<(int X, int Y), float> VertexTargets { get; init; }

        public required IReadOnlyDictionary<(int X, int Y), float> RingTargets { get; init; }

        public required RectangleF Bounds { get; init; }

        public required int RoadwayFaceCount { get; init; }

        public required float RoadwayProjectedArea { get; init; }

        public required string RoadwayFaceIdentityHash { get; init; }

        public required int FootprintCellCount { get; init; }

        public required bool ApronEndMin { get; init; }

        public required bool ApronEndMax { get; init; }

        /// <summary>
        /// Deterministic merge-and-apply: per-vertex minimum over all plans, applied once
        /// against the shared pre-cut-height map (LowerHeight is itself a lower bound, so
        /// plan application order cannot change the stored elevations), then one bounded
        /// normals refresh and one union bounds for the terrain notification.
        /// </summary>
        public static (RectangleF Bounds, int ChangedVertices) MergeAndApply(HeightMap heightMap, IReadOnlyList<TerrainClearancePlan> plans)
        {
            ArgumentNullException.ThrowIfNull(heightMap);
            ArgumentNullException.ThrowIfNull(plans);

            var merged = new Dictionary<(int X, int Y), float>();

            foreach (var plan in plans)
            {
                foreach (var (vertex, target) in plan.VertexTargets)
                {
                    merged[vertex] = merged.TryGetValue(vertex, out var current) ? MathF.Min(current, target) : target;
                }
            }

            foreach (var plan in plans)
            {
                foreach (var (vertex, target) in plan.RingTargets)
                {
                    merged[vertex] = merged.TryGetValue(vertex, out var current) ? MathF.Min(current, target) : target;
                }
            }

            var modified = 0;
            var minI = int.MaxValue;
            var minJ = int.MaxValue;
            var maxI = int.MinValue;
            var maxJ = int.MinValue;

            foreach (var (vertex, target) in merged)
            {
                var current = heightMap.GetHeight(vertex.X, vertex.Y);

                if (current <= target || target <= 0f)
                {
                    continue;
                }

                heightMap.LowerHeight(vertex.X, vertex.Y, target);
                modified++;

                minI = Math.Min(minI, vertex.X);
                minJ = Math.Min(minJ, vertex.Y);
                maxI = Math.Max(maxI, vertex.X);
                maxJ = Math.Max(maxJ, vertex.Y);
            }

            if (modified == 0)
            {
                return (default, 0);
            }

            // Every normal reads its surrounding samples, so a bounded refresh inflated
            // by one vertex repairs all neighbors of the changed heights.
            heightMap.RefreshNormalsInBoundedArea(minI, minJ, maxI, maxJ);

            var minCorner = heightMap.GetPosition(minI, minJ);
            var maxCorner = heightMap.GetPosition(maxI, maxJ);
            var bounds = new RectangleF(
                minCorner.X - HeightMap.HorizontalScale,
                minCorner.Y - HeightMap.HorizontalScale,
                (maxCorner.X - minCorner.X) + (2f * HeightMap.HorizontalScale),
                (maxCorner.Y - minCorner.Y) + (2f * HeightMap.HorizontalScale));

            return (bounds, modified);
        }
    }

    public readonly record struct BridgeRawFace(
        string PieceName,
        int CopyIndex,
        int SourceFaceIndex,
        int MeshPartIndex,
        int MaterialIndex,
        Vector3 A,
        Vector3 B,
        Vector3 C)
    {
        /// <summary>
        /// World Z of this triangle's plane at the XY point (extrapolates outside the
        /// triangle; callers only query vertices of cells the face overlaps). NaN when
        /// the face is not a usable height plane.
        /// </summary>
        public float PlaneZ(float x, float y)
        {
            var normal = Vector3.Cross(B - A, C - A);

            if (MathF.Abs(normal.Z) <= float.Epsilon)
            {
                return float.NaN;
            }

            return A.Z - (((normal.X * (x - A.X)) + (normal.Y * (y - A.Y))) / normal.Z);
        }

        public Vector2 MinXy => Vector2.Min(Axy, Vector2.Min(Bxy, Cxy));

        public Vector2 MaxXy => Vector2.Max(Axy, Vector2.Max(Bxy, Cxy));

        public Vector2 Axy => new(A.X, A.Y);

        public Vector2 Bxy => new(B.X, B.Y);

        public Vector2 Cxy => new(C.X, C.Y);
    }

    /// <summary>
    /// Classification accounting for one bridge's raw world faces: every face is
    /// rendered, candidate, supported roadway, non-roadway with a reviewed reason, or
    /// unclassified. Unclassified > 0 makes the bridge unsupported (no cut).
    /// </summary>
    public sealed class BridgeFaceClassification
    {
        public bool Supported { get; init; }

        public string? UnsupportedReason { get; init; }

        public required IReadOnlyList<BridgeRawFace> RoadwayFaces { get; init; }

        public int RenderedFaceCount { get; init; }

        public int CandidateFaceCount { get; init; }

        public int UnclassifiedCount { get; init; }

        public required IReadOnlyDictionary<string, int> NonRoadwayReasons { get; init; }
    }

    /// <summary>Supported installed asset: the Concrete template's CBBRIDGEST model,
    /// verified by subobject/local-geometry fingerprint (see ComputeGeometryFingerprint).</summary>
    public const string SupportedTemplateName = "Concrete";

    public const string SupportedGeometryFingerprint = "6929151B06A2E145D186375822EEBC33BD4E65EC4FB530374E4525D9C631828E";

    // Reviewed source face ranges per CBBRIDGEST subobject (BRIDGE_LEFT / BRIDGE_SPAN /
    // BRIDGE_RIGHT01), established from the installed-model face dump: 0-3 arch frames
    // (world-vertical), 4-7 the kerb-to-kerb roadway slab (world-horizontal, relZ
    // -1.08..+1.12 with the crown cross-slope the old single plane missed), 8-23
    // kerb/rail strips (elevated or edge slivers), 24-51 arch understructure, 52-67
    // furniture posts.
    public const int RoadwayFirstFaceIndex = 4;

    public const int RoadwayLastFaceIndex = 7;

    private const int KerbRailFirstFaceIndex = 8;

    private const int KerbRailLastFaceIndex = 23;

    private const int ArchFirstFaceIndex = 24;

    private const int ArchLastFaceIndex = 51;

    public static bool IsSupportedGeometry(string? fingerprint)
    {
        return string.Equals(fingerprint, SupportedGeometryFingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Canonical local-geometry fingerprint: SHA-256 over the subobject order with full
    /// names, vertex/index counts, per-vertex IEEE-754 bit patterns (invariant hex), all
    /// indices, and mesh-part ranges — exact for the same importer and asset bytes.
    /// </summary>
    public static string ComputeGeometryFingerprint(IReadOnlyList<(string FullName, ImportedMesh Mesh)> subObjects)
    {
        var canonical = new StringBuilder();

        foreach (var (fullName, mesh) in subObjects)
        {
            canonical.Append(fullName).Append('|').Append(mesh.Vertices.Count).Append('|').Append(mesh.Indices.Count);

            foreach (var part in mesh.MeshParts)
            {
                canonical.Append("|p").Append(part.StartIndex).Append(':').Append(part.IndexCount).Append(':').Append(part.MaterialIndex);
            }

            foreach (var vertex in mesh.Vertices)
            {
                canonical.Append("|v")
                    .Append(BitConverter.SingleToInt32Bits(vertex.Position.X).ToString("X8", CultureInfo.InvariantCulture))
                    .Append(BitConverter.SingleToInt32Bits(vertex.Position.Y).ToString("X8", CultureInfo.InvariantCulture))
                    .Append(BitConverter.SingleToInt32Bits(vertex.Position.Z).ToString("X8", CultureInfo.InvariantCulture));
            }

            foreach (var index in mesh.Indices)
            {
                canonical.Append('|').Append(index);
            }
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>
    /// Classifies raw world faces with the reviewed source ranges. Candidate rule mirrors
    /// the movement surface: world |normal Z| above HorizontalNormalZ and centroid inside
    /// the anchor band. Roadway = source faces 4-7; other candidates are non-roadway with
    /// their reviewed range reason; any candidate outside the reviewed sets is
    /// unclassified and fails the bridge closed.
    /// </summary>
    public static BridgeFaceClassification ClassifyRawFaces(IReadOnlyList<BridgeRawFace> faces, in Vector3 anchorStart, in Vector3 anchorEnd)
    {
        var axis = anchorEnd - anchorStart;
        var axisLength = axis.Length();

        if (axisLength <= float.Epsilon)
        {
            return new BridgeFaceClassification
            {
                Supported = false,
                UnsupportedReason = "degenerate-anchor-axis",
                RoadwayFaces = [],
                NonRoadwayReasons = new Dictionary<string, int>(),
            };
        }

        var axisDirection = axis / axisLength;
        var bandMax = (MathF.Abs(anchorEnd.Z - anchorStart.Z) * RoadwayBandRiseScale) + RoadwayBandBaseMax;
        var roadway = new List<BridgeRawFace>();
        var reasons = new Dictionary<string, int>();
        var rendered = 0;
        var candidates = 0;
        var unclassified = 0;

        foreach (var face in faces)
        {
            rendered++;

            var ab = face.B - face.A;
            var ac = face.C - face.A;
            var normal = Vector3.Cross(ab, ac);
            var normalLength = normal.Length();

            if (normalLength <= float.Epsilon)
            {
                continue;
            }

            var worldNormalZ = normal.Z / normalLength;
            var centroid = (face.A + face.B + face.C) / 3f;
            var anchorT = Math.Clamp(Vector3.Dot(centroid - anchorStart, axisDirection) / axisLength, 0f, 1f);
            var anchorLineZ = anchorStart.Z + ((anchorEnd.Z - anchorStart.Z) * anchorT);
            var anchorRelativeZ = centroid.Z - anchorLineZ;
            var isCandidate = MathF.Abs(worldNormalZ) > HorizontalNormalZ
                && anchorRelativeZ > RoadwayBandMin
                && anchorRelativeZ < bandMax;

            if (!isCandidate)
            {
                continue;
            }

            candidates++;

            if (face.SourceFaceIndex >= RoadwayFirstFaceIndex && face.SourceFaceIndex <= RoadwayLastFaceIndex)
            {
                roadway.Add(face);
            }
            else if (face.SourceFaceIndex >= KerbRailFirstFaceIndex && face.SourceFaceIndex <= KerbRailLastFaceIndex)
            {
                reasons["kerb-rail-strip"] = reasons.GetValueOrDefault("kerb-rail-strip") + 1;
            }
            else if (face.SourceFaceIndex >= ArchFirstFaceIndex && face.SourceFaceIndex <= ArchLastFaceIndex)
            {
                reasons["arch-understructure"] = reasons.GetValueOrDefault("arch-understructure") + 1;
            }
            else
            {
                unclassified++;
                reasons["unclassified"] = reasons.GetValueOrDefault("unclassified") + 1;
            }
        }

        var supported = unclassified == 0 && roadway.Count > 0;
        return new BridgeFaceClassification
        {
            Supported = supported,
            UnsupportedReason = supported ? null : (unclassified > 0 ? "unclassified-candidate-faces" : "no-supported-roadway-faces"),
            RoadwayFaces = roadway,
            RenderedFaceCount = rendered,
            CandidateFaceCount = candidates,
            UnclassifiedCount = unclassified,
            NonRoadwayReasons = reasons,
        };
    }

    /// <summary>
    /// Builds the envelope clearance plan from the supported roadway faces against the
    /// pre-cut height map. Fails closed (null + reason) when any roadway face crosses the
    /// usable lattice edge (faces not fully inside rendered terrain cannot be certified).
    /// Footprint: every rendered cell (0..Width-2) overlapping a roadway face; its four
    /// vertices take the minimum over intersecting faces of facePlaneZ(vertex) - 1.02
    /// (1.0 clearance + 0.02 quantization margin; LowerHeight ushort truncation only
    /// lowers further). Ring: unmarked vertices (0..Width-1) within the ring width of the
    /// footprint blend the adjacent-face envelope value to their pre-cut height with
    /// smoothstep (t^2)(3-2t). No fitted plane anywhere.
    /// </summary>
    public static TerrainClearancePlan? TryBuildEnvelopePlan(
        IReadOnlyList<BridgeRawFace> roadwayFaces,
        HeightMap heightMap,
        out string? unsupportedReason)
    {
        ArgumentNullException.ThrowIfNull(roadwayFaces);
        ArgumentNullException.ThrowIfNull(heightMap);

        unsupportedReason = null;

        if (roadwayFaces.Count == 0)
        {
            unsupportedReason = "no-roadway-faces";
            return null;
        }

        // Usable lattice: rendered samples 0..Width-1 / 0..Height-1, rendered cells
        // 0..Width-2 / 0..Height-2 (the stored last row/column is phantom).
        var lastSampleX = heightMap.Width - 1;
        var lastSampleY = heightMap.Height - 1;
        var lastCellX = heightMap.Width - 2;
        var lastCellY = heightMap.Height - 2;
        var usableMin = heightMap.GetPosition(0, 0);
        var usableMax = heightMap.GetPosition(lastSampleX, lastSampleY);

        foreach (var face in roadwayFaces)
        {
            var min = face.MinXy;
            var max = face.MaxXy;

            if (min.X < usableMin.X - 0.01f || min.Y < usableMin.Y - 0.01f || max.X > usableMax.X + 0.01f || max.Y > usableMax.Y + 0.01f)
            {
                unsupportedReason = $"roadway-face-crosses-usable-lattice-edge ({face.PieceName}#{face.CopyIndex} face {face.SourceFaceIndex})";
                return null;
            }

            if (!float.IsFinite(face.PlaneZ((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f)))
            {
                unsupportedReason = $"roadway-face-is-not-a-height-plane ({face.PieceName}#{face.CopyIndex} face {face.SourceFaceIndex})";
                return null;
            }
        }

        var footprintPolygons = roadwayFaces
            .Select(face => new List<Vector2> { face.Axy, face.Bxy, face.Cxy })
            .ToList();
        var cutDepth = ClearanceCutDepth + ClearanceCutMargin;

        // Footprint pass: cells 0..lastCell; per cell, the intersecting faces feed the
        // per-vertex minimum envelope target.
        var vertexTargets = new Dictionary<(int X, int Y), float>();
        var footprintCells = new HashSet<(int X, int Y)>();
        var footprintCellFaces = new Dictionary<(int X, int Y), List<BridgeRawFace>>();

        for (var i = 0; i <= lastCellX; i++)
        {
            for (var j = 0; j <= lastCellY; j++)
            {
                var quad = CellQuad(heightMap, i, j);
                List<BridgeRawFace>? intersecting = null;

                for (var faceIndex = 0; faceIndex < roadwayFaces.Count; faceIndex++)
                {
                    var face = roadwayFaces[faceIndex];
                    var min = face.MinXy;
                    var max = face.MaxXy;

                    if (max.X < quad[0].X || min.X > quad[2].X || max.Y < quad[0].Y || min.Y > quad[2].Y)
                    {
                        continue;
                    }

                    if (!ConvexPolygonsOverlap(quad, footprintPolygons[faceIndex]))
                    {
                        continue;
                    }

                    intersecting ??= [];
                    intersecting.Add(face);
                }

                if (intersecting == null)
                {
                    continue;
                }

                footprintCells.Add((i, j));
                footprintCellFaces[(i, j)] = intersecting;

                foreach (var vertex in new[] { (i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1) })
                {
                    var position = heightMap.GetPosition(vertex.Item1, vertex.Item2);
                    var target = float.MaxValue;

                    foreach (var face in intersecting)
                    {
                        var planeZ = face.PlaneZ(position.X, position.Y);

                        if (float.IsFinite(planeZ))
                        {
                            target = MathF.Min(target, planeZ - cutDepth);
                        }
                    }

                    if (target == float.MaxValue)
                    {
                        continue;
                    }

                    vertexTargets[vertex] = MathF.Min(vertexTargets.GetValueOrDefault(vertex, float.MaxValue), target);
                }
            }
        }

        if (vertexTargets.Count == 0)
        {
            unsupportedReason = "roadway-overlaps-no-rendered-cells";
            return null;
        }

        // Ring pass: unmarked vertices 0..lastSample within the ring width of the
        // footprint blend the adjacent-cell envelope value back to the pre-cut height.
        var ringTargets = new Dictionary<(int X, int Y), float>();

        for (var i = 0; i <= lastSampleX; i++)
        {
            for (var j = 0; j <= lastSampleY; j++)
            {
                if (vertexTargets.ContainsKey((i, j)))
                {
                    continue;
                }

                var position = heightMap.GetPosition(i, j);
                var distance = DistanceToFootprint(new Vector2(position.X, position.Y), footprintPolygons);

                if (distance >= ClearanceRingWidth)
                {
                    continue;
                }

                var envelope = float.MaxValue;

                for (var di = -1; di <= 1 && envelope == float.MaxValue; di++)
                {
                    for (var dj = -1; dj <= 1; dj++)
                    {
                        if (footprintCellFaces.TryGetValue((i + di, j + dj), out var adjacent))
                        {
                            foreach (var face in adjacent)
                            {
                                var planeZ = face.PlaneZ(position.X, position.Y);

                                if (float.IsFinite(planeZ))
                                {
                                    envelope = MathF.Min(envelope, planeZ - cutDepth);
                                }
                            }
                        }
                    }
                }

                if (envelope == float.MaxValue)
                {
                    continue;
                }

                var t = distance / ClearanceRingWidth;
                var weight = (t * t) * (3f - (2f * t));
                var preCut = heightMap.GetHeight(i, j);
                ringTargets[(i, j)] = envelope + ((preCut - envelope) * weight);
            }
        }

        // Mouth apron pass (r3-g8-mouth-apron): per-mouth LOCAL raised classification —
        // each deck mouth (the supported footprint's two longitudinal ends) is sampled
        // independently at INVARIANT stations beyond the apron lateral reach plus one
        // full lattice stencil (terrain never modified by footprint/ring/apron passes).
        // A mouth whose band samples sit above its roadway edge by more than the
        // threshold gets a cut-only linear apron: non-footprint vertices within 23
        // laterally of the roadway edge and inside seam±40 blend from the roadway-edge
        // envelope target to pre-cut, tapering smoothly at BOTH the lateral (d/23) and
        // longitudinal (smoothstep to zero at ±40) boundaries — no abrupt cutoff
        // between raised/not-raised rows. Footprint targets, the driven corridor
        // (vehicle path x 546-570 on the fixture), and the oracle are untouched.
        var slabMinX = roadwayFaces.Min(face => face.MinXy.X);
        var slabMinY = roadwayFaces.Min(face => face.MinXy.Y);
        var slabMaxX = roadwayFaces.Max(face => face.MaxXy.X);
        var slabMaxY = roadwayFaces.Max(face => face.MaxXy.Y);

        var apronEndMin = false;
        var apronEndMax = false;
        var lateralIsX = slabMaxX - slabMinX <= slabMaxY - slabMinY;
        var lateralMin = lateralIsX ? slabMinX : slabMinY;
        var lateralMax = lateralIsX ? slabMaxX : slabMaxY;
        var longMin = lateralIsX ? slabMinY : slabMinX;
        var longMax = lateralIsX ? slabMaxY : slabMaxX;

        if (slabMaxX - slabMinX > 1f && slabMaxY - slabMinY > 1f)
        {
            foreach (var (end, isMaxEnd) in new[] { (longMin, false), (longMax, true) })
            {
                var bandLo = end - ApronLongitudinalExtent;
                var bandHi = end + ApronLongitudinalExtent;

                // Classify BOTH lateral sides of this mouth independently: each side is
                // raised iff its mean pre-cut terrain (sampled beyond the apron reach +
                // one lattice stencil) exceeds the roadway-edge plane height by more
                // than the threshold.
                var raisedSides = new List<(bool IsMax, float RoadLateral, float InvariantOutsideMean)>();

                foreach (var (sideIsMax, sideRoadLateral) in new[] { (false, lateralMin), (true, lateralMax) })
                {
                    var sampleLateral = sideRoadLateral + (sideIsMax ? ApronClassificationSampleOffset : -ApronClassificationSampleOffset);
                    var sampleTile = heightMap.GetTilePosition(new Vector2(sampleLateral, end));

                    if (sampleTile == null)
                    {
                        continue;
                    }

                    var roadSum = 0f;
                    var outsideSum = 0f;
                    var samples = 0;

                    for (var y = bandLo; y <= bandHi + 0.1f; y += 5f)
                    {
                        var wx = lateralIsX ? sampleLateral : y;
                        var wy = lateralIsX ? y : sampleLateral;
                        var roadX = lateralIsX ? sideRoadLateral : y;
                        var roadY = lateralIsX ? y : sideRoadLateral;

                        float? roadZ = null;

                        foreach (var face in roadwayFaces)
                        {
                            var faceMinLong = lateralIsX ? face.MinXy.Y : face.MinXy.X;
                            var faceMaxLong = lateralIsX ? face.MaxXy.Y : face.MaxXy.X;

                            if (y < faceMinLong || y > faceMaxLong)
                            {
                                continue;
                            }

                            var planeZ = face.PlaneZ(roadX, roadY);

                            if (float.IsFinite(planeZ))
                            {
                                roadZ = planeZ;
                                break;
                            }
                        }

                        if (roadZ == null)
                        {
                            continue;
                        }

                        roadSum += roadZ.Value;
                        outsideSum += heightMap.GetHeight(wx, wy);
                        samples++;
                    }

                    if (samples == 0)
                    {
                        continue;
                    }

                    var outsideMean = outsideSum / samples;
                    var roadMean = roadSum / samples;
                    var sideRaised = outsideMean - roadMean > ShoulderRaisedThreshold;

                    if (sideRaised)
                    {
                        // InvariantOutsideMean: mean PRE-CUT terrain sampled beyond the
                        // apron reach + stencil (never modified by any application) —
                        // the apron target is a pure function of this reference and the
                        // roadway geometry, so re-application is stable (min-merge keeps
                        // the lower target; identical rebuilds reproduce it exactly).
                        raisedSides.Add((sideIsMax, sideRoadLateral, outsideMean));
                    }
                }

                if (raisedSides.Count == 0)
                {
                    continue;
                }

                if (raisedSides.Any(side => side.IsMax))
                {
                    apronEndMax = true;
                }

                if (raisedSides.Any(side => !side.IsMax))
                {
                    apronEndMin = true;
                }

                foreach (var raisedSide in raisedSides)
                {
                    var roadLateral = raisedSide.RoadLateral;

                    for (var i = 0; i <= lastSampleX; i++)
                    {
                        for (var j = 0; j <= lastSampleY; j++)
                        {
                            if (vertexTargets.ContainsKey((i, j)))
                            {
                                continue;
                            }

                            var position = heightMap.GetPosition(i, j);
                            var lateralCoord = lateralIsX ? position.X : position.Y;
                            var longitudinalCoord = lateralIsX ? position.Y : position.X;
                            var lateralDistance = raisedSide.IsMax ? lateralCoord - roadLateral : roadLateral - lateralCoord;

                            if (lateralDistance <= 0f || lateralDistance > ApronLateralReach)
                            {
                                continue;
                            }

                            if (longitudinalCoord < bandLo || longitudinalCoord > bandHi)
                            {
                                continue;
                            }

                            var envelope = float.MaxValue;

                            foreach (var face in roadwayFaces)
                            {
                                var planeZ = face.PlaneZ(position.X, position.Y);

                                if (float.IsFinite(planeZ))
                                {
                                    envelope = MathF.Min(envelope, planeZ - (ClearanceCutDepth + ClearanceCutMargin));
                                }
                            }

                            if (envelope == float.MaxValue)
                            {
                                continue;
                            }

                            // Apron strength: full at the footprint edge (wLat=1) and
                            // inside the band (sLong=1), zero at both the 23-unit lateral
                            // bound and the ±40 longitudinal bound — target blends from
                            // pre-cut (no effect) to the roadway-edge envelope target.
                            var wLat = 1f - (lateralDistance / ApronLateralReach);
                            var dLong = MathF.Abs(longitudinalCoord - end);
                            var tLong = Math.Clamp((ApronLongitudinalExtent - dLong) / 20f, 0f, 1f);
                            var sLong = (tLong * tLong) * (3f - (2f * tLong));
                            var apronStrength = wLat * sLong;
                            var preCut = heightMap.GetHeight(i, j);
                            var target = preCut - ((preCut - envelope) * apronStrength);

                            if (preCut <= target || target <= 0f)
                            {
                                continue;
                            }

                            ringTargets[(i, j)] = ringTargets.TryGetValue((i, j), out var existingApron)
                                ? MathF.Min(existingApron, target)
                                : target;
                        }
                    }
                }
            }
        }

        var projectedArea = roadwayFaces.Sum(face =>
            0.5f * MathF.Abs(((face.B.X - face.A.X) * (face.C.Y - face.A.Y)) - ((face.C.X - face.A.X) * (face.B.Y - face.A.Y))));

        var identities = roadwayFaces
            .Select(face => $"{face.PieceName}|{face.CopyIndex}|{face.SourceFaceIndex}")
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToList();

        using var identitySha = System.Security.Cryptography.SHA256.Create();
        var identityHash = System.Convert.ToHexString(identitySha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join(";", identities))));

        var growX = lateralIsX ? (apronEndMax || apronEndMin ? 13f : 0f) + ((apronEndMax ? 30f : 0f) + (apronEndMin ? 30f : 0f)) : 0f;
        var growY = lateralIsX ? ((apronEndMax ? 30f : 0f) + (apronEndMin ? 30f : 0f)) : (apronEndMax || apronEndMin ? 13f : 0f) + ((apronEndMax ? 30f : 0f) + (apronEndMin ? 30f : 0f));

        var bounds = new RectangleF(
            slabMinX - ClearanceRingWidth - HeightMap.HorizontalScale - (lateralIsX ? growX : growY),
            slabMinY - ClearanceRingWidth - HeightMap.HorizontalScale - (lateralIsX ? growY : growX),
            (slabMaxX - slabMinX) + (2f * (ClearanceRingWidth + HeightMap.HorizontalScale)) + (lateralIsX ? growX : growY),
            (slabMaxY - slabMinY) + (2f * (ClearanceRingWidth + HeightMap.HorizontalScale)) + (lateralIsX ? growY : growX));

        // Clamp to the usable lattice world box: the rendered/usable samples span
        // world 0 .. (usable samples) x pitch; bounds must never point outside it
        // (consumers iterate the rectangle).
        var usableMaxX = (heightMap.Width - 1) * HeightMap.HorizontalScale;
        var usableMaxY = (heightMap.Height - 1) * HeightMap.HorizontalScale;
        var clampedX = Math.Clamp(bounds.X, 0f, usableMaxX);
        var clampedY = Math.Clamp(bounds.Y, 0f, usableMaxY);
        var clampedRight = Math.Clamp(bounds.X + bounds.Width, 0f, usableMaxX);
        var clampedTop = Math.Clamp(bounds.Y + bounds.Height, 0f, usableMaxY);
        bounds = new RectangleF(clampedX, clampedY, clampedRight - clampedX, clampedTop - clampedY);

        return new TerrainClearancePlan
        {
            VertexTargets = vertexTargets,
            RingTargets = ringTargets,
            Bounds = bounds,
            RoadwayFaceCount = roadwayFaces.Count,
            RoadwayProjectedArea = projectedArea,
            RoadwayFaceIdentityHash = identityHash,
            FootprintCellCount = footprintCells.Count,
            ApronEndMin = apronEndMin,
            ApronEndMax = apronEndMax,
        };
    }

    private static List<Vector2> CellQuad(HeightMap heightMap, int i, int j)
    {
        var p00 = heightMap.GetPosition(i, j);
        var p10 = heightMap.GetPosition(i + 1, j);
        var p11 = heightMap.GetPosition(i + 1, j + 1);
        var p01 = heightMap.GetPosition(i, j + 1);

        return [new Vector2(p00.X, p00.Y), new Vector2(p10.X, p10.Y), new Vector2(p11.X, p11.Y), new Vector2(p01.X, p01.Y)];
    }

    /// <summary>
    /// Exact convex-polygon overlap test (separating-axis theorem over both polygons'
    /// edge normals); triangles and lattice quads are convex, so this is decisive.
    /// </summary>
    internal static bool ConvexPolygonsOverlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        if (a.Count < 3 || b.Count < 3)
        {
            return false;
        }

        return !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
    }

    private static bool HasSeparatingAxis(IReadOnlyList<Vector2> subject, IReadOnlyList<Vector2> other)
    {
        for (var index = 0; index < subject.Count; index++)
        {
            var p = subject[index];
            var q = subject[(index + 1) % subject.Count];
            var axis = new Vector2(q.Y - p.Y, p.X - q.X);

            var subjectMin = float.MaxValue;
            var subjectMax = float.MinValue;

            foreach (var vertex in subject)
            {
                var projection = (axis.X * vertex.X) + (axis.Y * vertex.Y);
                subjectMin = MathF.Min(subjectMin, projection);
                subjectMax = MathF.Max(subjectMax, projection);
            }

            var otherMin = float.MaxValue;
            var otherMax = float.MinValue;

            foreach (var vertex in other)
            {
                var projection = (axis.X * vertex.X) + (axis.Y * vertex.Y);
                otherMin = MathF.Min(otherMin, projection);
                otherMax = MathF.Max(otherMax, projection);
            }

            if (subjectMax <= otherMin || otherMax <= subjectMin)
            {
                return true;
            }
        }

        return false;
    }

    private static float DistanceToFootprint(Vector2 point, IReadOnlyList<List<Vector2>> footprint)
    {
        var minimum = float.MaxValue;

        foreach (var polygon in footprint)
        {
            minimum = MathF.Min(minimum, PointTriangleDistance(point, polygon[0], polygon[1], polygon[2]));
        }

        return minimum;
    }

    private static float PointTriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        if (PointInTriangle(p, a, b, c))
        {
            return 0f;
        }

        return Math.Min(Math.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)), SegmentDistance(p, c, a));
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(p, a, b);
        var d2 = Cross(p, b, c);
        var d3 = Cross(p, c, a);
        var hasNegative = (d1 < -1e-6f) || (d2 < -1e-6f) || (d3 < -1e-6f);
        var hasPositive = (d1 > 1e-6f) || (d2 > 1e-6f) || (d3 > 1e-6f);

        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b)
    {
        return ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);

        if (lengthSquared <= float.Epsilon)
        {
            return Vector2.Distance(p, a);
        }

        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private static float TriangleXyArea(in Triangle triangle)
    {
        var ax = triangle.A.X;
        var ay = triangle.A.Y;

        return 0.5f * MathF.Abs(((triangle.B.X - ax) * (triangle.C.Y - ay)) - ((triangle.C.X - ax) * (triangle.B.Y - ay)));
    }

    private static void AppendRoadwayTriangles(
        List<Triangle> triangles,
        ImportedMesh mesh,
        in Matrix4x4 transform,
        in Vector3 anchorStart,
        in Vector3 anchorEnd,
        in Vector3 axisDirection,
        float axisLength,
        float bandMax)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;

        for (var index = 0; index + 2 < indices.Count; index += 3)
        {
            var a = Vector3.Transform(vertices[indices[index]].Position, transform);
            var b = Vector3.Transform(vertices[indices[index + 1]].Position, transform);
            var c = Vector3.Transform(vertices[indices[index + 2]].Position, transform);

            // The cross of world-space edge vectors is already the world-space face normal
            // (mirrors included through the transformed vertex positions); normalizing the
            // transformed positions' cross a second time would rotate it by the bone matrix
            // again and scramble horizontal roadway faces into near-vertical ones.
            var geometricNormal = Vector3.Cross(b - a, c - a);
            var geometricLength = geometricNormal.Length();

            if (geometricLength <= float.Epsilon)
            {
                continue;
            }

            if (MathF.Abs(geometricNormal.Z / geometricLength) <= HorizontalNormalZ)
            {
                continue;
            }

            var centroid = (a + b + c) / 3f;
            var anchorT = Math.Clamp(Vector3.Dot(centroid - anchorStart, axisDirection) / axisLength, 0f, 1f);
            var anchorLineZ = anchorStart.Z + ((anchorEnd.Z - anchorStart.Z) * anchorT);
            var anchorRelativeZ = centroid.Z - anchorLineZ;

            // Arch undersides and bracing sit well below the roadway at the same XY; the
            // topmost-containing-triangle query resolves those overlaps to the roadway.
            if (anchorRelativeZ > RoadwayBandMin && anchorRelativeZ < bandMax)
            {
                triangles.Add(new Triangle(a, b, c));
            }
        }
    }

    private static BridgeSurface Build(List<Triangle> triangles)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        var buckets = new Dictionary<(int X, int Y), List<int>>();

        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var triangleMin = Vector2.Min(Vector2.Min(new Vector2(triangle.A.X, triangle.A.Y), new Vector2(triangle.B.X, triangle.B.Y)), new Vector2(triangle.C.X, triangle.C.Y));
            var triangleMax = Vector2.Max(Vector2.Max(new Vector2(triangle.A.X, triangle.A.Y), new Vector2(triangle.B.X, triangle.B.Y)), new Vector2(triangle.C.X, triangle.C.Y));

            min = Vector2.Min(min, triangleMin);
            max = Vector2.Max(max, triangleMax);

            var startX = (int)MathF.Floor(triangleMin.X / BucketSize);
            var endX = (int)MathF.Floor(triangleMax.X / BucketSize);
            var startY = (int)MathF.Floor(triangleMin.Y / BucketSize);
            var endY = (int)MathF.Floor(triangleMax.Y / BucketSize);

            for (var bucketX = startX; bucketX <= endX; bucketX++)
            {
                for (var bucketY = startY; bucketY <= endY; bucketY++)
                {
                    var key = (bucketX, bucketY);
                    if (!buckets.TryGetValue(key, out var list))
                    {
                        list = [];
                        buckets[key] = list;
                    }

                    list.Add(index);
                }
            }
        }

        return new BridgeSurface(triangles, min, max, buckets);
    }

    /// <summary>True when the XY point is inside the surface's world extent.</summary>
    public bool Contains(float x, float y)
    {
        return x >= _extentMin.X
            && x <= _extentMax.X
            && y >= _extentMin.Y
            && y <= _extentMax.Y;
    }

    /// <summary>
    /// Topmost roadway height at the XY point, interpolated on the containing triangle's
    /// plane; null when no roadway triangle covers the point.
    /// </summary>
    public float? GetSurfaceZ(float x, float y)
    {
        if (!Contains(x, y))
        {
            return null;
        }

        var bucketX = (int)MathF.Floor(x / BucketSize);
        var bucketY = (int)MathF.Floor(y / BucketSize);

        if (!_buckets.TryGetValue((bucketX, bucketY), out var candidates))
        {
            return null;
        }

        float? topmost = null;

        foreach (var index in candidates)
        {
            var triangle = _triangles[index];

            if (triangle.TryGetZ(x, y, out var z) && (topmost is null || z > topmost.Value))
            {
                topmost = z;
            }
        }

        return topmost;
    }

    private readonly struct Triangle
    {
        public Triangle(in Vector3 a, in Vector3 b, in Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }

        public Vector3 A { get; }

        public Vector3 B { get; }

        public Vector3 C { get; }

        /// <summary>
        /// Planar height at the XY point inside this triangle via barycentric interpolation.
        /// Boundary-inclusive so queries landing exactly on triangle edges resolve.
        /// </summary>
        public bool TryGetZ(float x, float y, out float z)
        {
            var denominator = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);

            if (denominator == 0f || !float.IsFinite(denominator))
            {
                z = 0f;
                return false;
            }

            var weightA = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / denominator;
            var weightB = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / denominator;
            var weightC = 1f - weightA - weightB;

            // Sign-tolerant inclusion: negative denominators flip all three weights together.
            if (weightA < -0.001f || weightB < -0.001f || weightC < -0.001f)
            {
                z = 0f;
                return false;
            }

            z = (A.Z * weightA) + (B.Z * weightB) + (C.Z * weightC);
            return true;
        }
    }
}
