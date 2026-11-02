using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Generates terrain chunks by inverse-warping each stored lattice sample (core and
/// apron) into the elevation raster once; no intermediate warped raster. Each sample's
/// global integer index is computed before any double position, so the shared edge
/// samples of adjacent chunks are bit-identical.
/// </summary>
public sealed class TerrainChunkGenerator
{
    private readonly RasterSampler _sampler;

    private readonly ICoordinateTransformer _transformer;

    public TerrainChunkGenerator(ICoordinateTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        _transformer = transformer;
        _sampler = new RasterSampler(transformer);
    }

    /// <summary>
    /// Generates one chunk. Rejected samples add typed
    /// <see cref="GeoDiagnosticCodes.GenerateFailure"/> diagnostics to
    /// <paramref name="diagnostics"/> and return null; fill events are counted on
    /// <paramref name="stats"/> only. Cancellation is checked once per lattice row.
    /// </summary>
    public TerrainChunk? Generate(
        LocalMapGrid grid,
        GeospatialRaster<double> elevation,
        SamplingOptions policy,
        int cx,
        int cy,
        SamplingStats? stats = null,
        GeoDiagnosticBag? diagnostics = null,
        SampleKernel kernel = SampleKernel.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfLessThan(cx, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cx, grid.ChunksX);
        ArgumentOutOfRangeException.ThrowIfLessThan(cy, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cy, grid.ChunksY);

        if (!Enum.IsDefined(kernel))
        {
            throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Undefined kernel value.");
        }

        diagnostics ??= new GeoDiagnosticBag();

        var stored = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[stored * stored];
        var mapTransform = grid.Transform;
        var verticalOrigin = mapTransform.VerticalOriginMeters;
        var exaggeration = mapTransform.VerticalExaggeration;
        var metersPerGameUnit = mapTransform.MetersPerGameUnit;
        var halfSpacing = (ChunkLayout.SampleSpacingGameUnits * metersPerGameUnit) / 2d;
        var target = grid.ProjectedCoordinateSystem;
        var source = elevation.CoordinateSystem;
        var baseSampleX = cx * ChunkLayout.IntervalsPerChunk;
        var baseSampleY = cy * ChunkLayout.IntervalsPerChunk;

        for (var sy = 0; sy < stored; sy++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gy = baseSampleY + sy - 1;
            var rowOffset = sy * stored;

            for (var sx = 0; sx < stored; sx++)
            {
                var gx = baseSampleX + sx - 1;
                var projected = grid.SampleToProjected(gx, gy);

                Vector2d center;

                try
                {
                    center = _transformer.Transform(projected, target, source);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): inverse transform of lattice sample (global {gx}, {gy}) from the target CRS to the elevation raster CRS failed.",
                        ex);
                }

                if (!double.IsFinite(center.X) || !double.IsFinite(center.Y))
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): inverse transform of lattice sample (global {gx}, {gy}) produced a non-finite raster coordinate.");
                }

                var footprint = new SampleFootprint(
                    new ProjectedBounds(
                        projected.X - halfSpacing,
                        projected.Y - halfSpacing,
                        projected.X + halfSpacing,
                        projected.Y + halfSpacing),
                    target);

                var result = _sampler.Sample(elevation, 0, center.X, center.Y, footprint, kernel, policy, stats);

                if (!result.IsSampled)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        diagnostics.Add(diagnostic);
                    }

                    return Fail(
                        $"Chunk ({cx}, {cy}): lattice sample (global {gx}, {gy}) failed against the elevation raster.");
                }

                var gameZ = (result.Value - verticalOrigin) * exaggeration / metersPerGameUnit;

                if (!double.IsFinite(gameZ))
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): lattice sample (global {gx}, {gy}) converts to a height that is not finite.");
                }

                if (Math.Abs(gameZ) > float.MaxValue)
                {
                    return Fail(
                        $"Chunk ({cx}, {cy}): lattice sample (global {gx}, {gy}) converts to height "
                        + $"{Format(gameZ)} game units, beyond the float32 height payload range.");
                }

                heights[rowOffset + sx] = (float)gameZ;
            }
        }

        return new TerrainChunk(cx, cy, heights);

        TerrainChunk? Fail(string message, Exception? detail = null)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.GenerateFailure,
                GeoStage.Generate,
                GeoSeverity.Error,
                message,
                detail: detail));

            return null;
        }
    }

    /// <summary>
    /// Lazily streams every chunk southwest row-major without retaining earlier chunks; a
    /// rejected chunk adds its typed error to <paramref name="diagnostics"/> and ends the
    /// enumeration.
    /// </summary>
    public IEnumerable<TerrainChunk> GenerateAll(
        LocalMapGrid grid,
        GeospatialRaster<double> elevation,
        SamplingOptions policy,
        TerrainGenerationReport? report = null,
        GeoDiagnosticBag? diagnostics = null,
        SampleKernel kernel = SampleKernel.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(policy);

        return GenerateAllCore(grid, elevation, policy, report, diagnostics, kernel, cancellationToken);
    }

    private IEnumerable<TerrainChunk> GenerateAllCore(
        LocalMapGrid grid,
        GeospatialRaster<double> elevation,
        SamplingOptions policy,
        TerrainGenerationReport? report,
        GeoDiagnosticBag? diagnostics,
        SampleKernel kernel,
        CancellationToken cancellationToken)
    {
        for (var cy = 0; cy < grid.ChunksY; cy++)
        {
            for (var cx = 0; cx < grid.ChunksX; cx++)
            {
                var chunk = Generate(grid, elevation, policy, cx, cy, report?.Sampling, diagnostics, kernel, cancellationToken);

                if (chunk is null)
                {
                    yield break;
                }

                report?.Record(chunk);
                yield return chunk;
            }
        }
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
