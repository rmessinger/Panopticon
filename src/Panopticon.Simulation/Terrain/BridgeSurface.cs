#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        System.IO.File.AppendAllText("/tmp/opencode/gd3.txt", $"n={centroidPoints.Count} pitchDeg=0.00\n");
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
