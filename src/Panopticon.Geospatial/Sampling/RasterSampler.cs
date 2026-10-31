using System;
using System.Collections.Generic;
using System.Globalization;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Sampling;

/// <summary>
/// Deterministic, CPU-only sampler for band-interleaved <see cref="GeospatialRaster{Double}"/>
/// data. The sample center is passed in source-CRS coordinates (the caller inverse-transforms
/// the output position exactly once); the <see cref="SampleFootprint"/> rectangle in target
/// projected meters is mapped here into source pixel space; no intermediate warped raster is
/// created. Distances, GSD comparisons, and search radii are target-projected meters,
/// including for geographic sources. Source transforms must be axis-aligned; the target CRS
/// may still rotate or warp the sampling direction, so kernel selection and reporting use the
/// footprint quad's extents in source pixel space. Coverage outside the raster integrates
/// over virtual source cells weighted by overlap area: edge-nearest fills each virtual cell
/// from the clamped nearest in-raster cell, and that edge sample then passes the no-data
/// policy.
/// </summary>
public sealed class RasterSampler
{
    private const double CellHalf = 0.5d;

    private readonly ICoordinateTransformer _transformer;

    public RasterSampler(ICoordinateTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        _transformer = transformer;
    }

    /// <summary>
    /// Reports the local source ground-sample distance per axis (adjacent source cell centers,
    /// measured through the affine even past the raster edge on single-cell axes, in target
    /// meters), the effective output spacing along each source axis (the footprint quad's
    /// cell-space extent times that axis's GSD), and the effective kernel:
    /// <see cref="SampleKernel.Auto"/> selects bilinear only when the quad spans at most one
    /// source cell on each axis, area-weighted when either axis spans more. Other kernels
    /// pass through.
    /// </summary>
    public KernelSelection SelectKernel(
        GeospatialRaster<double> raster,
        double sourceX,
        double sourceY,
        SampleFootprint footprint,
        SampleKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(raster);
        ArgumentNullException.ThrowIfNull(footprint);
        ThrowIfSheared(raster);

        if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY))
        {
            throw new ArgumentException("The sample center must be finite source-CRS coordinates.");
        }

        if (!Enum.IsDefined(kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Undefined kernel value.");
        }

        var run = CreateRun(raster, band: 0, footprint, new SamplingOptions());
        var centerCell = ToCellSpace(raster.Transform.WorldToPixel(new Vector2d(sourceX, sourceY)));

        return BuildSelection(run, footprint, kernel, centerCell);
    }

    /// <summary>
    /// Samples one band value with fixed row-major iteration and double accumulation; there is
    /// no parallelism inside one sample. Failures surface as typed error diagnostics on a failed
    /// result; the value is never silently NaN. Fill events (no-data and outside coverage) are
    /// counted on the result and recorded into <paramref name="stats"/> when supplied.
    /// </summary>
    public RasterSampleResult Sample(
        GeospatialRaster<double> raster,
        int band,
        double sourceX,
        double sourceY,
        SampleFootprint footprint,
        SampleKernel kernel,
        SamplingOptions? options = null,
        SamplingStats? stats = null)
    {
        ArgumentNullException.ThrowIfNull(raster);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(band, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(band, raster.BandCount);

        if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY))
        {
            throw new ArgumentException("The sample center must be finite source-CRS coordinates.");
        }

        if (!Enum.IsDefined(kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Undefined kernel value.");
        }

        options ??= new SamplingOptions();

        RasterSampleResult result;

        try
        {
            result = SampleCore(raster, band, sourceX, sourceY, footprint, kernel, options);
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException)
        {
            result = TransformFailure(band, ex);
        }

        stats?.Record(result, footprint);

        return result;
    }

    private static void ThrowIfSheared(GeospatialRaster<double> raster)
    {
        if (raster.Transform.RowOffsetX != 0d || raster.Transform.ColumnOffsetY != 0d)
        {
            throw new ArgumentException(
                "The source transform must be axis-aligned (no rotation or shear); rowOffsetX and columnOffsetY must be zero.");
        }
    }

    private RasterSampleResult SampleCore(
        GeospatialRaster<double> raster,
        int band,
        double sourceX,
        double sourceY,
        SampleFootprint footprint,
        SampleKernel kernel,
        SamplingOptions options)
    {
        var run = CreateRun(raster, band, footprint, options);
        var centerCell = ToCellSpace(raster.Transform.WorldToPixel(new Vector2d(sourceX, sourceY)));

        if (!double.IsFinite(centerCell.X) || !double.IsFinite(centerCell.Y))
        {
            return run.Fail(
                null,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {band}: the sample center maps to a non-finite source pixel position.");
        }

        if (raster.Transform.RowOffsetX != 0d || raster.Transform.ColumnOffsetY != 0d)
        {
            return run.Fail(
                null,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {band}: the source transform must be axis-aligned (no rotation or shear); "
                + "rowOffsetX and columnOffsetY must be zero.");
        }

        var selection = BuildSelection(run, footprint, kernel, centerCell);

        return selection.Kernel switch
        {
            SampleKernel.Nearest => SampleNearest(run, centerCell, selection),
            SampleKernel.Bilinear => SampleBilinear(run, centerCell, selection),
            SampleKernel.AreaWeighted => SampleAreaWeighted(run, footprint, selection),
            _ => run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {band}: undefined kernel {selection.Kernel}."),
        };
    }

    private RunContext CreateRun(
        GeospatialRaster<double> raster,
        int band,
        SampleFootprint footprint,
        SamplingOptions options)
    {
        return new RunContext
        {
            Raster = raster,
            Band = band,
            Options = options,
            Target = footprint.TargetCoordinateSystem,
            Transformer = _transformer,
        };
    }

    private static KernelSelection BuildSelection(
        RunContext run,
        SampleFootprint footprint,
        SampleKernel kernel,
        Vector2d centerCell)
    {
        var raster = run.Raster;
        var baseColumn = ClampToIndex(centerCell.X, raster.Width);
        var baseRow = ClampToIndex(centerCell.Y, raster.Height);
        var gsdX = AxisGsdMeters(run, baseColumn, baseRow, columnAxis: true);
        var gsdY = AxisGsdMeters(run, baseColumn, baseRow, columnAxis: false);

        // Decide from the footprint quad's axis extents in source pixel space: the
        // conservative inverse-Jacobian bound that stays correct when the target CRS
        // rotates or warps the sampling direction. The reported spacings are those
        // extents scaled back to meters along each source axis.
        var quad = FootprintQuad(run, footprint);
        var extents = QuadExtents(quad);
        var spacingX = extents.X * gsdX;
        var spacingY = extents.Y * gsdY;
        var effective = kernel;

        if (effective == SampleKernel.Auto)
        {
            // Decimation in either source axis selects area weighting.
            effective = extents.X <= 1d && extents.Y <= 1d
                ? SampleKernel.Bilinear
                : SampleKernel.AreaWeighted;
        }

        return new KernelSelection(effective, gsdX, gsdY, spacingX, spacingY);
    }

    private static (double X, double Y) QuadExtents(IReadOnlyList<Vector2d> quad)
    {
        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;

        for (var i = 0; i < quad.Count; i++)
        {
            minX = Math.Min(minX, quad[i].X);
            maxX = Math.Max(maxX, quad[i].X);
            minY = Math.Min(minY, quad[i].Y);
            maxY = Math.Max(maxY, quad[i].Y);
        }

        return (maxX - minX, maxY - minY);
    }

    private static RasterSampleResult SampleNearest(RunContext run, Vector2d centerCell, KernelSelection selection)
    {
        var queryColumn = Math.Floor(centerCell.X + CellHalf);
        var queryRow = Math.Floor(centerCell.Y + CellHalf);
        CellOutcome outcome;
        double value;

        if (IsInsideRaster(run.Raster, queryColumn, queryRow))
        {
            outcome = ResolveInsideCell(run, (int)queryRow, (int)queryColumn, out value);
        }
        else
        {
            // Outside queries keep the actual fractional position: the edge radius is
            // measured from the true sample location, not the rounded cell.
            outcome = ResolveOutsideCell(run, centerCell, out value);
        }

        if (outcome != CellOutcome.Resolved)
        {
            return run.Failed(selection);
        }

        return Finish(run, selection, value);
    }

    private static RasterSampleResult SampleBilinear(RunContext run, Vector2d centerCell, KernelSelection selection)
    {
        var columnBase = Math.Floor(centerCell.X);
        var rowBase = Math.Floor(centerCell.Y);
        var fractionX = centerCell.X - columnBase;
        var fractionY = centerCell.Y - rowBase;
        double sum = 0d;
        double weightSum = 0d;

        // Row-major: rows ascending, columns ascending within each row.
        for (var rowOffset = 0; rowOffset <= 1; rowOffset++)
        {
            for (var columnOffset = 0; columnOffset <= 1; columnOffset++)
            {
                var weight = (columnOffset == 0 ? 1d - fractionX : fractionX)
                    * (rowOffset == 0 ? 1d - fractionY : fractionY);

                if (weight <= 0d)
                {
                    continue;
                }

                var cellColumn = columnBase + columnOffset;
                var cellRow = rowBase + rowOffset;
                CellOutcome outcome;
                double value;

                if (IsInsideRaster(run.Raster, cellColumn, cellRow))
                {
                    outcome = ResolveInsideCell(run, (int)cellRow, (int)cellColumn, out value);
                }
                else
                {
                    outcome = ResolveOutsideCell(run, new Vector2d(cellColumn, cellRow), out value);
                }

                if (outcome != CellOutcome.Resolved)
                {
                    return run.Failed(selection);
                }

                sum += value * weight;
                weightSum += weight;
            }
        }

        if (weightSum <= 0d || !double.IsFinite(weightSum))
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {run.Band}: bilinear weights collapsed to zero.");
        }

        return Finish(run, selection, sum / weightSum);
    }

    private static RasterSampleResult SampleAreaWeighted(
        RunContext run,
        SampleFootprint footprint,
        KernelSelection selection)
    {
        var quad = FootprintQuad(run, footprint);

        if (quad.Count < 3)
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {run.Band}: the output footprint degenerates in source pixel space.");
        }

        var totalArea = PolygonMath.PolygonArea(quad);

        if (!double.IsFinite(totalArea) || totalArea <= 0d)
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {run.Band}: the output footprint has no positive area in source pixel space.");
        }

        if (!TryGetCellRange(quad, columnAxis: true, out var columns)
            || !TryGetCellRange(quad, columnAxis: false, out var rows))
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.ResourceLimit,
                $"Band {run.Band}: the output footprint maps to source cell bounds beyond addressable pixel indices.");
        }

        if ((long)columns.Max - columns.Min + 1 > run.Options.MaxFootprintCells
            || (long)rows.Max - rows.Min + 1 > run.Options.MaxFootprintCells)
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.ResourceLimit,
                $"Band {run.Band}: the output footprint spans more source cells per axis than the "
                + $"maxFootprintCells work budget ({run.Options.MaxFootprintCells}).");
        }

        double sum = 0d;
        double weightSum = 0d;

        // Row-major: rows ascending, columns ascending within each row. Cells beyond the
        // raster bounds are virtual cells carrying the edge policy.
        for (var row = rows.Min; row <= rows.Max; row++)
        {
            for (var column = columns.Min; column <= columns.Max; column++)
            {
                if (!run.TryVisitCell())
                {
                    return run.Failed(selection);
                }

                var area = PolygonMath.ClippedArea(
                    quad,
                    column - CellHalf,
                    row - CellHalf,
                    column + CellHalf,
                    row + CellHalf);

                if (area <= 0d)
                {
                    continue;
                }

                CellOutcome outcome;
                double value;

                if (IsInsideRaster(run.Raster, column, row))
                {
                    outcome = ResolveInsideCell(run, row, column, out value);
                }
                else
                {
                    outcome = ResolveOutsideCell(run, new Vector2d(column, row), out value);
                }

                if (outcome != CellOutcome.Resolved)
                {
                    return run.Failed(selection);
                }

                sum += value * area;
                weightSum += area;
            }
        }

        if (weightSum <= 0d || !double.IsFinite(weightSum))
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {run.Band}: area weights collapsed to zero.");
        }

        return Finish(run, selection, sum / weightSum);
    }

    /// <summary>Resolves the value of an in-raster cell, applying the no-data policy when needed.</summary>
    private static CellOutcome ResolveInsideCell(RunContext run, int row, int column, out double value)
    {
        var raw = run.Raster.Get(run.Band, row, column);

        if (IsUsableSample(run.Raster, raw))
        {
            value = raw;

            return CellOutcome.Resolved;
        }

        if (run.Options.NoData == NoDataPolicy.Constant)
        {
            value = run.Options.NoDataConstantMeters!.Value;
            run.NoDataFills++;
            run.Diagnostics.Add(new GeoDiagnostic(
                SamplingDiagnosticCodes.NoDataFilled,
                GeoStage.Raster,
                GeoSeverity.Info,
                $"Band {run.Band}: filled no-data at source cell (row {row}, column {column}) with constant "
                + $"{Format(value)} m."));

            return CellOutcome.Resolved;
        }

        if (run.Options.NoData == NoDataPolicy.NearestValid)
        {
            var radius = run.Options.NoDataSearchRadiusMeters!.Value;
            var search = FindNearestCell(run, new Vector2d(column, row), radius);

            if (search.Outcome == NearestOutcome.ResourceLimit)
            {
                value = 0d;

                return CellOutcome.ResourceLimit;
            }

            if (search.Outcome != NearestOutcome.Found)
            {
                value = 0d;
                run.Diagnostics.Add(new GeoDiagnostic(
                    SamplingDiagnosticCodes.NoDataPolicyFailure,
                    GeoStage.Raster,
                    GeoSeverity.Error,
                    $"Band {run.Band}: no-data at source cell (row {row}, column {column}) and nearest-valid "
                    + $"found no valid cell within {Format(radius)} m."));

                return CellOutcome.NoDataFailed;
            }

            value = run.Raster.Get(run.Band, search.Row, search.Column);
            run.NoDataFills++;
            run.Diagnostics.Add(new GeoDiagnostic(
                SamplingDiagnosticCodes.NoDataFilled,
                GeoStage.Raster,
                GeoSeverity.Info,
                $"Band {run.Band}: filled no-data at source cell (row {row}, column {column}) from nearest "
                + $"valid source cell (row {search.Row}, column {search.Column}) at {Format(search.DistanceMeters)} m."));

            return CellOutcome.Resolved;
        }

        value = 0d;
        run.Diagnostics.Add(new GeoDiagnostic(
            SamplingDiagnosticCodes.NoDataPolicyFailure,
            GeoStage.Raster,
            GeoSeverity.Error,
            $"Band {run.Band}: no-data at source cell (row {row}, column {column}) and the no-data policy is 'fail'."));

        return CellOutcome.NoDataFailed;
    }

    /// <summary>
    /// Resolves a value for a position outside source coverage by applying the edge policy.
    /// Edge-nearest fills from the clamped nearest in-raster cell, measuring the radius in
    /// target meters from the actual fractional origin; the edge sample then passes the
    /// no-data policy. The outside fill is counted only once the fill resolves.
    /// </summary>
    private static CellOutcome ResolveOutsideCell(RunContext run, Vector2d originCell, out double value)
    {
        if (run.Options.Edge == EdgePolicy.Constant)
        {
            value = run.Options.EdgeConstantMeters!.Value;
            run.OutsideFills++;
            run.Diagnostics.Add(new GeoDiagnostic(
                SamplingDiagnosticCodes.OutsideCoverageFilled,
                GeoStage.Raster,
                GeoSeverity.Info,
                $"Band {run.Band}: filled outside-coverage position (cell ({Format(originCell.X)}, "
                + $"{Format(originCell.Y)})) with edge constant {Format(value)} m."));

            return CellOutcome.Resolved;
        }

        if (run.Options.Edge == EdgePolicy.Nearest)
        {
            var radius = run.Options.EdgeSearchRadiusMeters!.Value;
            var raster = run.Raster;
            var clampedColumn = (int)Math.Clamp(originCell.X, 0d, raster.Width - 1d);
            var clampedRow = (int)Math.Clamp(originCell.Y, 0d, raster.Height - 1d);
            var originTarget = run.ToTarget(CellCenterWorld(raster, originCell.X, originCell.Y));
            var edgeTarget = run.ToTarget(CellCenterWorld(raster, clampedColumn, clampedRow));
            var distance = DistanceMeters(originTarget, edgeTarget);

            if (distance > radius)
            {
                value = 0d;
                run.Diagnostics.Add(new GeoDiagnostic(
                    SamplingDiagnosticCodes.EdgePolicyFailure,
                    GeoStage.Raster,
                    GeoSeverity.Error,
                    $"Band {run.Band}: outside-coverage position (cell ({Format(originCell.X)}, "
                    + $"{Format(originCell.Y)})) is {Format(distance)} m from the nearest edge source cell "
                    + $"(row {clampedRow}, column {clampedColumn}), beyond the edge search radius "
                    + $"{Format(radius)} m."));

                return CellOutcome.EdgeFailed;
            }

            var outcome = ResolveInsideCell(run, clampedRow, clampedColumn, out value);

            if (outcome != CellOutcome.Resolved)
            {
                return outcome;
            }

            run.OutsideFills++;
            run.Diagnostics.Add(new GeoDiagnostic(
                SamplingDiagnosticCodes.OutsideCoverageFilled,
                GeoStage.Raster,
                GeoSeverity.Info,
                $"Band {run.Band}: filled outside-coverage position (cell ({Format(originCell.X)}, "
                + $"{Format(originCell.Y)})) from nearest edge source cell (row {clampedRow}, column "
                + $"{clampedColumn}) at {Format(distance)} m."));

            return CellOutcome.Resolved;
        }

        value = 0d;
        run.Diagnostics.Add(new GeoDiagnostic(
            SamplingDiagnosticCodes.EdgePolicyFailure,
            GeoStage.Raster,
            GeoSeverity.Error,
            $"Band {run.Band}: position (cell ({Format(originCell.X)}, {Format(originCell.Y)})) is outside "
            + "source coverage and the edge policy is 'fail'."));

        return CellOutcome.EdgeFailed;
    }

    /// <summary>
    /// Bounded nearest-valid-cell search. Distances are between cell centers transformed into
    /// target meters. The row-major scan with a strict distance comparison resolves equidistant
    /// candidates to the lowest row and then the lowest column. The axes are independent for
    /// axis-aligned rasters, so clamping the per-axis window to the raster bounds keeps every
    /// cell within the radius inside the window. Candidate examinations count against the
    /// per-sample work budget.
    /// </summary>
    private static NearestCellResult FindNearestCell(RunContext run, Vector2d originCell, double radiusMeters)
    {
        var raster = run.Raster;
        var baseColumn = ClampToIndex(originCell.X, raster.Width);
        var baseRow = ClampToIndex(originCell.Y, raster.Height);
        var gsdX = AxisGsdMeters(run, baseColumn, baseRow, columnAxis: true);
        var gsdY = AxisGsdMeters(run, baseColumn, baseRow, columnAxis: false);
        var reachX = radiusMeters / gsdX;
        var reachY = radiusMeters / gsdY;

        // Clamp to the raster bounds before the double-to-int conversion so that huge
        // radii search only the raster instead of overflowing.
        var windowX = (int)Math.Min(Math.Ceiling(reachX + Math.Abs(originCell.X - baseColumn) + 1d), raster.Width - 1d);
        var windowY = (int)Math.Min(Math.Ceiling(reachY + Math.Abs(originCell.Y - baseRow) + 1d), raster.Height - 1d);
        var minRow = Math.Max(0, baseRow - windowY);
        var maxRow = Math.Min(raster.Height - 1, baseRow + windowY);
        var minColumn = Math.Max(0, baseColumn - windowX);
        var maxColumn = Math.Min(raster.Width - 1, baseColumn + windowX);
        var originTarget = run.ToTarget(CellCenterWorld(raster, originCell.X, originCell.Y));
        var best = new NearestCellResult(NearestOutcome.NotFound, 0, 0, double.PositiveInfinity);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var column = minColumn; column <= maxColumn; column++)
            {
                if (!run.TryVisitCell())
                {
                    return new NearestCellResult(NearestOutcome.ResourceLimit, 0, 0, 0d);
                }

                if (!IsUsableSample(raster, raster.Get(run.Band, row, column)))
                {
                    continue;
                }

                var candidateTarget = run.ToTarget(CellCenterWorld(raster, column, row));
                var distance = DistanceMeters(originTarget, candidateTarget);

                if (distance <= radiusMeters && distance < best.DistanceMeters)
                {
                    best = new NearestCellResult(NearestOutcome.Found, row, column, distance);
                }
            }
        }

        return best;
    }

    private static IReadOnlyList<Vector2d> FootprintQuad(RunContext run, SampleFootprint footprint)
    {
        var bounds = footprint.Bounds;
        var corners = new[]
        {
            new Vector2d(bounds.MinEasting, bounds.MinNorthing),
            new Vector2d(bounds.MaxEasting, bounds.MinNorthing),
            new Vector2d(bounds.MaxEasting, bounds.MaxNorthing),
            new Vector2d(bounds.MinEasting, bounds.MaxNorthing),
        };

        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = ToCellSpace(run.Raster.Transform.WorldToPixel(run.ToSource(corners[i])));
        }

        return PolygonMath.ConvexHull(corners);
    }

    private static double AxisGsdMeters(RunContext run, int baseColumn, int baseRow, bool columnAxis)
    {
        var raster = run.Raster;

        // Step to an adjacent cell even outside the raster when the axis has a single
        // real cell: the affine defines the lattice spacing there too.
        var neighborColumn = columnAxis
            ? (baseColumn == raster.Width - 1 && raster.Width > 1 ? baseColumn - 1 : baseColumn + 1)
            : baseColumn;
        var neighborRow = !columnAxis
            ? (baseRow == raster.Height - 1 && raster.Height > 1 ? baseRow - 1 : baseRow + 1)
            : baseRow;
        var first = run.ToTarget(CellCenterWorld(raster, baseColumn, baseRow));
        var second = run.ToTarget(CellCenterWorld(raster, neighborColumn, neighborRow));

        return DistanceMeters(first, second);
    }

    private static RasterSampleResult Finish(RunContext run, KernelSelection selection, double value)
    {
        if (!double.IsFinite(value))
        {
            return run.Fail(
                selection,
                SamplingDiagnosticCodes.InvalidSample,
                $"Band {run.Band}: sample accumulation produced a non-finite value.");
        }

        return run.Succeed(selection, value);
    }

    private static RasterSampleResult TransformFailure(int band, Exception detail)
    {
        var diagnostics = new[]
        {
            new GeoDiagnostic(
                SamplingDiagnosticCodes.InvalidSample,
                GeoStage.Raster,
                GeoSeverity.Error,
                $"Band {band}: coordinate transformation failed while sampling: {detail.Message}",
                detail: detail),
        };

        return new RasterSampleResult
        {
            Status = SampleStatus.Failed,
            Value = 0d,
            Selection = null,
            NoDataFills = 0,
            OutsideFills = 0,
            Diagnostics = diagnostics,
        };
    }

    private static bool IsInsideRaster(GeospatialRaster<double> raster, double column, double row)
    {
        return column >= 0d && column < raster.Width && row >= 0d && row < raster.Height;
    }

    private static bool IsUsableSample(GeospatialRaster<double> raster, double value)
    {
        if (!double.IsFinite(value))
        {
            return false;
        }

        if (!raster.HasNoData)
        {
            return true;
        }

        var sentinel = raster.NoData;

        return double.IsNaN(sentinel) ? !double.IsNaN(value) : value != sentinel;
    }

    private static Vector2d CellCenterWorld(GeospatialRaster<double> raster, double column, double row)
    {
        return raster.Transform.PixelToWorld(new Vector2d(column + CellHalf, row + CellHalf));
    }

    private static Vector2d ToCellSpace(Vector2d pixel)
    {
        return new Vector2d(pixel.X - CellHalf, pixel.Y - CellHalf);
    }

    private static double DistanceMeters(Vector2d first, Vector2d second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int ClampToIndex(double value, int limit)
    {
        var rounded = Math.Floor(value + CellHalf);

        return (int)Math.Clamp(rounded, 0d, limit - 1d);
    }

    /// <summary>
    /// Cell indices whose coverage rectangles overlap the quad's axis extent on the full
    /// lattice, including virtual cells beyond the raster bounds. Returns false when the
    /// extent is not representable as an int cell range; the caller fails the sample
    /// instead of silently clamping a different footprint into range.
    /// </summary>
    private static bool TryGetCellRange(IReadOnlyList<Vector2d> quad, bool columnAxis, out IndexRange range)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;

        for (var i = 0; i < quad.Count; i++)
        {
            var coordinate = columnAxis ? quad[i].X : quad[i].Y;
            min = Math.Min(min, coordinate);
            max = Math.Max(max, coordinate);
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            range = default;

            return false;
        }

        // Strict bounds: cells merely touching the quad edge have zero overlap and stay
        // out of the range. The upper bound keeps one slot of headroom below int.MaxValue
        // so the int loop induction can never overflow.
        var lower = Math.Floor(min - CellHalf) + 1d;
        var upper = Math.Ceiling(max + CellHalf) - 1d;

        if (lower < int.MinValue || upper > int.MaxValue - 1d)
        {
            range = default;

            return false;
        }

        if (lower > upper)
        {
            range = new IndexRange(0, -1);

            return true;
        }

        range = new IndexRange((int)lower, (int)upper);

        return true;
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private enum CellOutcome
    {
        Resolved = 0,

        NoDataFailed = 1,

        EdgeFailed = 2,

        ResourceLimit = 3,
    }

    private enum NearestOutcome
    {
        NotFound = 0,

        Found = 1,

        ResourceLimit = 2,
    }

    private readonly record struct NearestCellResult(NearestOutcome Outcome, int Row, int Column, double DistanceMeters);

    private readonly record struct IndexRange(int Min, int Max);

    private sealed class RunContext
    {
        public required GeospatialRaster<double> Raster { get; init; }

        public required int Band { get; init; }

        public required SamplingOptions Options { get; init; }

        public required CoordinateSystemId Target { get; init; }

        public required ICoordinateTransformer Transformer { get; init; }

        public List<GeoDiagnostic> Diagnostics { get; } = [];

        public int NoDataFills { get; set; }

        public int OutsideFills { get; set; }

        public long CellsVisited { get; private set; }

        private bool ResourceLimitReported { get; set; }

        public Vector2d ToTarget(Vector2d world)
        {
            return Transformer.Transform(world, Raster.CoordinateSystem, Target);
        }

        public Vector2d ToSource(Vector2d targetPoint)
        {
            return Transformer.Transform(targetPoint, Target, Raster.CoordinateSystem);
        }

        /// <summary>
        /// Counts one cell visit (a footprint overlap computation or a nearest-cell search
        /// candidate) against the cumulative per-sample work budget.
        /// </summary>
        public bool TryVisitCell()
        {
            if (CellsVisited >= Options.MaxFootprintCells)
            {
                if (!ResourceLimitReported)
                {
                    ResourceLimitReported = true;
                    Diagnostics.Add(new GeoDiagnostic(
                        SamplingDiagnosticCodes.ResourceLimit,
                        GeoStage.Raster,
                        GeoSeverity.Error,
                        $"Band {Band}: sampling exceeded the maxFootprintCells work budget of "
                        + $"{Options.MaxFootprintCells} cell visits."));
                }

                return false;
            }

            CellsVisited++;

            return true;
        }

        public RasterSampleResult Succeed(KernelSelection selection, double value)
        {
            return new RasterSampleResult
            {
                Status = SampleStatus.Sampled,
                Value = value,
                Selection = selection,
                NoDataFills = NoDataFills,
                OutsideFills = OutsideFills,
                Diagnostics = Diagnostics,
            };
        }

        public RasterSampleResult Failed(KernelSelection? selection)
        {
            // The deciding policy already added its error diagnostic.
            return new RasterSampleResult
            {
                Status = SampleStatus.Failed,
                Value = 0d,
                Selection = selection,
                NoDataFills = NoDataFills,
                OutsideFills = OutsideFills,
                Diagnostics = Diagnostics,
            };
        }

        public RasterSampleResult Fail(KernelSelection? selection, string code, string message, Exception? detail = null)
        {
            Diagnostics.Add(new GeoDiagnostic(code, GeoStage.Raster, GeoSeverity.Error, message, detail: detail));

            return Failed(selection);
        }
    }
}
