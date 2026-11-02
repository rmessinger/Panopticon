using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Pipeline.Generation;

/// <summary>
/// Chunk generation for one planned import (R2 orchestration contract, WI-B4b-18):
/// streams the four chunk layers into staged files, clips and maps features against the
/// bounded staged-terrain cache, and assembles the payload metadata plus per-layer
/// summaries. No metadata or report write, no reader, no commit; recoverable failures
/// stop with typed GEO-RUN diagnostics, cancellation and programmer errors propagate.
/// </summary>
internal static class ImportChunkGeneration
{
    /// <summary>
    /// Lower bound on one descriptor's encoded metadata bytes: three mandatory SHA-256
    /// values of 64 hex characters each, imagery excluded.
    /// </summary>
    private const long MinimumDescriptorMetadataBytes = 3L * 64L;

    public static ImportGenerationOutput? Run(
        ImportGenerationPlan plan,
        PayloadStagedFiles stagedFiles,
        string stageDirectory,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stagedFiles);
        ArgumentException.ThrowIfNullOrEmpty(stageDirectory);

        if (!Path.IsPathRooted(stageDirectory))
        {
            throw new ArgumentException("The stage directory must be an absolute path.", nameof(stageDirectory));
        }

        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new GeospatialImportLimits();

        var grid = plan.Grid;
        var manifest = plan.Manifest;
        var sources = plan.Sources;
        var chunkCount = (long)grid.ChunksX * grid.ChunksY;

        // Guarded before any chunk collection is allocated: a custom maxChunkCount far
        // above the metadata writer's byte bound must not allocate billions of slots.
        if (chunkCount > int.MaxValue || chunkCount * MinimumDescriptorMetadataBytes > limits.MaxPayloadMetadataBytes)
        {
            ImportChunkGenerationDiagnostics.AddError(
                diagnostics,
                ImportChunkGenerationDiagnostics.ChunkMetadataBudgetExceededCode,
                $"The grid needs {chunkCount} chunk descriptors of at least {MinimumDescriptorMetadataBytes} encoded bytes each, "
                + $"beyond the maxPayloadMetadataBytes bound of {limits.MaxPayloadMetadataBytes} or the int32 descriptor range.");
            return null;
        }

        var imageryRaster = sources.Imagery?.Raster;
        var imageryPixelsPerSample = plan.GridDefinition.ImageryPixelsPerSample;

        if (imageryRaster is not null && imageryPixelsPerSample is null)
        {
            ImportChunkGenerationDiagnostics.AddError(
                diagnostics,
                ImportChunkGenerationDiagnostics.PlanImageryInvalidCode,
                "The plan carries an imagery source but no imagery pixels per sample; the plan is inconsistent.");
            return null;
        }

        var count = (int)chunkCount;
        var terrainReferences = new PayloadFileReference[count];
        var imageryReferences = imageryRaster is null ? null : new PayloadFileReference[count];

        var terrainGenerator = new TerrainChunkGenerator(plan.Transformer);
        var imageryGenerator = imageryRaster is null ? null : new ImageryChunkGenerator(plan.Transformer);
        var terrainReport = new TerrainGenerationReport();
        var imageryStats = new SamplingStats();

        // Terrain and imagery, southwest row-major; only references and counters
        // survive the loop.
        for (var cy = 0; cy < grid.ChunksY; cy++)
        {
            for (var cx = 0; cx < grid.ChunksX; cx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = (cy * grid.ChunksX) + cx;

                var terrainChunk = terrainGenerator.Generate(
                    grid,
                    sources.PrimaryElevation.Raster,
                    plan.SamplingOptions,
                    cx,
                    cy,
                    terrainReport.Sampling,
                    diagnostics,
                    cancellationToken: cancellationToken);

                if (terrainChunk is null)
                {
                    return null;
                }

                terrainReport.Record(terrainChunk);
                var terrainReference = stagedFiles.WriteTerrain(terrainChunk, cancellationToken);

                if (terrainReference is null)
                {
                    return null;
                }

                terrainReferences[index] = terrainReference;

                if (imageryGenerator is null || imageryRaster is null)
                {
                    continue;
                }

                var imageryChunk = imageryGenerator.Generate(
                    grid,
                    imageryRaster,
                    plan.SamplingOptions,
                    cx,
                    cy,
                    imageryPixelsPerSample!.Value,
                    imageryStats,
                    diagnostics,
                    limits,
                    cancellationToken);

                if (imageryChunk is null)
                {
                    return null;
                }

                var imageryReference = stagedFiles.WriteImagery(imageryChunk, cancellationToken);

                if (imageryReference is null)
                {
                    return null;
                }

                imageryReferences![index] = imageryReference;
            }
        }

        // Clip and map features; point Z resolves through the staged terrain cache,
        // never a full payload reader.
        var localFeatures = FeatureClipper.Clip(sources.VectorFeatures, grid, plan.Transformer, diagnostics, limits, cancellationToken);

        if (diagnostics.HasErrors)
        {
            return null;
        }

        var terrainCache = new StagedTerrainChunkCache(stageDirectory, grid.ChunksX, grid.ChunksY, terrainReferences, diagnostics, cancellationToken);
        var heightSampler = new TerrainChunkHeightSampler(grid.ChunksX, grid.ChunksY, terrainCache.Resolve);
        var generatedFeatures = FeatureMapper.Map(localFeatures, manifest.FeatureMappings, heightSampler, diagnostics, limits, cancellationToken);

        if (diagnostics.HasErrors)
        {
            return null;
        }

        // Materials and masks, southwest row-major against the staged terrain.
        var materialGenerator = new MaterialLayerGenerator(plan.Transformer);
        var classificationStats = new SamplingStats();
        var maskReport = new TerrainMaskReport();
        var descriptors = new PayloadChunkDescriptor[count];

        for (var cy = 0; cy < grid.ChunksY; cy++)
        {
            for (var cx = 0; cx < grid.ChunksX; cx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = (cy * grid.ChunksX) + cx;

                var materialChunk = materialGenerator.Generate(
                    grid,
                    sources.Classification?.Raster,
                    localFeatures,
                    manifest.MaterialRules,
                    manifest.FeatureMappings,
                    plan.Palette,
                    cx,
                    cy,
                    plan.SamplingOptions,
                    classificationStats,
                    diagnostics,
                    limits,
                    cancellationToken);

                if (materialChunk is null)
                {
                    return null;
                }

                var materialReference = stagedFiles.WriteMaterial(materialChunk, cancellationToken);

                if (materialReference is null)
                {
                    return null;
                }

                var stagedTerrain = terrainCache.Resolve(cx, cy);

                if (stagedTerrain is null)
                {
                    return null;
                }

                var maskChunk = TerrainMaskGenerator.Generate(
                    grid,
                    stagedTerrain,
                    generatedFeatures,
                    plan.SlopeThresholds,
                    maskReport,
                    diagnostics,
                    limits,
                    cancellationToken);

                if (maskChunk is null)
                {
                    return null;
                }

                var maskReference = stagedFiles.WriteMask(maskChunk, cancellationToken);

                if (maskReference is null)
                {
                    return null;
                }

                descriptors[index] = new PayloadChunkDescriptor(
                    cx,
                    cy,
                    terrainReferences[index],
                    materialReference,
                    maskReference,
                    imageryReferences?[index]);
            }
        }

        return BuildOutput(
            plan,
            terrainReport,
            imageryStats,
            imageryReferences is null,
            maskReport,
            generatedFeatures,
            classificationStats,
            descriptors,
            diagnostics);
    }

    private static ImportGenerationOutput? BuildOutput(
        ImportGenerationPlan plan,
        TerrainGenerationReport terrainReport,
        SamplingStats imageryStats,
        bool withoutImagery,
        TerrainMaskReport maskReport,
        IReadOnlyList<GeneratedFeature> generatedFeatures,
        SamplingStats classificationStats,
        PayloadChunkDescriptor[] descriptors,
        GeoDiagnosticBag diagnostics)
    {
        PayloadTerrainGenerationSummary terrainSummary;
        PayloadImageryGenerationSummary? imagerySummary = null;
        PayloadSamplingSummary classificationSummary;
        PayloadMaskGenerationSummary maskSummary;
        PayloadFeatureGenerationSummary featureSummary;

        try
        {
            terrainSummary = new PayloadTerrainGenerationSummary(
                ToSamplingSummary(terrainReport.Sampling),
                terrainReport.ChunkCount,
                terrainReport.MinHeightGameUnits,
                terrainReport.MaxHeightGameUnits);
            imagerySummary = withoutImagery ? null : new PayloadImageryGenerationSummary(ToSamplingSummary(imageryStats), terrainReport.ChunkCount);
            classificationSummary = ToSamplingSummary(classificationStats);
            maskSummary = new PayloadMaskGenerationSummary(
                maskReport.ImpassableThresholdDegrees,
                maskReport.UnbuildableThresholdDegrees,
                maskReport.VerticalExaggeration,
                maskReport.ChunkCount,
                maskReport.ImpassableCellCount,
                maskReport.UnbuildableCellCount,
                maskReport.SlopeImpassableCellCount,
                maskReport.SlopeUnbuildableCellCount,
                maskReport.WaterRuleApplicationCount,
                maskReport.PassabilityRuleApplicationCount,
                maskReport.BuildabilityRuleApplicationCount,
                maskReport.PlayableForcedCellCount);
            featureSummary = BuildFeatureSummary(generatedFeatures);
        }
        catch (ArgumentException ex)
        {
            ImportChunkGenerationDiagnostics.AddError(
                diagnostics,
                ImportChunkGenerationDiagnostics.SummaryInvalidCode,
                $"A generation summary cannot be built from the recorded counters: {ex.Message}",
                detail: ex);
            return null;
        }

        GeneratedMapPayloadMetadata metadata;

        try
        {
            metadata = new GeneratedMapPayloadMetadata(
                plan.GridDefinition,
                plan.Palette,
                generatedFeatures,
                plan.ManifestSha256,
                plan.Provenance,
                descriptors);
        }
        catch (ArgumentException ex)
        {
            ImportChunkGenerationDiagnostics.AddError(
                diagnostics,
                ImportChunkGenerationDiagnostics.MetadataInvalidCode,
                $"The payload metadata cannot be assembled from the generated content: {ex.Message}",
                detail: ex);
            return null;
        }

        return new ImportGenerationOutput(metadata, terrainSummary, imagerySummary, maskSummary, featureSummary, classificationSummary);
    }

    /// <summary>Converts one runtime accumulator without merging it with any other layer's stats.</summary>
    private static PayloadSamplingSummary ToSamplingSummary(SamplingStats stats)
    {
        var kernels = Enum.GetValues<SampleKernel>();
        var kernelCounts = new PayloadKernelSampleCount[kernels.Length];

        for (var i = 0; i < kernels.Length; i++)
        {
            kernelCounts[i] = new PayloadKernelSampleCount(kernels[i], stats.GetKernelSampleCount(kernels[i]));
        }

        return new PayloadSamplingSummary(
            stats.SampleCount,
            stats.FailedSampleCount,
            stats.NoDataFillCount,
            stats.OutsideFillCount,
            kernelCounts,
            new PayloadObservedRanges(
                TryRange(stats.TryGetObservedSourceGsdXMeters),
                TryRange(stats.TryGetObservedSourceGsdYMeters),
                TryRange(stats.TryGetObservedOutputSpacingXMeters),
                TryRange(stats.TryGetObservedOutputSpacingYMeters),
                TryRange(stats.TryGetObservedGsdRatioX),
                TryRange(stats.TryGetObservedGsdRatioY)));

        static PayloadObservedRange? TryRange(TryGetRange tryGet)
        {
            return tryGet(out var min, out var max) ? new PayloadObservedRange(min, max) : null;
        }
    }

    private delegate bool TryGetRange(out double min, out double max);

    private static PayloadFeatureGenerationSummary BuildFeatureSummary(IReadOnlyList<GeneratedFeature> features)
    {
        var kinds = Enum.GetValues<GeneratedFeatureKind>();
        var counts = new long[kinds.Length];

        foreach (var feature in features)
        {
            counts[Array.IndexOf(kinds, feature.Kind)]++;
        }

        var byKind = new PayloadFeatureKindCount[kinds.Length];

        for (var i = 0; i < kinds.Length; i++)
        {
            byKind[i] = new PayloadFeatureKindCount(kinds[i], counts[i]);
        }

        return new PayloadFeatureGenerationSummary(features.Count, byKind);
    }
}
