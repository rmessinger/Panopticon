using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Projection;

[assembly: InternalsVisibleTo("Panopticon.Geospatial.Tests")]

namespace Panopticon.Geospatial.Features;

/// <summary>
/// Transforms vector features into local game coordinates and clips them to the full
/// chunk-rounded map rectangle (WI-B4b-15); the playable-area polygon is applied later by
/// terrain masking. NetTopologySuite backs the polygon overlay and stays private to this
/// class.
/// </summary>
public static class FeatureClipper
{
    /// <summary>Maximum multi geometry nesting, matching the KML reader's bound.</summary>
    public const int MaxGeometryNestingDepth = 128;

    /// <summary>
    /// Projects each feature into local game coordinates, clips it to the chunk-rounded map
    /// rectangle, and returns the surviving parts with stable identities; boundary points are
    /// kept and fully outside features are dropped with an Info diagnostic. The clipping,
    /// normalization, altitude, and resource-limit contracts live in B4b deep dive 5.3 and 7.
    /// </summary>
    public static IReadOnlyList<LocalFeature> Clip(
        IEnumerable<GeospatialFeature> features,
        LocalMapGrid grid,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        return Clip(features, grid, transformer, diagnostics, limits, cancellationToken, beforeOverlay: null);
    }

    /// <summary>
    /// Test entry: <paramref name="beforeOverlay"/> runs inside the polygon overlay's
    /// failure-translation boundary, so adapter translation is testable per call. Public
    /// <see cref="Clip"/> passes null; there is no static test state.
    /// </summary>
    internal static IReadOnlyList<LocalFeature> Clip(
        IEnumerable<GeospatialFeature> features,
        LocalMapGrid grid,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits,
        CancellationToken cancellationToken,
        Action? beforeOverlay)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(transformer);
        ArgumentNullException.ThrowIfNull(diagnostics);
        limits ??= new GeospatialImportLimits();

        var context = new ClipContext(grid, transformer, diagnostics, limits, cancellationToken, beforeOverlay);
        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(feature);
            context.ClipFeature(feature);

            if (context.Stopped)
            {
                break;
            }
        }

        return context.Results;
    }

    private enum InputCountStatus
    {
        WithinBounds,

        CountExceeded,

        TooDeep,
    }

    private sealed class ClipContext
    {
        private readonly ICoordinateTransformer _transformer;

        private readonly GeoDiagnosticBag _diagnostics;

        private readonly GeospatialImportLimits _limits;

        private readonly CancellationToken _cancellationToken;

        private readonly Action? _beforeOverlay;

        private readonly List<LocalFeature> _results = [];

        private readonly IReadOnlyList<LocalFeature> _resultsView;

        private readonly List<GeoGeometry> _parts = [];

        private readonly Dictionary<(double X, double Y), double> _polygonAltitudes = [];

        private readonly GeometryFactory _factory = GeometryFactory.Default;

        private readonly Polygon _mapRectangle;

        private readonly List<GeoPosition> _ringVertices = [];

        private long _emittedPositions;

        private long _inputCoordinates;

        private long _inputFeatureCount;

        private string _currentFeatureId = string.Empty;

        private bool _featureErrored;

        private bool _featureTouched;

        public ClipContext(
            LocalMapGrid grid,
            ICoordinateTransformer transformer,
            GeoDiagnosticBag diagnostics,
            GeospatialImportLimits limits,
            CancellationToken cancellationToken,
            Action? beforeOverlay)
        {
            Grid = grid;
            _transformer = transformer;
            _diagnostics = diagnostics;
            _limits = limits;
            _cancellationToken = cancellationToken;
            _beforeOverlay = beforeOverlay;
            _resultsView = _results.AsReadOnly();
            MapWidthGameUnits = grid.ChunksX * (double)ChunkLayout.ChunkSpanGameUnits;
            MapHeightGameUnits = grid.ChunksY * (double)ChunkLayout.ChunkSpanGameUnits;
            _mapRectangle = (Polygon)_factory.ToGeometry(new Envelope(0d, MapWidthGameUnits, 0d, MapHeightGameUnits));
        }

        public LocalMapGrid Grid { get; }

        public double MapWidthGameUnits { get; }

        public double MapHeightGameUnits { get; }

        // A read-only view: callers must not be able to cast back to the working list.
        public IReadOnlyList<LocalFeature> Results => _resultsView;

        public bool Stopped { get; private set; }

        public void ClipFeature(GeospatialFeature feature)
        {
            _parts.Clear();
            _currentFeatureId = feature.FeatureId;
            _featureErrored = false;
            _featureTouched = false;

            // Input bounds are checked before any geometry copy or NTS allocation, so
            // oversized features cost one bounded counting pass, not a clipped copy.
            if (!InputWithinBounds(feature))
            {
                return;
            }

            if (!TryProjectGeometry(feature.Geometry, feature.CoordinateSystem, 0, out var local))
            {
                return;
            }

            if (!ClipLocalGeometry(local))
            {
                return;
            }

            // A feature that already failed with an error is not additionally reported as
            // dropped.
            if (_parts.Count == 0 && !_featureErrored)
            {
                AddDroppedFeatureInfo(feature);
                return;
            }

            EmitParts(feature);
        }

        private bool InputWithinBounds(GeospatialFeature feature)
        {
            if (_inputFeatureCount >= _limits.MaxFeatureCount)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureOutputLimitExceeded,
                    GeoStage.Generate,
                    $"Input feature count exceeded the maxFeatureCount limit {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)} at feature '{feature.FeatureId}'.");
                Stopped = true;
                return false;
            }

            _inputFeatureCount++;

            // The input coordinate budget is total across the whole enumeration, charged
            // for outside and errored features too; a per-feature reset would allow
            // maxFeatureCount * maxCoordinateCount units of work with no output at all.
            var status = CountInputCoordinates(feature.Geometry, 0);
            if (status == InputCountStatus.CountExceeded)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureOutputLimitExceeded,
                    GeoStage.Generate,
                    $"Total input coordinates exceeded the maxCoordinateCount limit {_limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)} at feature '{feature.FeatureId}'.");
                Stopped = true;
                return false;
            }

            return status == InputCountStatus.WithinBounds;
        }

        private InputCountStatus CountInputCoordinates(GeoGeometry geometry, int depth)
        {
            if (depth >= MaxGeometryNestingDepth)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureNestingTooDeep,
                    GeoStage.Parse,
                    $"Feature '{_currentFeatureId}' nests multi geometry deeper than the maximum depth {MaxGeometryNestingDepth.ToString(CultureInfo.InvariantCulture)}.");
                return InputCountStatus.TooDeep;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            _inputCoordinates += geometry switch
            {
                GeoPointGeometry => 1L,
                GeoLineString line => line.Positions.Count,
                GeoPolygon polygon => CountRingPositions(polygon),
                _ => 0L,
            };

            // Early exit keeps the pass bounded: the rest of an oversized feature is not
            // traversed and no copy is made.
            if (_inputCoordinates > _limits.MaxCoordinateCount)
            {
                return InputCountStatus.CountExceeded;
            }

            if (geometry is GeoMultiGeometry multi)
            {
                for (var i = 0; i < multi.Geometries.Count; i++)
                {
                    var childStatus = CountInputCoordinates(multi.Geometries[i], depth + 1);
                    if (childStatus != InputCountStatus.WithinBounds)
                    {
                        return childStatus;
                    }
                }
            }

            return InputCountStatus.WithinBounds;
        }

        private static long CountRingPositions(GeoPolygon polygon)
        {
            var count = (long)polygon.OuterRing.Positions.Count;
            for (var i = 0; i < polygon.InnerRings.Count; i++)
            {
                count += polygon.InnerRings[i].Positions.Count;
            }

            return count;
        }

        private bool TryProjectGeometry(GeoGeometry geometry, CoordinateSystemId source, int depth, out GeoGeometry local)
        {
            local = null!;

            if (geometry is GeoPointGeometry point)
            {
                if (!TryProjectPosition(point.Position, source, out var position))
                {
                    return false;
                }

                local = new GeoPointGeometry(position);
                return true;
            }

            if (geometry is GeoLineString line)
            {
                var positions = new GeoPosition[line.Positions.Count];
                for (var i = 0; i < line.Positions.Count; i++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    if (!TryProjectPosition(line.Positions[i], source, out positions[i]))
                    {
                        return false;
                    }
                }

                local = new GeoLineString(positions);
                return true;
            }

            if (geometry is GeoPolygon polygon)
            {
                if (!TryProjectRing(polygon.OuterRing, source, out var outerRing))
                {
                    return false;
                }

                var innerRings = new GeoRing[polygon.InnerRings.Count];
                for (var i = 0; i < polygon.InnerRings.Count; i++)
                {
                    if (!TryProjectRing(polygon.InnerRings[i], source, out innerRings[i]))
                    {
                        return false;
                    }
                }

                local = new GeoPolygon(outerRing, innerRings);
                return true;
            }

            if (geometry is GeoMultiGeometry multi)
            {
                var children = new GeoGeometry[multi.Geometries.Count];
                for (var i = 0; i < multi.Geometries.Count; i++)
                {
                    if (!TryProjectGeometry(multi.Geometries[i], source, depth + 1, out children[i]))
                    {
                        return false;
                    }
                }

                local = new GeoMultiGeometry(children);
                return true;
            }

            AddError(
                FeatureDiagnosticCodes.FeatureUnsupportedGeometry,
                GeoStage.Parse,
                $"Feature '{_currentFeatureId}' has geometry kind {geometry.GetType().Name}, which the clipper does not support.");
            return false;
        }

        private bool TryProjectRing(GeoRing ring, CoordinateSystemId source, out GeoRing projected)
        {
            var positions = new GeoPosition[ring.Positions.Count];
            for (var i = 0; i < ring.Positions.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (!TryProjectPosition(ring.Positions[i], source, out positions[i]))
                {
                    projected = null!;
                    return false;
                }
            }

            projected = new GeoRing(positions);
            return true;
        }

        private bool TryProjectPosition(GeoPosition position, CoordinateSystemId source, out GeoPosition local)
        {
            local = default;

            if (!double.IsFinite(position.Coordinate.X) || !double.IsFinite(position.Coordinate.Y))
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureNonFiniteCoordinate,
                    GeoStage.Coordinates,
                    $"Feature '{_currentFeatureId}' contains a coordinate that is not finite.");
                return false;
            }

            if (position.AltitudeMeters is { } altitude && !double.IsFinite(altitude))
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureNonFiniteCoordinate,
                    GeoStage.Coordinates,
                    $"Feature '{_currentFeatureId}' contains an altitude that is not finite.");
                return false;
            }

            if (source == Grid.ProjectedCoordinateSystem)
            {
                return FinishProject(position.Coordinate, position, out local);
            }

            var target = Grid.ProjectedCoordinateSystem;
            Vector2d projected;
            if (_transformer is WgsTransverseMercatorTransformer concrete)
            {
                if (!concrete.TryTransform(position.Coordinate, source, target, out projected, out var diagnostic))
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureProjectionFailed,
                        GeoStage.Coordinates,
                        $"Coordinate transform from {source} to {target} failed for feature '{_currentFeatureId}': {diagnostic.Message}",
                        diagnostic.Detail);
                    return false;
                }
            }
            else
            {
                try
                {
                    projected = _transformer.Transform(position.Coordinate, source, target);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureProjectionFailed,
                        GeoStage.Coordinates,
                        $"Coordinate transform from {source} to {target} failed for feature '{_currentFeatureId}': {ex.Message}",
                        ex);
                    return false;
                }
            }

            return FinishProject(projected, position, out local);
        }

        private bool FinishProject(Vector2d projected, GeoPosition position, out GeoPosition local)
        {
            var game = Grid.ProjectedToGame(projected);
            if (!double.IsFinite(game.X) || !double.IsFinite(game.Y))
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureProjectionFailed,
                    GeoStage.Coordinates,
                    $"Coordinate transform into {Grid.ProjectedCoordinateSystem} produced a non-finite local coordinate for feature '{_currentFeatureId}'.");
                local = default;
                return false;
            }

            local = new GeoPosition(game, position.AltitudeMeters);
            return true;
        }

        private bool ClipLocalGeometry(GeoGeometry geometry)
        {
            if (geometry is GeoPointGeometry point)
            {
                if (ContainsInclusive(point.Position.Coordinate))
                {
                    _parts.Add(point);
                }

                return true;
            }

            if (geometry is GeoLineString line)
            {
                return ClipLine(line);
            }

            if (geometry is GeoPolygon polygon)
            {
                return ClipPolygon(polygon);
            }

            if (geometry is GeoMultiGeometry multi)
            {
                for (var i = 0; i < multi.Geometries.Count; i++)
                {
                    if (!ClipLocalGeometry(multi.Geometries[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool ContainsInclusive(Vector2d coordinate)
        {
            return coordinate.X >= 0d
                && coordinate.X <= MapWidthGameUnits
                && coordinate.Y >= 0d
                && coordinate.Y <= MapHeightGameUnits;
        }

        private bool ClipLine(GeoLineString line)
        {
            var positions = line.Positions;
            List<GeoPosition>? run = null;

            void Flush()
            {
                if (run is { Count: >= 2 })
                {
                    _parts.Add(new GeoLineString(run));
                }

                run = null;
            }

            for (var i = 0; i + 1 < positions.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var a = positions[i];
                var b = positions[i + 1];

                // A finite endpoint pair can still overflow its delta; Liang-Barsky on an
                // infinite delta silently misclassifies the segment, so reject it first.
                var dx = b.Coordinate.X - a.Coordinate.X;
                var dy = b.Coordinate.Y - a.Coordinate.Y;
                if (!double.IsFinite(dx) || !double.IsFinite(dy))
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureClipArithmeticOverflow,
                        GeoStage.Coordinates,
                        $"Clipping a segment of feature '{_currentFeatureId}' overflowed to a non-finite segment delta from finite input.");
                    return false;
                }

                if (!TryClipSegment(a, b, dx, dy, out var t0, out var t1) || t1 <= t0)
                {
                    if (t1 == t0)
                    {
                        _featureTouched = true;
                    }

                    Flush();
                    continue;
                }

                var enter = Interpolate(a, b, t0);
                var exit = Interpolate(a, b, t1);
                if (!IsFinite(enter) || !IsFinite(exit))
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureClipArithmeticOverflow,
                        GeoStage.Coordinates,
                        $"Clipping a segment of feature '{_currentFeatureId}' produced a non-finite local coordinate from finite input.");
                    return false;
                }

                if (run is null)
                {
                    run = [enter];
                }
                else if (run[^1].Coordinate != enter.Coordinate)
                {
                    Flush();
                    run = [enter];
                }

                if (run[^1].Coordinate != exit.Coordinate)
                {
                    run.Add(exit);
                }

                if (t1 < 1d)
                {
                    Flush();
                }
            }

            Flush();
            return true;
        }

        private bool TryClipSegment(GeoPosition a, GeoPosition b, double dx, double dy, out double t0, out double t1)
        {
            // Liang-Barsky against the map rectangle.
            t0 = 0d;
            t1 = 1d;

            return ClipEdge(-dx, a.Coordinate.X, ref t0, ref t1)
                && ClipEdge(dx, MapWidthGameUnits - a.Coordinate.X, ref t0, ref t1)
                && ClipEdge(-dy, a.Coordinate.Y, ref t0, ref t1)
                && ClipEdge(dy, MapHeightGameUnits - a.Coordinate.Y, ref t0, ref t1);
        }

        private static bool ClipEdge(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0d)
            {
                return q >= 0d;
            }

            var r = q / p;
            if (p < 0d)
            {
                if (r > t1)
                {
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }

        private static GeoPosition Interpolate(GeoPosition a, GeoPosition b, double t)
        {
            var x = a.Coordinate.X + (t * (b.Coordinate.X - a.Coordinate.X));
            var y = a.Coordinate.Y + (t * (b.Coordinate.Y - a.Coordinate.Y));
            double? altitude = a.AltitudeMeters is { } from && b.AltitudeMeters is { } to
                ? from + (t * (to - from))
                : null;

            return new GeoPosition(new Vector2d(x, y), altitude);
        }

        private static bool IsFinite(GeoPosition position)
        {
            if (!double.IsFinite(position.Coordinate.X) || !double.IsFinite(position.Coordinate.Y))
            {
                return false;
            }

            return position.AltitudeMeters is not { } altitude || double.IsFinite(altitude);
        }

        private bool ClipPolygon(GeoPolygon polygon)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (!TryBuildNtsPolygon(polygon, out var ntsPolygon))
            {
                return false;
            }

            _polygonAltitudes.Clear();
            CollectAltitudes(polygon.OuterRing);
            for (var i = 0; i < polygon.InnerRings.Count; i++)
            {
                CollectAltitudes(polygon.InnerRings[i]);
            }

            Geometry intersection;
            try
            {
                _beforeOverlay?.Invoke();
                intersection = ntsPolygon.Intersection(_mapRectangle);
            }
            catch (Exception ex) when (ex is TopologyException or ArgumentException)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureOverlayFailed,
                    GeoStage.Generate,
                    $"Polygon overlay failed for feature '{_currentFeatureId}': {ex.Message}",
                    ex);
                return false;
            }

            var pieces = new List<Polygon>();
            CollectPolygonParts(intersection, pieces);

            var parts = new List<GeoPolygon>(pieces.Count);
            foreach (var piece in pieces)
            {
                if (ToCanonicalPolygon(piece) is { } part)
                {
                    parts.Add(part);
                }
            }

            if (parts.Count == 0 && !intersection.IsEmpty)
            {
                // The overlay produced only zero-dimensional or linear touch leftovers.
                _featureTouched = true;
            }

            // Sorting here and in the hole list keeps the output independent of the
            // overlay engine's traversal order.
            parts.Sort(CompareCanonicalPolygons);
            foreach (var part in parts)
            {
                _parts.Add(part);
            }

            return true;
        }

        private bool TryBuildNtsPolygon(GeoPolygon polygon, out Polygon ntsPolygon)
        {
            ntsPolygon = null!;
            var shell = ToRingCoordinates(polygon.OuterRing);
            if (shell is null)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureInvalidTopology,
                    GeoStage.Generate,
                    "A polygon outer ring has fewer than three distinct positions.");
                return false;
            }

            var holes = new Coordinate[polygon.InnerRings.Count][];
            for (var i = 0; i < polygon.InnerRings.Count; i++)
            {
                var hole = ToRingCoordinates(polygon.InnerRings[i]);
                if (hole is null)
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureInvalidTopology,
                        GeoStage.Generate,
                        $"A polygon hole ring (index {i.ToString(CultureInfo.InvariantCulture)}) has fewer than three distinct positions.");
                    return false;
                }

                holes[i] = hole;
            }

            Polygon candidate;
            IsValidOp validator;
            try
            {
                candidate = _factory.CreatePolygon(
                    _factory.CreateLinearRing(shell),
                    ToLinearRings(holes));
                validator = new IsValidOp(candidate);
                _ = validator.IsValid;
            }
            catch (Exception ex) when (ex is TopologyException or ArgumentException)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureOverlayFailed,
                    GeoStage.Generate,
                    $"Polygon conversion failed for feature '{_currentFeatureId}': {ex.Message}",
                    ex);
                return false;
            }

            // Invalid input (self-intersection, bad ring nesting) is reported, never
            // silently repaired.
            if (validator.ValidationError is { } error)
            {
                AddError(
                    FeatureDiagnosticCodes.FeatureInvalidTopology,
                    GeoStage.Generate,
                    $"Polygon topology is invalid ({error.Message}); rework the source geometry.");
                return false;
            }

            ntsPolygon = candidate;
            return true;
        }

        private LinearRing[] ToLinearRings(Coordinate[][] holes)
        {
            var rings = new LinearRing[holes.Length];
            for (var i = 0; i < holes.Length; i++)
            {
                rings[i] = _factory.CreateLinearRing(holes[i]);
            }

            return rings;
        }

        private static Coordinate[]? ToRingCoordinates(GeoRing ring)
        {
            // Returns a closed NTS ring over the distinct vertices, or null when the ring
            // is too small to bound an area. Consecutive duplicates and the closing
            // position are representation details, not topology, and are dropped.
            List<Coordinate> opened = [];
            foreach (var position in ring.Positions)
            {
                var coordinate = new Coordinate(position.Coordinate.X, position.Coordinate.Y);
                if (opened.Count == 0 || !opened[^1].Equals(coordinate))
                {
                    opened.Add(coordinate);
                }
            }

            if (opened.Count > 1 && opened[0].Equals(opened[^1]))
            {
                opened.RemoveAt(opened.Count - 1);
            }

            if (opened.Count < 3)
            {
                return null;
            }

            opened.Add(opened[0]);
            return [.. opened];
        }

        private void CollectAltitudes(GeoRing ring)
        {
            foreach (var position in ring.Positions)
            {
                if (position.AltitudeMeters is { } altitude)
                {
                    _polygonAltitudes.TryAdd((position.Coordinate.X, position.Coordinate.Y), altitude);
                }
            }
        }

        private static void CollectPolygonParts(Geometry geometry, List<Polygon> parts)
        {
            if (geometry is Polygon polygon && !polygon.IsEmpty && polygon.Area > 0d)
            {
                parts.Add(polygon);
            }
            else if (geometry is GeometryCollection collection)
            {
                for (var i = 0; i < collection.NumGeometries; i++)
                {
                    CollectPolygonParts(collection.GetGeometryN(i), parts);
                }
            }

            // Zero-dimensional and linear leftovers from boundary touches are dropped; the
            // feature-level diagnostic classifies the feature.
        }

        private GeoPolygon? ToCanonicalPolygon(Polygon piece)
        {
            var outerRing = ToCanonicalRing(piece.ExteriorRing.Coordinates, exterior: true);
            if (outerRing is null)
            {
                return null;
            }

            var innerRings = new List<GeoRing>(piece.NumInteriorRings);
            for (var i = 0; i < piece.NumInteriorRings; i++)
            {
                if (ToCanonicalRing(piece.GetInteriorRingN(i).Coordinates, exterior: false) is { } hole)
                {
                    innerRings.Add(hole);
                }
            }

            innerRings.Sort(CompareRings);
            return new GeoPolygon(outerRing, innerRings);
        }

        private GeoRing? ToCanonicalRing(Coordinate[] coordinates, bool exterior)
        {
            _ringVertices.Clear();
            foreach (var coordinate in coordinates)
            {
                if (_ringVertices.Count > 0
                    && _ringVertices[^1].Coordinate.X == coordinate.X
                    && _ringVertices[^1].Coordinate.Y == coordinate.Y)
                {
                    continue;
                }

                // Vertices the clip introduces have unknown altitude (null), never an
                // invented zero.
                _ringVertices.Add(
                    new GeoPosition(new Vector2d(coordinate.X, coordinate.Y), AltitudeAt(coordinate)));
            }

            if (_ringVertices.Count > 1 && SameCoordinate(_ringVertices[0], _ringVertices[^1]))
            {
                _ringVertices.RemoveAt(_ringVertices.Count - 1);
            }

            if (_ringVertices.Count < 3)
            {
                return null;
            }

            // Canonical winding first (exterior counter-clockwise, holes clockwise), then
            // rotate so the lexicographically smallest vertex leads.
            var counterClockwise = SignedTwiceArea(_ringVertices) > 0d;
            if (counterClockwise != exterior)
            {
                _ringVertices.Reverse();
            }

            var start = 0;
            for (var i = 1; i < _ringVertices.Count; i++)
            {
                if (CompareCoordinate(_ringVertices[i].Coordinate, _ringVertices[start].Coordinate) < 0)
                {
                    start = i;
                }
            }

            var closed = new GeoPosition[_ringVertices.Count + 1];
            for (var i = 0; i < _ringVertices.Count; i++)
            {
                closed[i] = _ringVertices[(start + i) % _ringVertices.Count];
            }

            closed[^1] = closed[0];
            return new GeoRing(closed);
        }

        private double? AltitudeAt(Coordinate coordinate)
        {
            return _polygonAltitudes.TryGetValue((coordinate.X, coordinate.Y), out var altitude) ? altitude : null;
        }

        private static bool SameCoordinate(GeoPosition a, GeoPosition b)
        {
            return a.Coordinate.X == b.Coordinate.X && a.Coordinate.Y == b.Coordinate.Y;
        }

        private static double SignedTwiceArea(List<GeoPosition> vertices)
        {
            // Translating avoids cancellation at large local origins.
            var origin = vertices[0].Coordinate;
            var twiceArea = 0d;
            for (var i = 1; i + 1 < vertices.Count; i++)
            {
                var ax = vertices[i].Coordinate.X - origin.X;
                var ay = vertices[i].Coordinate.Y - origin.Y;
                var bx = vertices[i + 1].Coordinate.X - origin.X;
                var by = vertices[i + 1].Coordinate.Y - origin.Y;
                twiceArea += (ax * by) - (bx * ay);
            }

            return twiceArea;
        }

        private static int CompareCoordinate(Vector2d a, Vector2d b)
        {
            var byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Y.CompareTo(b.Y);
        }

        private static int CompareRings(GeoRing a, GeoRing b)
        {
            var byCount = a.Positions.Count.CompareTo(b.Positions.Count);
            if (byCount != 0)
            {
                return byCount;
            }

            for (var i = 0; i < a.Positions.Count; i++)
            {
                var comparison = CompareCoordinate(a.Positions[i].Coordinate, b.Positions[i].Coordinate);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static int CompareCanonicalPolygons(GeoPolygon a, GeoPolygon b)
        {
            RingBounds(a.OuterRing, out var aBounds);
            RingBounds(b.OuterRing, out var bBounds);
            var byMinX = aBounds.MinX.CompareTo(bBounds.MinX);
            if (byMinX != 0)
            {
                return byMinX;
            }

            var byMinY = aBounds.MinY.CompareTo(bBounds.MinY);
            if (byMinY != 0)
            {
                return byMinY;
            }

            var byMaxX = aBounds.MaxX.CompareTo(bBounds.MaxX);
            if (byMaxX != 0)
            {
                return byMaxX;
            }

            var byMaxY = aBounds.MaxY.CompareTo(bBounds.MaxY);
            if (byMaxY != 0)
            {
                return byMaxY;
            }

            return CompareRings(a.OuterRing, b.OuterRing);
        }

        private static void RingBounds(GeoRing ring, out (double MinX, double MinY, double MaxX, double MaxY) bounds)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            foreach (var position in ring.Positions)
            {
                minX = Math.Min(minX, position.Coordinate.X);
                minY = Math.Min(minY, position.Coordinate.Y);
                maxX = Math.Max(maxX, position.Coordinate.X);
                maxY = Math.Max(maxY, position.Coordinate.Y);
            }

            bounds = (minX, minY, maxX, maxY);
        }

        private void EmitParts(GeospatialFeature feature)
        {
            for (var i = 0; i < _parts.Count; i++)
            {
                if (_results.Count >= _limits.MaxFeatureCount)
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureOutputLimitExceeded,
                        GeoStage.Generate,
                        $"Clipping feature '{feature.FeatureId}' exceeded the maxFeatureCount limit {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}.");
                    Stopped = true;
                    return;
                }

                var positions = CountPositions(_parts[i]);
                if (_emittedPositions + positions > _limits.MaxCoordinateCount)
                {
                    AddError(
                        FeatureDiagnosticCodes.FeatureOutputLimitExceeded,
                        GeoStage.Generate,
                        $"Clipping feature '{feature.FeatureId}' exceeded the maxCoordinateCount limit {_limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");
                    Stopped = true;
                    return;
                }

                _emittedPositions += positions;
                _results.Add(
                    new LocalFeature(
                        _parts[i],
                        feature,
                        feature.SourceId + "/" + feature.FeatureId + "#" + i.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static long CountPositions(GeoGeometry geometry)
        {
            if (geometry is GeoPointGeometry)
            {
                return 1;
            }

            if (geometry is GeoLineString line)
            {
                return line.Positions.Count;
            }

            if (geometry is GeoPolygon polygon)
            {
                return CountRingPositions(polygon);
            }

            if (geometry is GeoMultiGeometry multi)
            {
                long count = 0;
                for (var i = 0; i < multi.Geometries.Count; i++)
                {
                    count += CountPositions(multi.Geometries[i]);
                }

                return count;
            }

            return 0;
        }

        private void AddDroppedFeatureInfo(GeospatialFeature feature)
        {
            if (_featureTouched)
            {
                AddInfo(
                    FeatureDiagnosticCodes.FeatureDegenerateAfterClip,
                    $"Feature '{feature.FeatureId}' only touches the map rectangle boundary and left no clipped geometry.");
            }
            else
            {
                AddInfo(
                    FeatureDiagnosticCodes.FeatureDroppedOutside,
                    $"Feature '{feature.FeatureId}' lies entirely outside the map rectangle and was dropped.");
            }
        }

        private void AddError(string code, GeoStage stage, string message, Exception? detail = null)
        {
            _featureErrored = true;
            _diagnostics.Add(new GeoDiagnostic(code, stage, GeoSeverity.Error, message, detail: detail));
        }

        private void AddInfo(string code, string message)
        {
            _diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Info, message));
        }
    }
}
