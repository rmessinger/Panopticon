#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Serialization;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Sampling;
using Xunit;
using static Panopticon.Geospatial.Tests.Payload.PayloadTestSupport;

namespace Panopticon.Geospatial.Tests.Payload;

public class PayloadSerializationTests
{
    [Fact]
    public void MetadataRoundTrip_PreservesGridTransformLayoutAndPlayableArea()
    {
        var metadata = ReadMetadata(WriteMetadataBytes(Metadata()));

        Assert.NotNull(metadata);
        Assert.Equal("panopticon-geospatial-payload", metadata!.Kind);
        Assert.Equal(1, metadata.SchemaVersion);

        var grid = metadata.Grid;
        Assert.Equal(ChunksX, grid.ChunksX);
        Assert.Equal(ChunksY, grid.ChunksY);
        Assert.Equal(2, grid.ImageryPixelsPerSample);
        Assert.Equal(45d, grid.SlopeThresholds.ImpassableDegrees);
        Assert.Equal(15d, grid.SlopeThresholds.UnbuildableDegrees);

        var transform = grid.Transform;
        Assert.Equal(
            [CoordinateSystemId.Wgs84, CoordinateSystemId.TransverseMercator(9d, 0.9996d, 500000d, 0d)],
            transform.SourceCoordinateSystems);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), transform.ProjectedCoordinateSystem);
        Assert.Equal(new Vector2d(500000d, 6000000d), transform.OriginEastingNorthing);
        Assert.Equal(1d, transform.MetersPerGameUnit);
        Assert.Equal("EGM96", transform.VerticalDatum);
        Assert.Equal(10d, transform.VerticalOriginMeters);
        Assert.Equal(1.5d, transform.VerticalExaggeration);

        var playable = grid.PlayableArea;
        Assert.Equal(5, playable.OuterRing.Positions.Count);
        var hole = Assert.Single(playable.InnerRings);
        Assert.Equal(5, hole.Positions.Count);
        Assert.Equal(100d, hole.Positions[0].Coordinate.X);
    }

    [Fact]
    public void MetadataRoundTrip_PreservesEveryGeometryKindWithAltitudes()
    {
        var original = Metadata();
        var metadata = ReadMetadata(WriteMetadataBytes(original));

        Assert.NotNull(metadata);
        Assert.Equal(original.Features.Count, metadata!.Features.Count);

        var kinds = metadata.Features.Select(feature => feature.Kind).ToList();
        Assert.Equal(
            [
                GeneratedFeatureKind.Waypoint, GeneratedFeatureKind.Landmark, GeneratedFeatureKind.ObjectCandidate,
                GeneratedFeatureKind.Road, GeneratedFeatureKind.River, GeneratedFeatureKind.Water,
                GeneratedFeatureKind.Passability, GeneratedFeatureKind.Buildability, GeneratedFeatureKind.Annotation,
                GeneratedFeatureKind.Waypoint,
            ],
            kinds);

        var waypoint = metadata.Features[0];
        var point = Assert.IsType<GeoPointGeometry>(waypoint.Geometry);
        Assert.Equal(120d, point.Position.AltitudeMeters);
        Assert.Equal(100d, point.Position.Coordinate.X);
        Assert.Equal(200d, point.Position.Coordinate.Y);
        Assert.Equal(30d, waypoint.Position!.Value.Z);
        Assert.Equal(10d, waypoint.Position.Value.X);
        Assert.NotEqual(point.Position.AltitudeMeters, waypoint.Position.Value.Z);

        var road = Assert.IsType<GeoLineString>(metadata.Features[3].Geometry);
        Assert.Equal(3, road.Positions.Count);
        Assert.Equal(5d, road.Positions[0].AltitudeMeters);
        Assert.Null(road.Positions[1].AltitudeMeters);

        var water = Assert.IsType<GeoPolygon>(metadata.Features[5].Geometry);
        Assert.Single(water.InnerRings);

        var annotation = Assert.IsType<GeoMultiGeometry>(metadata.Features[8].Geometry);
        Assert.Equal(2, annotation.Geometries.Count);
        Assert.IsType<GeoPointGeometry>(annotation.Geometries[0]);
        Assert.IsType<GeoLineString>(annotation.Geometries[1]);

        Assert.True(metadata.Features[6].Passable);
        Assert.False(metadata.Features[7].Buildable);
        var placement = metadata.Features[2].ObjectPlacement;
        Assert.NotNull(placement);
        Assert.Equal("TreeOak", placement!.ObjectDefinition);
        Assert.Equal(45d, placement.OrientationDegrees);
    }

    [Fact]
    public void MetadataRoundTrip_PreservesProvenanceManifestHashAndChunkInventory()
    {
        var original = Metadata();
        var metadata = ReadMetadata(WriteMetadataBytes(original));

        Assert.NotNull(metadata);
        Assert.Equal(ManifestSha256Hex, metadata!.ManifestSha256Hex);
        Assert.Equal(["grass", "dirt", "rock"], metadata.Palette.Names);

        Assert.Equal(3, metadata.Provenance.Count);
        var dem = metadata.Provenance[1];
        Assert.Equal("dem", dem.SourceId);
        Assert.Equal(ManifestSourceKind.Geotiff, dem.Kind);
        Assert.Equal(ManifestSourceRole.Elevation, dem.Role);
        Assert.Equal("sources/dem.tif", dem.RelativePath);
        Assert.Equal(Sha256(2), dem.Sha256Hex);
        Assert.Equal(4096L, dem.LengthBytes);
        Assert.Equal(CoordinateSystemId.Utm(31, northernHemisphere: true), dem.ActualCoordinateSystem);
        Assert.Equal("EGM96", dem.DeclaredVerticalDatum);
        Assert.Equal("EGM96", dem.EmbeddedVerticalDatum);
        Assert.Equal(5.5d, dem.HorizontalAccuracyMeters);
        Assert.Equal(7.25d, dem.VerticalAccuracyMeters);

        Assert.Equal(2, metadata.Chunks.Count);
        Assert.Equal(PayloadChunkPaths.Terrain(0, 0), metadata.Chunks[0].Terrain.Path);
        Assert.Equal(PayloadChunkPaths.Terrain(1, 0), metadata.Chunks[1].Terrain.Path);
        Assert.Equal(PayloadChunkDescriptor.TerrainChunkBytes, metadata.Chunks[0].Terrain.LengthBytes);
        Assert.Equal(PayloadChunkDescriptor.MaterialChunkBytes, metadata.Chunks[0].Materials.LengthBytes);
        Assert.Equal(PayloadChunkDescriptor.MaskChunkBytes, metadata.Chunks[0].Masks.LengthBytes);
        Assert.NotNull(metadata.Chunks[0].Imagery);
        Assert.Equal(Sha256(70), metadata.Chunks[0].Imagery!.Sha256Hex);
    }

    [Fact]
    public void MetadataRoundTrip_SharesOneAttributeTableAcrossFeaturesWithoutLoss()
    {
        var shared = SharedAttributes();
        var metadata = ReadMetadata(WriteMetadataBytes(Metadata()));

        Assert.NotNull(metadata);
        var first = metadata!.Features[0];
        var last = metadata.Features[9];

        Assert.Same(first.Attributes, last.Attributes);
        Assert.Equal(3, first.Attributes.Count);
        Assert.Equal("residential", first.Attributes["zone"]);
        Assert.Equal("alpha", first.Attributes["name"]);
        Assert.Equal("hill", first.Attributes["elevation-tag"]);

        var json = Text(WriteMetadataBytes(metadata));
        Assert.Equal(1, CountOccurrences(json, "\"entries\""));
        Assert.Equal(2, CountOccurrences(json, "\"attributeTable\":0"));
    }

    [Fact]
    public void MetadataWrite_SortsAttributeEntriesOrdinal()
    {
        var json = JsonDocument.Parse(WriteMetadataBytes(Metadata()));

        var entries = json.RootElement.GetProperty("features").GetProperty("attributeTables")[0].GetProperty("entries");
        var keys = entries.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Equal(["elevation-tag", "name", "zone"], keys);
    }

    [Fact]
    public void MetadataWrite_IsDeterministicAcrossInstances()
    {
        var first = WriteMetadataBytes(Metadata());
        var second = WriteMetadataBytes(Metadata());

        Assert.Equal(first, second);
    }

    [Fact]
    public void MetadataRoundTrip_IsByteStable()
    {
        var original = WriteMetadataBytes(Metadata());
        var metadata = ReadMetadata(original);

        Assert.NotNull(metadata);
        Assert.Equal(original, WriteMetadataBytes(metadata!));
    }

    [Fact]
    public void ReportRoundTrip_PreservesEverySection()
    {
        var report = ReadReport(WriteReportBytes(Report()));

        Assert.NotNull(report);
        Assert.Equal("panopticon-geospatial-payload-report", report!.Kind);
        Assert.Equal(PayloadSha256Hex, report.PayloadSha256Hex);

        Assert.Equal(2, report.Diagnostics.Count);
        Assert.Equal("GEO-GEN-001", report.Diagnostics[0].Code);
        Assert.Equal(GeoStage.Generate, report.Diagnostics[0].Stage);
        Assert.Equal(GeoSeverity.Warning, report.Diagnostics[0].Severity);
        Assert.Equal("sources/dem.tif", report.Diagnostics[0].SourcePath);

        var terrain = report.Terrain;
        Assert.Equal(2L, terrain.ChunkCount);
        Assert.Equal(1.5f, terrain.MinHeightGameUnits);
        Assert.Equal(87.25f, terrain.MaxHeightGameUnits);
        Assert.Equal(10_000L, terrain.Sampling.SampleCount);
        Assert.Equal(3L, terrain.Sampling.FailedSampleCount);
        Assert.Equal(12L, terrain.Sampling.NoDataFillCount);
        Assert.Equal(5L, terrain.Sampling.OutsideFillCount);
        Assert.Equal(100L, terrain.Sampling.KernelCounts[(int)SampleKernel.Nearest].Count);
        Assert.Equal(8d, terrain.Sampling.ObservedRanges!.SourceGsdXMeters!.Value.Min);
        Assert.Equal(12.5d, terrain.Sampling.ObservedRanges.SourceGsdXMeters.Value.Max);
        Assert.Equal(1d, terrain.Sampling.ObservedRanges.GsdRatioX!.Value.Min);

        Assert.NotNull(report.Imagery);
        Assert.Equal(2L, report.Imagery!.ChunkCount);
        Assert.Equal(20L, report.Imagery.Sampling.SampleCount);

        Assert.Equal(45d, report.Masks.ImpassableThresholdDegrees);
        Assert.Equal(15d, report.Masks.UnbuildableThresholdDegrees);
        Assert.Equal(1.5d, report.Masks.VerticalExaggeration);
        Assert.Equal(100L, report.Masks.ImpassableCellCount);
        Assert.Equal(40L, report.Masks.PlayableForcedCellCount);

        Assert.Equal(10L, report.Features.TotalCount);
        Assert.Equal(2L, report.Features.CountsByKind[(int)GeneratedFeatureKind.Waypoint].Count);

        Assert.Equal(3, report.ValidationChecks.Count);
        var passed = report.ValidationChecks[0];
        Assert.Equal(PayloadValidationStatus.Passed, passed.Status);
        Assert.Equal(30d, passed.Expected);
        Assert.Equal(0.5d, passed.Tolerance);
        Assert.Equal("game-units", passed.Units);
        Assert.Equal(PayloadValidationStatus.Failed, report.ValidationChecks[1].Status);
        Assert.Equal(PayloadValidationStatus.NotEvaluated, report.ValidationChecks[2].Status);
    }

    [Fact]
    public void ReportRoundTrip_WithoutImagery_KeepsByteStability()
    {
        var original = WriteReportBytes(Report(imagery: false));

        var report = ReadReport(original);

        Assert.NotNull(report);
        Assert.Null(report!.Imagery);
        Assert.Equal(original, WriteReportBytes(report));
    }

    [Fact]
    public void ReportWrite_IsDeterministicAcrossInstances()
    {
        Assert.Equal(WriteReportBytes(Report()), WriteReportBytes(Report()));
    }

    [Fact]
    public void ReportWithImageryAndMetadataWithout_RejectsInconsistentInventory()
    {
        var metadata = Metadata(imagery: false);

        var error = Assert.Throws<ArgumentException>(() => new GeneratedMapPayloadMetadata(
            metadata.Grid,
            metadata.Palette,
            metadata.Features,
            metadata.ManifestSha256Hex,
            metadata.Provenance,
            ChunkDescriptors(imagery: true)));

        Assert.Contains("imagery", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedDocuments_AreRejectedAsSchemaInvalid()
    {
        var cases = new (string Name, Func<string, string> Corrupt)[]
        {
            ("duplicate", t => t.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")),
            ("unknown", t => t.Replace("\"grid\":{\"chunksX\"", "\"grid\":{\"surprise\":1,\"chunksX\"")),
            ("bad-enum", t => t.Replace("\"kind\":\"waypoint\"", "\"kind\":\"way-point\"")),
            ("integer-enum", t => t.Replace("\"kind\":\"waypoint\"", "\"kind\":1")),
            ("version", t => t.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")),
            ("kind", t => t.Replace("\"kind\":\"panopticon-geospatial-payload\"", "\"kind\":\"panopticon-geospatial-payload-x\"")),
            ("non-finite", t => t.Replace("\"metersPerGameUnit\":1", "\"metersPerGameUnit\":1e999")),
            ("missing-member", t => t.Replace("\"manifestSha256\":\"" + ManifestSha256Hex + "\",", string.Empty)),
            ("trailing", t => t + " {}"),
        };

        foreach (var (name, corrupt) in cases)
        {
            var diagnostics = new GeoDiagnosticBag();
            var bytes = BytesOf(corrupt(Text(WriteMetadataBytes(Metadata()))));

            var result = ReadMetadata(bytes, diagnostics: diagnostics);

            Assert.Null(result);
            var diagnostic = Assert.Single(diagnostics.Items);
            Assert.Equal(PayloadDiagnosticCodes.PayloadSchemaInvalid, diagnostic.Code);
            Assert.Equal(GeoStage.Parse, diagnostic.Stage);
        }
    }

    [Fact]
    public void ReportDuplicateMember_IsRejected()
    {
        var diagnostics = new GeoDiagnosticBag();
        var bytes = BytesOf(Text(WriteReportBytes(Report())).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"));

        var report = ReadReport(bytes, diagnostics: diagnostics);

        Assert.Null(report);
        Assert.Equal(PayloadDiagnosticCodes.PayloadSchemaInvalid, Assert.Single(diagnostics.Items).Code);
    }

    [Fact]
    public void OversizedDocument_IsRejectedWithLimitDiagnostic()
    {
        var bytes = WriteMetadataBytes(Metadata());
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = bytes.Length - 1 };

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(bytes, limits, diagnostics));
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("maxPayloadMetadataBytes", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChunkCountAboveLimit_IsRejectedBeforeDescriptorAllocation()
    {
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits(MaxChunkCount: 1);

        Assert.Null(ReadMetadata(WriteMetadataBytes(Metadata()), limits, diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("maxChunkCount", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureCountAboveLimit_IsRejectedBeforeFeatureAllocation()
    {
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits(MaxFeatureCount: 2);

        Assert.Null(ReadMetadata(WriteMetadataBytes(Metadata()), limits, diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("maxFeatureCount", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinateCountAboveLimit_IsRejectedDuringPlayablAreaParse()
    {
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 3);

        Assert.Null(ReadMetadata(WriteMetadataBytes(Metadata()), limits, diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("maxCoordinateCount", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PaletteAboveFixedMaximum_IsRejectedBeforePaletteAllocation()
    {
        var palette = string.Join(",", Enumerable.Range(0, MaterialPalette.MaxEntries + 1).Select(index => $"\"m{index}\""));
        var json = Text(WriteMetadataBytes(Metadata())).Replace("\"palette\":[\"grass\",\"dirt\",\"rock\"]", $"\"palette\":[{palette}]");

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("palette", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamReadFailure_ReportsResourceDiagnostic()
    {
        var diagnostics = new GeoDiagnosticBag();
        using var stream = new ThrowingStream();

        var result = PayloadJson.TryReadMetadata(stream, diagnostics, new GeospatialImportLimits());

        Assert.Null(result);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadResourceFailure, diagnostic.Code);
    }

    [Fact]
    public void CancelledToken_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ReadMetadata(WriteMetadataBytes(Metadata()), cancellationToken: source.Token));
    }

    [Fact]
    public void NonCanonicalChunkPath_IsRejectedAsValueInvalid()
    {
        var json = Text(WriteMetadataBytes(Metadata())).Replace("\"path\":\"terrain/0_0.bin\"", "\"path\":\"terrain/0_0.alt\"");

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Validate, diagnostic.Stage);
    }

    [Fact]
    public void InvalidTransformValue_IsRejectedAsValueInvalid()
    {
        var json = Text(WriteMetadataBytes(Metadata())).Replace("\"metersPerGameUnit\":1", "\"metersPerGameUnit\":-1");

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
    }

    [Fact]
    public void ChunkDescriptors_MustMatchGridInSouthwestRowMajorOrder()
    {
        var metadata = Metadata();
        var swapped = metadata.Chunks.Reverse().ToList();

        Assert.Throws<ArgumentException>(() => new GeneratedMapPayloadMetadata(
            metadata.Grid,
            metadata.Palette,
            metadata.Features,
            metadata.ManifestSha256Hex,
            metadata.Provenance,
            swapped));
    }

    [Fact]
    public void Provenance_RejectsDuplicateSourceIds()
    {
        var metadata = Metadata();
        var duplicated = metadata.Provenance.Append(metadata.Provenance[0]).ToList();

        Assert.Throws<ArgumentException>(() => new GeneratedMapPayloadMetadata(
            metadata.Grid,
            metadata.Palette,
            metadata.Features,
            metadata.ManifestSha256Hex,
            duplicated,
            metadata.Chunks));
    }

    [Fact]
    public void FileReference_RejectsTraversalAndAbsolutePaths()
    {
        Assert.Throws<ArgumentException>(() => new PayloadFileReference("/terrain/0_0.bin", 1, Sha256(1)));
        Assert.Throws<ArgumentException>(() => new PayloadFileReference("terrain/../terrain/0_0.bin", 1, Sha256(1)));
        Assert.Throws<ArgumentException>(() => new PayloadFileReference("terrain\\0_0.bin", 1, Sha256(1)));
        Assert.Throws<ArgumentException>(() => new PayloadFileReference("terrain//0_0.bin", 1, Sha256(1)));
        Assert.Throws<ArgumentException>(() => new PayloadFileReference("terrain/0_0.bin", 1, "NOT-A-SHA"));
    }

    [Fact]
    public void PayloadDiagnostic_RejectsAbsoluteSourcePathsAndDropsExceptionDetail()
    {
        Assert.Throws<ArgumentException>(() => new PayloadDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Warning, "m", "/tmp/dem.tif"));

        var runtime = new GeoDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Warning, "message", "sources/dem.tif", detail: new IOException("boom"));
        var serialized = PayloadDiagnostic.FromGeoDiagnostic(runtime);

        Assert.Equal("sources/dem.tif", serialized.SourcePath);
        Assert.Equal("message", serialized.Message);

        Assert.Throws<ArgumentException>(() => PayloadDiagnostic.FromGeoDiagnostic(
            new GeoDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Warning, "m", "/absolute/dem.tif")));
    }

    [Fact]
    public void ValidationCheck_RejectsNonFiniteNumbersAndNegativeTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PayloadValidationCheck("c", "n", PayloadValidationStatus.Passed, expected: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PayloadValidationCheck("c", "n", PayloadValidationStatus.Passed, tolerance: -1d));
    }

    [Fact]
    public void SamplingSummary_RejectsUnorderedKernelsAndOutOfRangeCounts()
    {
        var reversed = KernelCounts(0, 0, 0, 0);
        reversed.Reverse();

        Assert.Throws<ArgumentException>(() => new PayloadSamplingSummary(1, 0, 0, 0, reversed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PayloadSamplingSummary(0, 1, 0, 0, KernelCounts(0, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => new PayloadSamplingSummary(1, 0, 0, 0, KernelCounts(2, 0, 0, 0)));
    }

    [Fact]
    public void FeatureSummary_RejectsCountsNotSummingToTotal()
    {
        var counts = FeaturesSummary(PayloadTestSupport.Features()).CountsByKind.ToList();
        counts[0] = new PayloadFeatureKindCount(counts[0].Kind, counts[0].Count + 1);

        Assert.Throws<ArgumentException>(() => new PayloadFeatureGenerationSummary(PayloadTestSupport.Features(SharedAttributes()).Count, counts));
    }

    [Fact]
    public void Models_DefensivelyCopySourceLists()
    {
        var features = PayloadTestSupport.Features();
        var provenance = Provenance();
        var chunks = ChunkDescriptors();
        var metadata = new GeneratedMapPayloadMetadata(PayloadTestSupport.Grid(), new MaterialPalette(["grass"]), features, ManifestSha256Hex, provenance, chunks);

        features.Clear();
        provenance.Clear();
        chunks.Clear();

        Assert.Equal(10, metadata.Features.Count);
        Assert.Equal(3, metadata.Provenance.Count);
        Assert.Equal(2, metadata.Chunks.Count);

        var diagnostics = new List<PayloadDiagnostic>();
        var report = new GeneratedMapPayloadReport(
            diagnostics,
            new PayloadTerrainGenerationSummary(SamplingSummary(1, 0, 0, 0, 1d, 1d, 1d, 1d), 1),
            MasksSummary(),
            FeaturesSummary([]),
            [],
            PayloadSha256Hex);
        diagnostics.Add(new PayloadDiagnostic("X", GeoStage.Validate, GeoSeverity.Error, "later"));

        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void GeoImportResult_EnforcesSuccessAndPayloadPathConsistency()
    {
        Assert.ThrowsAny<ArgumentException>(() => new GeoImportResult(success: true));
        Assert.ThrowsAny<ArgumentException>(() => new GeoImportResult(success: false, payloadPath: "/out/payload"));
        Assert.ThrowsAny<ArgumentException>(
            () => new GeoImportResult(
                success: true,
                payloadPath: "/out/payload",
                diagnostics: [new GeoDiagnostic("X", GeoStage.Write, GeoSeverity.Error, "boom")]));

        var result = new GeoImportResult(
            success: true,
            payloadPath: "/out/payload",
            retainedBackupPath: "/out/payload.backup-1",
            report: Report());

        Assert.True(result.Success);
        Assert.Equal("/out/payload", result.PayloadPath);
        Assert.Equal("/out/payload.backup-1", result.RetainedBackupPath);
        Assert.NotNull(result.Report);
    }

    [Fact]
    public void GeoImportOptions_CopiesOrderedValidators()
    {
        var first = new StubValidator();
        var second = new StubValidator();
        var validators = new List<IStagedPayloadValidator> { first, second };

        var options = new GeoImportOptions(stagedValidators: validators);
        validators.Clear();

        Assert.Equal([first, second], options.StagedValidators);
        Assert.False(options.Force);
        Assert.Equal(128L * 1024 * 1024, options.Limits.MaxPayloadMetadataBytes);
    }

    [Fact]
    public void ReadBeyondByteLimit_StillStopsIncrementally()
    {
        // Sanity companion to the streaming reader: the limit fires mid-document without
        // buffering the remainder.
        var counting = new CountingReadStream(WriteMetadataBytes(Metadata()), chunkBytes: 4096);
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 512 };
        var diagnostics = new GeoDiagnosticBag();

        using (counting)
        {
            Assert.Null(PayloadJson.TryReadMetadata(counting, diagnostics, limits));
        }

        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, Assert.Single(diagnostics.Items).Code);
        Assert.True(counting.BytesServed <= 16 * 1024, $"served {counting.BytesServed}");
    }

    [Fact]
    public void PrecancelledRead_PerformsZeroStreamReads()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        using var counting = new CountingReadStream(WriteMetadataBytes(Metadata()), chunkBytes: 4096);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Throws<OperationCanceledException>(() => PayloadJson.TryReadMetadata(counting, diagnostics, new GeospatialImportLimits(), source.Token));
        Assert.Equal(0, counting.ReadCount);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void CancellationDuringStreamReads_Propagates()
    {
        using var source = new CancellationTokenSource();
        using var cancelling = new CancellingReadStream(WriteMetadataBytes(Metadata()), cancelsAfterReads: 4, source);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Throws<OperationCanceledException>(() => PayloadJson.TryReadMetadata(cancelling, diagnostics, new GeospatialImportLimits(), source.Token));
        Assert.True(cancelling.ReadCount > 0);
        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void StructuralLimit_StopsReadingBeforeDocumentEnd()
    {
        // The palette limit fires at entry 256 of 300; the trailing 1 MiB filler member is
        // never pulled from the stream.
        var palette = string.Join(",", Enumerable.Range(0, 300).Select(index => $"\"m{index}\""));
        var filler = new string('a', 1024 * 1024);
        var json = Encoding.UTF8.GetBytes(
            "{\"kind\":\"panopticon-geospatial-payload\",\"schemaVersion\":1,\"palette\":[" + palette + "],\"filler\":\"" + filler + "\"}");

        using var counting = new CountingReadStream(json, chunkBytes: 4096);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadJson.TryReadMetadata(counting, diagnostics, new GeospatialImportLimits()));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("palette", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(counting.BytesServed < json.Length / 2, $"served {counting.BytesServed} of {json.Length}");
    }

    [Fact]
    public void WriteBeyondLimit_FailsWithoutOversizeEmission()
    {
        // A mid-document limit: incremental flushes make progress, then the bound rejects
        // the remainder without emitting any byte past the limit.
        var full = WriteMetadataBytes(Metadata());
        var counting = new CountingWriteStream();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = full.Length / 2 };

        using (counting)
        {
            Assert.Throws<PayloadJsonLimitException>(
                () => PayloadJson.WriteMetadata(counting, Metadata(), limits));
        }

        Assert.True(counting.WrittenBytes > 0, "streaming write must make progress before failing");
        Assert.True(counting.WrittenBytes <= limits.MaxPayloadMetadataBytes, $"wrote {counting.WrittenBytes} of limit {limits.MaxPayloadMetadataBytes}");
    }

    [Fact]
    public void WriteBeyondLimit_ReportFailsWithoutOversizeEmission()
    {
        var counting = new CountingWriteStream();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 256 };

        using (counting)
        {
            Assert.Throws<PayloadJsonLimitException>(
                () => PayloadJson.WriteReport(counting, Report(), limits));
        }

        Assert.True(counting.WrittenBytes <= limits.MaxPayloadMetadataBytes, $"wrote {counting.WrittenBytes}");
    }

    [Fact]
    public void OversizeSingleString_IsRejectedBeforeEncoding()
    {
        var huge = new string('x', 200_000);
        var metadata = Metadata();
        var withHugeAttribute = new GeneratedMapPayloadMetadata(
            metadata.Grid,
            metadata.Palette,
            [GeneratedFeature.CreateShared(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(0d, 0d)), new GeoPosition(new Vector2d(1d, 1d))]),
                "vectors",
                "huge",
                "vectors/huge#0",
                -1,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = huge },
                null,
                null,
                null,
                null)],
            metadata.ManifestSha256Hex,
            metadata.Provenance,
            metadata.Chunks);

        var counting = new CountingWriteStream();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 64 * 1024 };

        using (counting)
        {
            Assert.Throws<PayloadJsonLimitException>(
                () => PayloadJson.WriteMetadata(counting, withHugeAttribute, limits));
        }

        Assert.True(counting.WrittenBytes <= limits.MaxPayloadMetadataBytes, $"wrote {counting.WrittenBytes}");
    }

    [Fact]
    public void Write_UsesStandardLimitByDefault()
    {
        // The parameterless write path applies the same 128 MiB bound as reads.
        Assert.Throws<PayloadJsonLimitException>(
            () => PayloadJson.WriteMetadata(Stream.Null, Metadata(), new GeospatialImportLimits { MaxPayloadMetadataBytes = 8 }));
    }

    [Theory]
    [InlineData("unclosed outer ring", "{\"kind\":\"water\",\"geometry\":{\"type\":\"polygon\",\"outerRing\":{\"positions\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0},{\"x\":10,\"y\":10},{\"x\":0,\"y\":10}]},\"innerRings\":[]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}", "closed")]
    [InlineData("one position ring", "{\"kind\":\"water\",\"geometry\":{\"type\":\"polygon\",\"outerRing\":{\"positions\":[{\"x\":0,\"y\":0}]},\"innerRings\":[]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}", "three distinct")]
    [InlineData("collinear ring", "{\"kind\":\"water\",\"geometry\":{\"type\":\"polygon\",\"outerRing\":{\"positions\":[{\"x\":0,\"y\":0},{\"x\":5,\"y\":0},{\"x\":10,\"y\":0},{\"x\":0,\"y\":0}]},\"innerRings\":[]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}", "zero area")]
    [InlineData("unclosed hole", "{\"kind\":\"water\",\"geometry\":{\"type\":\"polygon\",\"outerRing\":{\"positions\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0},{\"x\":10,\"y\":10},{\"x\":0,\"y\":0}]},\"innerRings\":[{\"positions\":[{\"x\":2,\"y\":2},{\"x\":4,\"y\":2},{\"x\":4,\"y\":4},{\"x\":2,\"y\":4}]}]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}", "closed")]
    [InlineData("line with one position", "{\"kind\":\"road\",\"geometry\":{\"type\":\"line-string\",\"positions\":[{\"x\":0,\"y\":0}]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}", "at least two")]
    public void DegenerateFeatureGeometry_IsRejectedAsValueError(string caseName, string featureJson, string expectedFragment)
    {
        var diagnostics = new GeoDiagnosticBag();
        var bytes = Encoding.UTF8.GetBytes(MetadataJsonWithFeature(featureJson));

        var metadata = ReadMetadata(bytes, diagnostics: diagnostics);

        Assert.True(metadata is null, caseName);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
        Assert.Contains(expectedFragment, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidClosedFeaturePolygon_RoundTrips()
    {
        var featureJson = "{\"kind\":\"water\",\"geometry\":{\"type\":\"polygon\",\"outerRing\":{\"positions\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0},{\"x\":10,\"y\":10},{\"x\":0,\"y\":0}]},\"innerRings\":[]},\"sourceId\":\"v\",\"featureId\":\"f\",\"partId\":\"p\",\"ruleIndex\":-1}";
        var diagnostics = new GeoDiagnosticBag();

        var metadata = ReadMetadata(Encoding.UTF8.GetBytes(MetadataJsonWithFeature(featureJson)), diagnostics: diagnostics);

        Assert.NotNull(metadata);
        Assert.True(diagnostics.Items.Count == 0 || diagnostics.Items.All(item => item.Severity != GeoSeverity.Error));
    }

    [Fact]
    public void KnownDatumConflict_IsRejectedAtConstruction()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PayloadSourceProvenance(
            "dem", ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation, "sources/dem.tif", Sha256(1), 1L,
            CoordinateSystemId.Wgs84, "EGM96", embeddedVerticalDatum: "EGM2008"));

        // Case-insensitive and whitespace-tolerant agreement is accepted.
        new PayloadSourceProvenance(
            "dem", ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation, "sources/dem.tif", Sha256(1), 1L,
            CoordinateSystemId.Wgs84, " egm96 ", embeddedVerticalDatum: "EGM96");

        // Unknown or unrecognized datums are unconstrained; null embedded is permitted.
        new PayloadSourceProvenance(
            "dem", ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation, "sources/dem.tif", Sha256(1), 1L,
            CoordinateSystemId.Wgs84, "unknown", embeddedVerticalDatum: "EGM2008");
        new PayloadSourceProvenance(
            "dem", ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation, "sources/dem.tif", Sha256(1), 1L,
            CoordinateSystemId.Wgs84, "NAVD88", embeddedVerticalDatum: "EGM96");
    }

    [Fact]
    public void KnownDatumConflict_IsRejectedOnRead()
    {
        var json = Text(WriteMetadataBytes(Metadata()))
            .Replace("\"embeddedVerticalDatum\":\"EGM96\"", "\"embeddedVerticalDatum\":\"EGM2008\"");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
        Assert.Contains("geoid", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCounts_CannotOverflowPastSampleCount()
    {
        var counts = KernelCounts(long.MaxValue, long.MaxValue, 0, 0);

        var error = Assert.Throws<ArgumentException>(
            () => new PayloadSamplingSummary(0, 0, 0, 0, counts));

        Assert.Contains("recorded samples", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureKindCounts_CannotOverflowPastTotal()
    {
        var counts = new List<PayloadFeatureKindCount>();
        var index = 0;
        foreach (GeneratedFeatureKind kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            counts.Add(new PayloadFeatureKindCount(kind, index < 2 ? long.MaxValue : 0));
            index++;
        }

        var error = Assert.Throws<ArgumentException>(
            () => new PayloadFeatureGenerationSummary(0, counts));

        Assert.Contains("declared total", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateKernelToken_IsRejectedDuringParse()
    {
        var json = Text(WriteReportBytes(Report())).Replace("\"kernel\":\"bilinear\"", "\"kernel\":\"nearest\"");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadReport(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadSchemaInvalid, diagnostic.Code);
        Assert.Contains("Duplicate kernel", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFeatureKindToken_IsRejectedDuringParse()
    {
        var json = Text(WriteReportBytes(Report())).Replace("\"kind\":\"landmark\"", "\"kind\":\"waypoint\"");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadReport(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadSchemaInvalid, diagnostic.Code);
        Assert.Contains("Duplicate feature kind", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCountsBeyondEnumSize_IsRejectedDuringParse()
    {
        var json = Text(WriteReportBytes(Report()))
            .Replace("\"kernel\":\"area-weighted\",\"count\":2}", "\"kernel\":\"area-weighted\",\"count\":2},{\"kernel\":\"auto\",\"count\":0}");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadReport(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, diagnostic.Code);
        Assert.Contains("kernels", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TinyCapUnterminatedToken_PullsAtMostCapPlusOneByte()
    {
        // One unterminated 1 MiB string token under a tiny cap: growth stops at the
        // budget, the probe byte proves the excess, and nothing more is pulled.
        var json = Encoding.UTF8.GetBytes("{\"k\":\"" + new string('a', 1024 * 1024));
        using var counting = new CountingReadStream(json, chunkBytes: 4096);
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 512 };
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(PayloadJson.TryReadMetadata(counting, diagnostics, limits));

        Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, Assert.Single(diagnostics.Items).Code);
        Assert.True(counting.BytesServed <= limits.MaxPayloadMetadataBytes + 1, $"served {counting.BytesServed}");
    }

    [Fact]
    public void LargeSingleTokenDocument_ReadsExactlyWithinBudget()
    {
        // A 100 KB attribute value forms one large string token; the incremental reader
        // completes it with the budget at the exact document size and fails one byte below.
        var metadata = MetadataWithAttributeValue(new string('v', 100_000));
        var bytes = WriteMetadataBytes(metadata);

        using (var exact = new CountingReadStream(bytes, chunkBytes: 4096))
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.NotNull(PayloadJson.TryReadMetadata(exact, diagnostics, new GeospatialImportLimits { MaxPayloadMetadataBytes = bytes.Length }));
            Assert.False(diagnostics.HasErrors);
        }

        using (var below = new CountingReadStream(bytes, chunkBytes: 4096))
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.Null(PayloadJson.TryReadMetadata(below, diagnostics, new GeospatialImportLimits { MaxPayloadMetadataBytes = bytes.Length - 1 }));

            Assert.Equal(PayloadDiagnosticCodes.PayloadLimitExceeded, Assert.Single(diagnostics.Items).Code);
            Assert.True(below.BytesServed <= bytes.Length, $"served {below.BytesServed}");
        }
    }

    [Theory]
    [InlineData("plain-ascii-value-0123456789")]
    [InlineData("html <b>&amp; escaped")]
    [InlineData("quotes \" backslash \\ and newline \n")]
    [InlineData("surrogate pair \uD83D\uDE00")]
    [InlineData("mixed 😀 \" < \\ ünïcödé")]
    public void StringBudget_UsesActualEscapedLength(string value)
    {
        var metadata = MetadataWithAttributeValue(value);
        using var full = new MemoryStream();
        PayloadJson.WriteMetadata(full, metadata);
        var exact = full.Length;

        // The exact document size passes; one byte below fails with a typed limit.
        using var atLimit = new MemoryStream();
        PayloadJson.WriteMetadata(atLimit, metadata, new GeospatialImportLimits { MaxPayloadMetadataBytes = exact });
        Assert.Equal(exact, atLimit.Length);

        using var belowLimit = new MemoryStream();
        Assert.Throws<PayloadJsonLimitException>(
            () => PayloadJson.WriteMetadata(belowLimit, metadata, new GeospatialImportLimits { MaxPayloadMetadataBytes = exact - 1 }));
    }

    [Fact]
    public void Grid_RejectsGeographicOrUnsupportedProjectedCrs()
    {
        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithProjected(CoordinateSystemId.Wgs84));
        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithProjected(CoordinateSystemId.FromEpsg(27700)));

        // WGS84 UTM and well-formed custom Transverse Mercator remain accepted.
        PayloadTestSupport.GridWithProjected(CoordinateSystemId.Utm(31, northernHemisphere: true));
        PayloadTestSupport.GridWithProjected(CoordinateSystemId.TransverseMercator(9d, 0.9996d, 500000d, 0d));
    }

    [Fact]
    public void Grid_RejectsUnsupportedSourceCrs()
    {
        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithSources([CoordinateSystemId.FromEpsg(27700)]));

        // The default struct is a malformed Transverse Mercator (scale zero).
        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithSources([default]));

        PayloadTestSupport.GridWithSources([CoordinateSystemId.Wgs84, CoordinateSystemId.Utm(31, northernHemisphere: true)]);
    }

    [Fact]
    public void Grid_RejectsDegeneratePlayableRings()
    {
        // A single position is trivially closed but has no area.
        var onePoint = new GeoPolygon(PayloadTestSupport.Ring((500000d, 6000000d)), []);

        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithPlayableArea(onePoint));

        // Closed collinear hole: four stored positions, two distinct corners, zero area.
        var collinearHole = new GeoPolygon(
            PayloadTestSupport.Ring((0d, 0d), (1280d, 0d), (1280d, 1280d), (0d, 1280d), (0d, 0d)),
            [PayloadTestSupport.Ring((100d, 100d), (150d, 100d), (200d, 100d), (100d, 100d))]);

        Assert.ThrowsAny<ArgumentException>(() => PayloadTestSupport.GridWithPlayableArea(collinearHole));
    }

    [Fact]
    public void Grid_RejectsNonFiniteAltitudesInPlayableRings()
    {
        var ring = new GeoRing(
        [
            new GeoPosition(new Vector2d(0d, 0d), double.NaN),
            new GeoPosition(new Vector2d(10d, 0d)),
            new GeoPosition(new Vector2d(10d, 10d)),
            new GeoPosition(new Vector2d(0d, 0d), double.NaN),
        ]);

        Assert.ThrowsAny<ArgumentException>(
            () => PayloadTestSupport.GridWithPlayableArea(new GeoPolygon(ring, [])));
    }

    [Fact]
    public void Metadata_RejectsKnownMapDatumConflictingWithElevationSource()
    {
        var metadata = Metadata();
        var conflicting = metadata.Provenance.Select(source => source.SourceId == "dem"
            ? new PayloadSourceProvenance(
                source.SourceId,
                source.Kind,
                source.Role,
                source.RelativePath,
                source.Sha256Hex,
                source.LengthBytes,
                source.ActualCoordinateSystem,
                "EGM2008")
            : source).ToList();

        Assert.ThrowsAny<ArgumentException>(() => new GeneratedMapPayloadMetadata(
            metadata.Grid,
            metadata.Palette,
            metadata.Features,
            metadata.ManifestSha256Hex,
            conflicting,
            metadata.Chunks));

        // An unknown map datum is resolved later by the importer and stays permitted.
        var unknownRoot = new GeneratedMapPayloadMetadata(
            PayloadTestSupport.GridWithTransform(verticalDatum: "unknown"),
            metadata.Palette,
            metadata.Features,
            metadata.ManifestSha256Hex,
            metadata.Provenance,
            metadata.Chunks);

        Assert.Equal("unknown", unknownRoot.Grid.Transform.VerticalDatum);
    }

    [Fact]
    public void GeographicTargetCrs_IsRejectedOnRead()
    {
        var json = Text(WriteMetadataBytes(Metadata()))
            .Replace("\"projectedCoordinateSystem\":{\"epsg\":32631}", "\"projectedCoordinateSystem\":{\"epsg\":4326}");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
        Assert.Contains("geographic", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapDatumConflictingWithElevationSource_IsRejectedOnRead()
    {
        var json = Text(WriteMetadataBytes(Metadata()))
            .Replace("\"metersPerGameUnit\":1,\"verticalDatum\":\"EGM96\"", "\"metersPerGameUnit\":1,\"verticalDatum\":\"EGM2008\"");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadMetadata(BytesOf(json), diagnostics: diagnostics));

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, diagnostic.Code);
        Assert.Contains("elevation source", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LongMaxValueBudgets_RoundTripMetadataAndReport()
    {
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = long.MaxValue };

        using var metadataBytes = new MemoryStream();
        PayloadJson.WriteMetadata(metadataBytes, Metadata(), limits);

        using var metadataStream = new MemoryStream(metadataBytes.ToArray(), writable: false);
        var metadata = PayloadJson.TryReadMetadata(metadataStream, new GeoDiagnosticBag(), limits);

        Assert.NotNull(metadata);
        Assert.Equal(ManifestSha256Hex, metadata!.ManifestSha256Hex);

        using var reportBytes = new MemoryStream();
        PayloadJson.WriteReport(reportBytes, Report(), limits);

        using var reportStream = new MemoryStream(reportBytes.ToArray(), writable: false);
        var report = PayloadJson.TryReadReport(reportStream, new GeoDiagnosticBag(), limits);

        Assert.NotNull(report);
        Assert.Equal(PayloadSha256Hex, report!.PayloadSha256Hex);
    }

    [Fact]
    public void LongMaxValueBudget_SupportsLargeTokenWindowGrowth()
    {
        // A 100 KB single-token value under a long.MaxValue budget exercises the growth
        // path where the remainder is clamped to the addressable headroom, not the budget.
        var metadata = MetadataWithAttributeValue(new string('g', 100_000));
        var bytes = WriteMetadataBytes(metadata);
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = long.MaxValue };

        using var counting = new CountingReadStream(bytes, chunkBytes: 4096);
        var diagnostics = new GeoDiagnosticBag();

        Assert.NotNull(PayloadJson.TryReadMetadata(counting, diagnostics, limits));
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(bytes.Length, counting.BytesServed);
    }

    [Fact]
    public void LargeLineGeometry_UnderSmallCap_FailsBounded()
    {
        // A 250k-position line under a 64 KiB cap: batched flushes reach the bounded
        // stream during the geometry instead of buffering it whole.
        var counting = new CountingWriteStream();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 64 * 1024 };

        using (counting)
        {
            Assert.Throws<PayloadJsonLimitException>(
                () => PayloadJson.WriteMetadata(counting, MetadataWithLine(250_000), limits));
        }

        Assert.True(counting.WrittenBytes <= limits.MaxPayloadMetadataBytes, $"wrote {counting.WrittenBytes}");
    }

    [Fact]
    public void CancellationDuringGeometryWrite_Propagates()
    {
        using var source = new CancellationTokenSource();
        using var counting = new CountingWriteStream(cancelsAfter: source, cancelAfterWrites: 3);

        Assert.Throws<OperationCanceledException>(
            () => PayloadJson.WriteMetadata(counting, MetadataWithLine(250_000), cancellationToken: source.Token));
        Assert.True(counting.WrittenBytes > 0);
    }

    [Fact]
    public void AttributePairBudget_HugeKeyAndValue_FailsBounded()
    {
        // 40k key plus 40k value under a 64 KiB cap: the chained checks must reject the
        // pair instead of letting both pass against the same writer counters.
        var counting = new CountingWriteStream();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 64 * 1024 };

        using (counting)
        {
            Assert.Throws<PayloadJsonLimitException>(
                () => PayloadJson.WriteMetadata(
                    counting,
                    MetadataWithAttributePair(new string('k', 40_000), new string('v', 40_000)),
                    limits));
        }

        Assert.True(counting.WrittenBytes <= limits.MaxPayloadMetadataBytes, $"wrote {counting.WrittenBytes}");
    }

    [Theory]
    [InlineData("key-plain", "value-plain-0123456789")]
    [InlineData("html-<key>&", "value <b>&amp; escaped")]
    [InlineData("quotes-\"key\"", "value \" backslash \\ newline \n")]
    [InlineData("surrogate-\uD83D\uDE00", "pair 😀 \" < \\ ünïcödé")]
    public void AttributePairBudget_UsesCombinedExactEncodedLength(string key, string value)
    {
        var metadata = MetadataWithAttributePair(key, value);
        using var full = new MemoryStream();
        PayloadJson.WriteMetadata(full, metadata);
        var exact = full.Length;

        using var atLimit = new MemoryStream();
        PayloadJson.WriteMetadata(atLimit, metadata, new GeospatialImportLimits { MaxPayloadMetadataBytes = exact });
        Assert.Equal(exact, atLimit.Length);

        using var belowLimit = new MemoryStream();
        Assert.Throws<PayloadJsonLimitException>(
            () => PayloadJson.WriteMetadata(belowLimit, metadata, new GeospatialImportLimits { MaxPayloadMetadataBytes = exact - 1 }));
    }

    [Fact]
    public void ReportClassificationSampling_RoundTripsDistinctFromTerrainAndImagery()
    {
        var classification = new PayloadSamplingSummary(
            5_000,
            2,
            7,
            3,
            KernelCounts(0, 4_998, 0, 0),
            new PayloadObservedRanges(
                new PayloadObservedRange(30d, 31.5d),
                new PayloadObservedRange(30d, 31.5d),
                new PayloadObservedRange(90d, 94.5d),
                new PayloadObservedRange(90d, 94.5d),
                new PayloadObservedRange(3d, 3d),
                new PayloadObservedRange(3d, 3d)));

        var original = WriteReportBytes(Report(classificationSampling: classification));

        var report = ReadReport(original);

        Assert.NotNull(report);
        Assert.NotNull(report!.ClassificationSampling);
        Assert.Equal(5_000L, report.ClassificationSampling.SampleCount);
        Assert.Equal(2L, report.ClassificationSampling.FailedSampleCount);
        Assert.Equal(7L, report.ClassificationSampling.NoDataFillCount);
        Assert.Equal(3L, report.ClassificationSampling.OutsideFillCount);
        Assert.Equal(4_998L, report.ClassificationSampling.KernelCounts[(int)SampleKernel.Nearest].Count);
        Assert.Equal(30d, report.ClassificationSampling.ObservedRanges!.SourceGsdXMeters!.Value.Min);
        Assert.Equal(3d, report.ClassificationSampling.ObservedRanges.GsdRatioX!.Value.Max);

        // The terrain and imagery summaries stay separate and untouched.
        Assert.Equal(10_000L, report.Terrain.Sampling.SampleCount);
        Assert.Equal(100L, report.Terrain.Sampling.KernelCounts[(int)SampleKernel.Nearest].Count);
        Assert.NotNull(report.Imagery);
        Assert.Equal(20L, report.Imagery!.Sampling.SampleCount);

        Assert.Equal(original, WriteReportBytes(report));
    }

    [Fact]
    public void ReportClassificationSampling_OmittedWhenNullStaysBackwardCompatible()
    {
        var defaultReport = ReadReport(WriteReportBytes(Report()));

        Assert.NotNull(defaultReport);
        Assert.Null(defaultReport!.ClassificationSampling);
        Assert.Equal(0, CountOccurrences(Text(WriteReportBytes(Report())), "\"classificationSampling\""));

        // A report written before the member existed parses with a null summary.
        var withClassification = Text(WriteReportBytes(Report(classificationSampling: SamplingSummary(9, 1, 0, 0, 1d, 1d, 1d, 1d))));
        var start = withClassification.IndexOf("\"classificationSampling\"", StringComparison.Ordinal);
        var end = withClassification.IndexOf("\"masks\"", StringComparison.Ordinal);
        var stripped = withClassification[..start] + withClassification[end..];

        var strippedReport = ReadReport(BytesOf(stripped));

        Assert.NotNull(strippedReport);
        Assert.Null(strippedReport!.ClassificationSampling);
        Assert.Equal(PayloadSha256Hex, strippedReport.PayloadSha256Hex);
    }

    [Fact]
    public void MalformedClassificationSampling_IsRejectedAsSchemaInvalid()
    {
        var cases = new Func<string, string>[]
        {
            // Unknown kernel token inside the classification summary.
            t => t.Replace("\"kernel\":\"nearest\",\"count\":4998", "\"kernel\":\"nearest-x\",\"count\":4998"),
            // Duplicate kernel token inside the classification summary (entry count stays four).
            t => t.Replace("\"kernel\":\"area-weighted\",\"count\":0}]},\"masks\":{", "\"kernel\":\"nearest\",\"count\":0}]},\"masks\":{"),
            // Duplicate classificationSampling member at the report root.
            t => t.Replace("\"masks\":{", "\"classificationSampling\":{\"sampleCount\":1},\"masks\":{"),
        };

        var original = Text(WriteReportBytes(Report(classificationSampling: new PayloadSamplingSummary(
            5_000, 2, 7, 3, KernelCounts(0, 4_998, 0, 0)))));

        foreach (var corrupt in cases)
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.Null(ReadReport(BytesOf(corrupt(original)), diagnostics: diagnostics));

            Assert.Equal(PayloadDiagnosticCodes.PayloadSchemaInvalid, Assert.Single(diagnostics.Items).Code);
        }
    }

    [Fact]
    public void InvalidClassificationSamplingCounts_IsRejectedAsValueError()
    {
        var json = Text(WriteReportBytes(Report(classificationSampling: new PayloadSamplingSummary(
            5_000, 2, 7, 3, KernelCounts(0, 4_998, 0, 0)))))
            .Replace("\"sampleCount\":5000,\"failedSampleCount\":2", "\"sampleCount\":2,\"failedSampleCount\":2");

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(ReadReport(BytesOf(json), diagnostics: diagnostics));

        Assert.Equal(PayloadDiagnosticCodes.PayloadValueInvalid, Assert.Single(diagnostics.Items).Code);
    }

    [Fact]
    public void ReportStreamReadFailure_ReportsResourceDiagnostic()
    {
        var diagnostics = new GeoDiagnosticBag();
        using var stream = new ThrowingStream();

        var report = PayloadJson.TryReadReport(stream, diagnostics, new GeospatialImportLimits());

        Assert.Null(report);
        Assert.Equal(PayloadDiagnosticCodes.PayloadResourceFailure, Assert.Single(diagnostics.Items).Code);
    }

    private sealed class StubValidator : IStagedPayloadValidator
    {
        public IReadOnlyList<PayloadValidationCheck> Validate(
            IGeneratedMapPayloadReader reader,
            GeospatialImportManifest manifest,
            GeoDiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            return [];
        }
    }

    /// <summary>Forward-only chunked read stream that counts reads and bytes served; never seekable.</summary>
    private sealed class CountingReadStream(byte[] data, int chunkBytes) : Stream
    {
        private readonly byte[] _data = data;
        private readonly int _chunkBytes = chunkBytes;
        private int _position;

        public int ReadCount { get; private set; }

        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;

            var remaining = _data.Length - _position;
            var served = Math.Min(count, Math.Min(_chunkBytes, remaining));

            if (served > 0)
            {
                Buffer.BlockCopy(_data, _position, buffer, offset, served);
                _position += served;
                BytesServed += served;
            }

            return served;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Read stream that cancels an external source after a configured number of reads.</summary>
    private sealed class CancellingReadStream(byte[] data, int cancelsAfterReads, CancellationTokenSource source, int chunkBytes = 64) : Stream
    {
        private readonly byte[] _data = data;
        private readonly int _cancelsAfterReads = cancelsAfterReads;
        private readonly CancellationTokenSource _source = source;
        private readonly int _chunkBytes = chunkBytes;
        private int _position;

        public int ReadCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;

            var served = Math.Min(count, Math.Min(_chunkBytes, _data.Length - _position));
            if (served > 0)
            {
                Buffer.BlockCopy(_data, _position, buffer, offset, served);
                _position += served;
            }

            if (ReadCount >= _cancelsAfterReads)
            {
                _source.Cancel();
            }

            return served;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Write stream that counts bytes and may cancel a per-instance source after N writes.</summary>
    private sealed class CountingWriteStream(CancellationTokenSource? cancelsAfter = null, int cancelAfterWrites = int.MaxValue) : Stream
    {
        private readonly CancellationTokenSource? _cancelsAfter = cancelsAfter;
        private readonly int _cancelAfterWrites = cancelAfterWrites;
        private int _writes;

        public long WrittenBytes { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writes++;
            WrittenBytes += buffer.Length;

            if (_cancelsAfter is not null && _writes >= _cancelAfterWrites)
            {
                _cancelsAfter.Cancel();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("storage is offline");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private static string Text(byte[] bytes)
    {
        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] BytesOf(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
