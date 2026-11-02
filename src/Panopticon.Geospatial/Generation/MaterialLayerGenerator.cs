using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Generates material-index chunks from categorical classification and explicit polygon
/// rules, in the precedence order defined by B4b deep dive section 5.2.
/// </summary>
public sealed class MaterialLayerGenerator
{
    private readonly RasterSampler _sampler;

    private readonly ICoordinateTransformer _transformer;

    public MaterialLayerGenerator(ICoordinateTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        _transformer = transformer;
        _sampler = new RasterSampler(transformer);
    }

    /// <summary>
    /// Generates one chunk using nearest-neighbour categorical samples and bounded
    /// polygon painting. Rejections add diagnostics and return null; cancellation throws.
    /// </summary>
    public MaterialChunk? Generate(
        LocalMapGrid grid,
        GeospatialRaster<double>? classification,
        IReadOnlyList<LocalFeature>? clippedFeatures,
        IReadOnlyList<MaterialRule>? classificationRules,
        IReadOnlyList<FeatureMappingRule>? polygonRules,
        MaterialPalette palette,
        int cx,
        int cy,
        SamplingOptions? samplingOptions = null,
        SamplingStats? stats = null,
        GeoDiagnosticBag? diagnostics = null,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentOutOfRangeException.ThrowIfLessThan(cx, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cx, grid.ChunksX);
        ArgumentOutOfRangeException.ThrowIfLessThan(cy, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cy, grid.ChunksY);

        diagnostics ??= new GeoDiagnosticBag();
        limits ??= new GeospatialImportLimits();
        cancellationToken.ThrowIfCancellationRequested();

        SplitRules(classificationRules, out var valueRules, out var attributeRules);

        if (valueRules.Count > 0 && classification is null)
        {
            return Fail(
                MaterialDiagnosticCodes.MaterialClassificationMissing,
                $"Chunk ({cx}, {cy}): classification rules need a classification raster; none was supplied.");
        }

        if (classification is not null && !IsCategoricalFormat(classification.SampleFormat))
        {
            return Fail(
                MaterialDiagnosticCodes.MaterialSourceFormatInvalid,
                $"Chunk ({cx}, {cy}): the classification source must be a categorical integer raster "
                + $"(uint8, int16, uint16, or int32); found {classification.SampleFormat}.");
        }

        if (classification is not null && classification.BandCount != 1)
        {
            return Fail(
                MaterialDiagnosticCodes.MaterialSourceBandCountInvalid,
                $"Chunk ({cx}, {cy}): the classification source must carry exactly one band; found {classification.BandCount}.");
        }

        // Every rule form is validated against the explicit palette, features or not.
        var lookup = BuildClassificationLookup(valueRules, palette, cx, cy, diagnostics);
        if (lookup is null)
        {
            return null;
        }

        if (!TryResolveAttributeRules(attributeRules, palette, cx, cy, diagnostics, out var resolvedAttribute)
            || !TryResolveMappingRules(polygonRules, palette, cx, cy, diagnostics, out var resolvedMappings))
        {
            return null;
        }

        int baseSampleX;
        int baseSampleY;
        int lastSampleX;
        int lastSampleY;
        try
        {
            baseSampleX = checked(cx * ChunkLayout.IntervalsPerChunk);
            baseSampleY = checked(cy * ChunkLayout.IntervalsPerChunk);
            lastSampleX = checked(baseSampleX + ChunkLayout.IntervalsPerChunk);
            lastSampleY = checked(baseSampleY + ChunkLayout.IntervalsPerChunk);
        }
        catch (OverflowException ex)
        {
            return Fail(
                GeoDiagnosticCodes.GenerateFailure,
                $"Chunk ({cx}, {cy}): the chunk's global sample indices overflow int32; the grid is too large.",
                ex);
        }

        var points = ChunkLayout.PointsPerChunkAxis;
        var outputBytes = (long)points * points;
        if (outputBytes > limits.MaxRasterDecodedBytes)
        {
            return Fail(
                MaterialDiagnosticCodes.MaterialOutputLimitExceeded,
                $"Chunk ({cx}, {cy}): the {outputBytes}-byte index buffer exceeds the maxRasterDecodedBytes "
                + $"budget of {limits.MaxRasterDecodedBytes} bytes.");
        }

        var indices = new byte[outputBytes];

        if (lookup.Count > 0)
        {
            if (!TrySampleClassification(
                    grid,
                    classification!,
                    lookup,
                    cx,
                    cy,
                    baseSampleX,
                    baseSampleY,
                    samplingOptions,
                    stats,
                    indices,
                    diagnostics,
                    cancellationToken))
            {
                return null;
            }
        }

        if (!TryPaintPolygons(
                clippedFeatures,
                resolvedAttribute,
                resolvedMappings,
                cx,
                cy,
                indices,
                baseSampleX,
                baseSampleY,
                lastSampleX,
                lastSampleY,
                limits,
                diagnostics,
                cancellationToken))
        {
            return null;
        }

        return new MaterialChunk(cx, cy, palette, indices);

        MaterialChunk? Fail(string code, string message, Exception? detail = null)
        {
            diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message, detail: detail));

            return null;
        }
    }

    private static bool IsCategoricalFormat(RasterSampleFormat format) => format switch
    {
        RasterSampleFormat.UInt8 or RasterSampleFormat.Int16 or RasterSampleFormat.UInt16 or RasterSampleFormat.Int32 => true,
        _ => false,
    };

    private static void SplitRules(
        IReadOnlyList<MaterialRule>? rules,
        out List<MaterialRule> valueRules,
        out List<MaterialRule> attributeRules)
    {
        valueRules = [];
        attributeRules = [];

        if (rules is null)
        {
            return;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            ArgumentNullException.ThrowIfNull(rule);

            if (rule.ClassificationValue is not null)
            {
                valueRules.Add(rule);
            }
            else if (rule.Attribute is not null)
            {
                attributeRules.Add(rule);
            }
        }
    }

    private static Dictionary<int, byte>? BuildClassificationLookup(
        IReadOnlyList<MaterialRule> valueRules,
        MaterialPalette palette,
        int cx,
        int cy,
        GeoDiagnosticBag diagnostics)
    {
        var lookup = new Dictionary<int, byte>();

        for (var i = 0; i < valueRules.Count; i++)
        {
            var rule = valueRules[i];
            if (!palette.TryGetIndex(rule.Material, out var index))
            {
                AddNameError(
                    diagnostics,
                    cx,
                    cy,
                    $"materialRules entry for classification value {rule.ClassificationValue!.Value.ToString(CultureInfo.InvariantCulture)}",
                    rule.Material);

                return null;
            }

            // A duplicated classification value keeps the later rule.
            lookup[rule.ClassificationValue!.Value] = index;
        }

        return lookup;
    }

    private static bool TryResolveAttributeRules(
        IReadOnlyList<MaterialRule> rules,
        MaterialPalette palette,
        int cx,
        int cy,
        GeoDiagnosticBag diagnostics,
        out List<(MaterialRule Rule, byte Index)> resolved)
    {
        resolved = [];

        for (var i = 0; i < rules.Count; i++)
        {
            if (!palette.TryGetIndex(rules[i].Material, out var index))
            {
                AddNameError(diagnostics, cx, cy, $"materialRules[{i.ToString(CultureInfo.InvariantCulture)}] attribute entry", rules[i].Material);

                return false;
            }

            resolved.Add((rules[i], index));
        }

        return true;
    }

    private static bool TryResolveMappingRules(
        IReadOnlyList<FeatureMappingRule>? rules,
        MaterialPalette palette,
        int cx,
        int cy,
        GeoDiagnosticBag diagnostics,
        out List<(FeatureMappingRule Rule, byte Index)> resolved)
    {
        resolved = [];

        if (rules is null)
        {
            return true;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            ArgumentNullException.ThrowIfNull(rule);

            if (rule.Target != ManifestFeatureTarget.Material)
            {
                continue;
            }

            if (!palette.TryGetIndex(rule.Material, out var index))
            {
                AddNameError(
                    diagnostics,
                    cx,
                    cy,
                    $"featureMappings[{i.ToString(CultureInfo.InvariantCulture)}] entry for source '{rule.SourceId}'",
                    rule.Material);

                return false;
            }

            resolved.Add((rule, index));
        }

        return true;
    }

    private bool TrySampleClassification(
        LocalMapGrid grid,
        GeospatialRaster<double> classification,
        Dictionary<int, byte> lookup,
        int cx,
        int cy,
        int baseSampleX,
        int baseSampleY,
        SamplingOptions? samplingOptions,
        SamplingStats? stats,
        byte[] indices,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var options = samplingOptions ?? new SamplingOptions();
        var points = ChunkLayout.PointsPerChunkAxis;
        var halfSpacingMeters = (ChunkLayout.SampleSpacingGameUnits * grid.Transform.MetersPerGameUnit) / 2d;
        var target = grid.ProjectedCoordinateSystem;
        var source = classification.CoordinateSystem;

        for (var sy = 0; sy < points; sy++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var gy = baseSampleY + sy;
            var rowOffset = sy * points;

            for (var sx = 0; sx < points; sx++)
            {
                // The global index is computed before any double position, so the shared
                // edge cells of adjacent chunks sample the source bit-identically.
                var gx = baseSampleX + sx;
                var projected = grid.SampleToProjected(gx, gy);

                Vector2d center;

                try
                {
                    center = _transformer.Transform(projected, target, source);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    return FailSampled(
                        $"Chunk ({cx}, {cy}): inverse transform of lattice sample (global {gx}, {gy}) from the "
                        + "target CRS to the classification raster CRS failed.",
                        ex);
                }

                if (!double.IsFinite(center.X) || !double.IsFinite(center.Y))
                {
                    return FailSampled(
                        $"Chunk ({cx}, {cy}): inverse transform of lattice sample (global {gx}, {gy}) produced "
                        + "a non-finite raster coordinate.");
                }

                var footprint = new SampleFootprint(
                    new ProjectedBounds(
                        projected.X - halfSpacingMeters,
                        projected.Y - halfSpacingMeters,
                        projected.X + halfSpacingMeters,
                        projected.Y + halfSpacingMeters),
                    target);

                var result = _sampler.Sample(
                    classification,
                    0,
                    center.X,
                    center.Y,
                    footprint,
                    SampleKernel.Nearest,
                    options,
                    stats);

                if (!result.IsSampled)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        diagnostics.Add(diagnostic);
                    }

                    return FailSampled(
                        $"Chunk ({cx}, {cy}): lattice sample (global {gx}, {gy}) failed against the classification raster.");
                }

                var value = result.Value;
                if (!IsIntegralClassValue(value))
                {
                    diagnostics.Add(new GeoDiagnostic(
                        MaterialDiagnosticCodes.MaterialValueNotIntegral,
                        GeoStage.Generate,
                        GeoSeverity.Error,
                        $"Chunk ({cx}, {cy}): lattice sample (global {gx}, {gy}) produced classification value "
                        + $"{Format(value)}, which is not a finite integral class value."));

                    return false;
                }

                if (lookup.TryGetValue((int)value, out var index))
                {
                    indices[rowOffset + sx] = index;
                }
            }
        }

        return true;

        bool FailSampled(string message, Exception? detail = null)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.GenerateFailure,
                GeoStage.Generate,
                GeoSeverity.Error,
                message,
                detail: detail));

            return false;
        }
    }

    private static bool IsIntegralClassValue(double value)
    {
        return double.IsFinite(value)
            && value == Math.Floor(value)
            && value >= int.MinValue
            && value <= int.MaxValue;
    }

    private static bool TryPaintPolygons(
        IReadOnlyList<LocalFeature>? clippedFeatures,
        List<(MaterialRule Rule, byte Index)> attributeRules,
        List<(FeatureMappingRule Rule, byte Index)> mappingRules,
        int cx,
        int cy,
        byte[] indices,
        int baseSampleX,
        int baseSampleY,
        int lastSampleX,
        int lastSampleY,
        GeospatialImportLimits limits,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        if (attributeRules.Count == 0 && mappingRules.Count == 0)
        {
            return true;
        }

        if (clippedFeatures is not { Count: > 0 })
        {
            return true;
        }

        if (clippedFeatures.Count > limits.MaxFeatureCount)
        {
            diagnostics.Add(new GeoDiagnostic(
                MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded,
                GeoStage.Generate,
                GeoSeverity.Error,
                $"Chunk ({cx}, {cy}): {clippedFeatures.Count} clipped features exceed the maxFeatureCount limit "
                + $"{limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}."));

            return false;
        }

        var context = new PaintContext(cx, cy, diagnostics, limits, cancellationToken);
        if (!TryCountPositions(clippedFeatures, context))
        {
            return false;
        }

        // Frozen paint order: attribute-form material rules, then material-target feature
        // mappings, each in declaration order; every later paint wins.
        for (var r = 0; r < attributeRules.Count && !context.Stopped; r++)
        {
            var (rule, index) = attributeRules[r];

            for (var f = 0; f < clippedFeatures.Count && !context.Stopped; f++)
            {
                var feature = clippedFeatures[f];
                if (!context.TryCharge(1) || !MatchesAttributeRule(rule, feature))
                {
                    continue;
                }

                context.CurrentPartId = feature.PartId;
                TryPaintFeature((GeoPolygon)feature.Geometry, index, indices, baseSampleX, baseSampleY, lastSampleX, lastSampleY, context);
            }
        }

        for (var r = 0; r < mappingRules.Count && !context.Stopped; r++)
        {
            var (rule, index) = mappingRules[r];

            for (var f = 0; f < clippedFeatures.Count && !context.Stopped; f++)
            {
                var feature = clippedFeatures[f];
                if (!context.TryCharge(1) || !MatchesMappingRule(rule, feature))
                {
                    continue;
                }

                context.CurrentPartId = feature.PartId;
                TryPaintFeature((GeoPolygon)feature.Geometry, index, indices, baseSampleX, baseSampleY, lastSampleX, lastSampleY, context);
            }
        }

        return !context.Stopped;
    }

    private static bool TryCountPositions(IReadOnlyList<LocalFeature> features, PaintContext context)
    {
        long total = 0;

        for (var i = 0; i < features.Count && !context.Stopped; i++)
        {
            var feature = features[i];
            ArgumentNullException.ThrowIfNull(feature);
            context.CurrentPartId = feature.PartId;

            var count = CountPositions(feature.Geometry, context);
            if (count < 0)
            {
                return false;
            }

            total += count;
            if (total > context.Limits.MaxCoordinateCount)
            {
                context.FailLimit(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): clipped feature coordinates exceed the "
                    + $"maxCoordinateCount limit {context.Limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");

                return false;
            }
        }

        return true;
    }

    /// <summary>Returns -1 when the traversal stopped on an error.</summary>
    private static long CountPositions(GeoGeometry geometry, PaintContext context)
    {
        if (geometry is GeoPointGeometry)
        {
            return context.TryCharge(1) ? 1 : -1;
        }

        if (geometry is GeoLineString line)
        {
            long count = 0;

            for (var i = 0; i < line.Positions.Count; i++)
            {
                if (!context.TryCharge(1))
                {
                    return -1;
                }

                count++;
            }

            return count;
        }

        if (geometry is GeoPolygon polygon)
        {
            long count = 0;
            if (!TryChargeRing(polygon.OuterRing, context, ref count))
            {
                return -1;
            }

            for (var i = 0; i < polygon.InnerRings.Count; i++)
            {
                if (!TryChargeRing(polygon.InnerRings[i], context, ref count))
                {
                    return -1;
                }
            }

            return count;
        }

        // Multi geometry is not traversed, not even for counting: the clipper emits
        // flattened parts, a nested multi could recurse without bound, and no material
        // rule can consume it anyway.
        context.FailFeature(
            $"Chunk ({context.ChunkX}, {context.ChunkY}): feature part '{context.CurrentPartId}' has a "
            + $"{geometry.GetType().Name} geometry, which the material generator does not support.");

        return -1;

        static bool TryChargeRing(GeoRing ring, PaintContext context, ref long count)
        {
            for (var i = 0; i < ring.Positions.Count; i++)
            {
                if (!context.TryCharge(1))
                {
                    return false;
                }

                count++;
            }

            return true;
        }
    }

    /// <summary>
    /// An attribute-form rule matches any source when its source id is null, otherwise the
    /// exact Ordinal id; only polygon features, then attribute presence or exact value.
    /// </summary>
    private static bool MatchesAttributeRule(MaterialRule rule, LocalFeature feature)
    {
        if (rule.SourceId is { } sourceId
            && !string.Equals(sourceId, feature.SourceFeature.SourceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (feature.Geometry is not GeoPolygon)
        {
            return false;
        }

        if (!feature.SourceFeature.Attributes.TryGetValue(rule.Attribute!, out var value))
        {
            return false;
        }

        return rule.Value is not { } expected || string.Equals(value, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mapping rule matches on exact source id, optional exact layer name (the source
    /// feature's "folder" attribute), optional geometry, and attribute presence or exact
    /// value; the material target only ever consumes polygon features.
    /// </summary>
    private static bool MatchesMappingRule(FeatureMappingRule rule, LocalFeature feature)
    {
        var source = feature.SourceFeature;
        if (!string.Equals(rule.SourceId, source.SourceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!FeatureMappingRule.IsTargetCompatible(rule.Target, ManifestFeatureGeometry.Polygon))
        {
            return false;
        }

        if (rule.Geometry is { } predicate && predicate != ManifestFeatureGeometry.Polygon)
        {
            return false;
        }

        if (feature.Geometry is not GeoPolygon)
        {
            return false;
        }

        if (rule.LayerName is { } layerName
            && (!source.Attributes.TryGetValue("folder", out var folder)
                || !string.Equals(folder, layerName, StringComparison.Ordinal)))
        {
            return false;
        }

        if (rule.Attribute is { } attribute)
        {
            if (!source.Attributes.TryGetValue(attribute, out var value))
            {
                return false;
            }

            if (rule.Value is { } expected && !string.Equals(value, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void TryPaintFeature(
        GeoPolygon polygon,
        byte index,
        byte[] indices,
        int baseSampleX,
        int baseSampleY,
        int lastSampleX,
        int lastSampleY,
        PaintContext context)
    {
        if (context.Stopped || !TryPrepareRings(polygon, context, out var rings, out var originX, out var originY, out var minX, out var minY, out var maxX, out var maxY))
        {
            return;
        }

        var spacing = (double)ChunkLayout.SampleSpacingGameUnits;

        // Polygon bbox in game units; non-finite derived sums are rejected, not painted.
        var worldMinX = originX + minX;
        var worldMinY = originY + minY;
        var worldMaxX = originX + maxX;
        var worldMaxY = originY + maxY;
        if (!IsFiniteQuad(worldMinX, worldMinY, worldMaxX, worldMaxY))
        {
            context.FailFeature(
                $"Chunk ({context.ChunkX}, {context.ChunkY}): the bbox of feature part "
                + $"'{context.CurrentPartId}' is not finite.");

            return;
        }

        // Clip the bbox to the chunk's world range before any index conversion, then
        // derive lattice indices: the casts then stay inside [base, last].
        var chunkMinX = baseSampleX * spacing;
        var chunkMinY = baseSampleY * spacing;
        var chunkMaxX = lastSampleX * spacing;
        var chunkMaxY = lastSampleY * spacing;
        var clippedMinX = Math.Max(worldMinX, chunkMinX);
        var clippedMinY = Math.Max(worldMinY, chunkMinY);
        var clippedMaxX = Math.Min(worldMaxX, chunkMaxX);
        var clippedMaxY = Math.Min(worldMaxY, chunkMaxY);

        var minGx = Math.Ceiling(clippedMinX / spacing);
        var maxGx = Math.Floor(clippedMaxX / spacing);
        var minGy = Math.Ceiling(clippedMinY / spacing);
        var maxGy = Math.Floor(clippedMaxY / spacing);
        if (minGx > maxGx || minGy > maxGy || !IsFiniteQuad(minGx, minGy, maxGx, maxGy))
        {
            return;
        }

        var gxMin = (int)Math.Clamp(minGx, baseSampleX, lastSampleX);
        var gxMax = (int)Math.Clamp(maxGx, baseSampleX, lastSampleX);
        var gyMin = (int)Math.Clamp(minGy, baseSampleY, lastSampleY);
        var gyMax = (int)Math.Clamp(maxGy, baseSampleY, lastSampleY);

        var points = ChunkLayout.PointsPerChunkAxis;

        for (var gy = gyMin; gy <= gyMax && !context.Stopped; gy++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var ty = (gy * spacing) - originY;
            var rowOffset = (gy - baseSampleY) * points;

            for (var gx = gxMin; gx <= gxMax && !context.Stopped; gx++)
            {
                // Direct evaluation per global index keeps the shared edge points of
                // adjacent chunks bit-identical; incremental accumulation would not.
                var tx = (gx * spacing) - originX;

                if (RegionContains(rings, tx, ty, context))
                {
                    indices[rowOffset + (gx - baseSampleX)] = index;
                }
            }
        }
    }

    /// <summary>
    /// Translates every ring by the outer ring's first vertex (standard translated math,
    /// no boolean overlay engine) and drops consecutive and closing duplicates. Positions
    /// must be finite; degenerate rings (fewer than three distinct positions) bound no
    /// area and are dropped.
    /// </summary>
    private static bool TryPrepareRings(
        GeoPolygon polygon,
        PaintContext context,
        out List<double[]> rings,
        out double originX,
        out double originY,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        rings = [];
        var outerPositions = polygon.OuterRing.Positions;
        originX = outerPositions[0].Coordinate.X;
        originY = outerPositions[0].Coordinate.Y;

        var outer = TryPrepareRing(polygon.OuterRing, originX, originY, context, "outer");
        if (outer is null)
        {
            minX = minY = maxX = maxY = 0d;

            // Degenerate or failed preparation: never report success without an outer ring.
            return false;
        }

        rings.Add(outer);

        for (var i = 0; i < polygon.InnerRings.Count && !context.Stopped; i++)
        {
            if (TryPrepareRing(polygon.InnerRings[i], originX, originY, context, $"hole {i}") is { } hole)
            {
                rings.Add(hole);
            }
        }

        minX = double.PositiveInfinity;
        minY = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        maxY = double.NegativeInfinity;
        for (var i = 0; i < outer.Length; i += 2)
        {
            minX = Math.Min(minX, outer[i]);
            maxX = Math.Max(maxX, outer[i]);
            minY = Math.Min(minY, outer[i + 1]);
            maxY = Math.Max(maxY, outer[i + 1]);
        }

        return !context.Stopped;
    }

    private static double[]? TryPrepareRing(GeoRing ring, double originX, double originY, PaintContext context, string role)
    {
        List<double> opened = [];
        var positions = ring.Positions;

        for (var i = 0; i < positions.Count; i++)
        {
            if (!context.TryCharge(1))
            {
                return null;
            }

            var x = positions[i].Coordinate.X - originX;
            var y = positions[i].Coordinate.Y - originY;
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                context.FailFeature(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): the {role} ring of feature part "
                    + $"'{context.CurrentPartId}' has a non-finite coordinate or non-finite translated position.");

                return null;
            }

            if (opened.Count >= 2 && opened[^2] == x && opened[^1] == y)
            {
                continue;
            }

            opened.Add(x);
            opened.Add(y);
        }

        if (opened.Count >= 6 && opened[0] == opened[^2] && opened[1] == opened[^1])
        {
            opened.RemoveRange(opened.Count - 2, 2);
        }

        return opened.Count < 6 ? null : [.. opened];
    }

    private static bool RegionContains(List<double[]> rings, double px, double py, PaintContext context)
    {
        var state = RingState(rings[0], px, py, context);
        if (state == RingPosition.Outside)
        {
            return false;
        }

        // The outer boundary counts as inside, but holes are still tested so a hole
        // boundary touching the outer boundary stays excluded.
        for (var i = 1; i < rings.Count && !context.Stopped; i++)
        {
            if (RingState(rings[i], px, py, context) != RingPosition.Outside)
            {
                // Hole interiors and hole boundaries are excluded, unambiguously.
                return false;
            }
        }

        return true;
    }

    private static RingPosition RingState(double[] ring, double px, double py, PaintContext context)
    {
        var vertices = ring.Length / 2;

        // Boundary first: an exact on-edge point is decided here and never reaches the
        // ray cast, whose behavior on edges is undefined.
        for (var i = 0; i < vertices; i++)
        {
            if (!context.TryCharge(1))
            {
                return RingPosition.Outside;
            }

            var j = (i + 1) % vertices;
            if (OnSegment(ring[(i * 2)], ring[(i * 2) + 1], ring[(j * 2)], ring[(j * 2) + 1], px, py))
            {
                return RingPosition.OnBoundary;
            }
        }

        var inside = false;
        for (var i = 0; i < vertices; i++)
        {
            if (!context.TryCharge(1))
            {
                return RingPosition.Outside;
            }

            var j = (i + 1) % vertices;
            var ax = ring[(i * 2)];
            var ay = ring[(i * 2) + 1];
            var bx = ring[(j * 2)];
            var by = ring[(j * 2) + 1];

            if ((ay > py) != (by > py))
            {
                var crossingX = ax + ((py - ay) * (bx - ax) / (by - ay));
                if (px < crossingX)
                {
                    inside = !inside;
                }
            }
        }

        return inside ? RingPosition.Inside : RingPosition.Outside;
    }

    private static bool OnSegment(double ax, double ay, double bx, double by, double px, double py)
    {
        var cross = ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
        if (cross != 0d)
        {
            return false;
        }

        return px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx)
            && py >= Math.Min(ay, by) && py <= Math.Max(ay, by);
    }

    private static bool IsFiniteQuad(double minX, double minY, double maxX, double maxY)
    {
        return double.IsFinite(minX) && double.IsFinite(minY) && double.IsFinite(maxX) && double.IsFinite(maxY);
    }

    private static void AddNameError(GeoDiagnosticBag diagnostics, int cx, int cy, string ruleDescription, string? material)
    {
        diagnostics.Add(new GeoDiagnostic(
            MaterialDiagnosticCodes.MaterialNameUnknown,
            GeoStage.Generate,
            GeoSeverity.Error,
            $"Chunk ({cx}, {cy}): the {ruleDescription} references material '{material ?? string.Empty}', "
            + "which is not an entry of the palette."));
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private enum RingPosition
    {
        Outside,

        Inside,

        OnBoundary,
    }

    /// <summary>
    /// One chunk's polygon paint budget: every rule-feature check, ring position, and
    /// point edge test charges one unit against MaxVectorWorkPerChunk; the first
    /// exhaustion or invalid feature stops all painting with one typed error.
    /// </summary>
    private sealed class PaintContext(
        int chunkX,
        int chunkY,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken)
    {
        private long _budgetRemaining = limits.MaxVectorWorkPerChunk;

        private long _unitsSinceCancellationCheck;

        public int ChunkX { get; } = chunkX;

        public int ChunkY { get; } = chunkY;

        public GeospatialImportLimits Limits { get; } = limits;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public string CurrentPartId { get; set; } = string.Empty;

        public bool Stopped { get; private set; }

        public bool TryCharge(long units)
        {
            if (Stopped)
            {
                return false;
            }

            _budgetRemaining -= units;
            if (_budgetRemaining < 0)
            {
                FailLimit(
                    $"Chunk ({ChunkX}, {ChunkY}): material polygon work exceeded the maxVectorWorkPerChunk budget "
                    + $"of {Limits.MaxVectorWorkPerChunk.ToString(CultureInfo.InvariantCulture)} units.");

                return false;
            }

            // Ring and edge loops are long; stay responsive to cancellation inside them.
            _unitsSinceCancellationCheck += units;
            if (_unitsSinceCancellationCheck >= 256)
            {
                _unitsSinceCancellationCheck = 0;
                CancellationToken.ThrowIfCancellationRequested();
            }

            return true;
        }

        public void FailFeature(string message)
        {
            Stop(MaterialDiagnosticCodes.MaterialFeatureInvalid, message);
        }

        public void FailLimit(string message)
        {
            Stop(MaterialDiagnosticCodes.MaterialVectorWorkLimitExceeded, message);
        }

        private void Stop(string code, string message)
        {
            if (Stopped)
            {
                return;
            }

            Stopped = true;
            diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message));
        }
    }
}
