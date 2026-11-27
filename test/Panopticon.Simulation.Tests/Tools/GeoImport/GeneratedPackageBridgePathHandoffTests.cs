#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;
using Panopticon.Graphics;
using Panopticon.Mathematics;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;
using MapInterchange = Panopticon.Content.Importing.Maps.MapInterchange;
using MapPropertyValue = Panopticon.Content.Importing.Maps.MapPropertyValue;
using Rectangle = Panopticon.Mathematics.Rectangle;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// WP1029 path-plan handoff. The portable fact converts the WP1022 gorge fixture with
/// bridge rules on the supported template (default) and without rules, and reloads both
/// packages through PackageReader: the supported template's landing band is skipped, so
/// every lattice height matches the no-rules package, and both placed objects carry the
/// generated provenance plus the path-plan handoff integer as their only legacy
/// properties.
/// </summary>
public sealed class GeneratedPackageBridgePathHandoffTests
{
    private static readonly (double X, double Y)[] BridgeEndpoints = [(550d, 300d), (550d, 1000d)];

    [Fact]
    public void SupportedTemplatePackage_ReloadCarriesOriginalLandingHeights_AndPathHandoff()
    {
        var scratchRoot = NewScratchRoot();

        try
        {
            var payloadDir = StagePayload(Path.Combine(scratchRoot, "payload"));
            var packageOn = Path.Combine(scratchRoot, "package-on");
            var packageOff = Path.Combine(scratchRoot, "package-off");

            Convert(payloadDir, packageOn, withRules: true);
            Convert(payloadDir, packageOff, withRules: false);

            var on = new PackageReader(packageOn).Read();
            var off = new PackageReader(packageOff).Read();

            // The supported template's approach corridor is planned at runtime, so the
            // lattice keeps its original heights: every stored sample (interior and ghost
            // border) matches the no-rules package.
            Assert.Equal(off.Layout.Chunks.Count, on.Layout.Chunks.Count);

            foreach (var (chunk, offChunk) in on.Layout.Chunks.Zip(off.Layout.Chunks))
            {
                Assert.Equal(offChunk.Heights.Samples, chunk.Heights.Samples);
            }

            var latticeWidth = on.Layout.LatticeWidth;

            // Endpoint cells keep the deck-anchoring heights; the band cells north and
            // south of the endpoints keep their quantized payload heights (no grading).
            Assert.Equal(1083.75f, ReloadedLatticeHeight(on, latticeWidth, 55, 30));
            Assert.Equal(1118.75f, ReloadedLatticeHeight(on, latticeWidth, 55, 100));
            Assert.Equal(ResidentGridHeight(PayloadHeight(55, 29)), ReloadedLatticeHeight(on, latticeWidth, 55, 29));
            Assert.Equal(ResidentGridHeight(PayloadHeight(55, 28)), ReloadedLatticeHeight(on, latticeWidth, 55, 28));
            Assert.Equal(ResidentGridHeight(PayloadHeight(55, 101)), ReloadedLatticeHeight(on, latticeWidth, 55, 101));

            // Both placed objects carry the generated provenance and the path-plan handoff
            // (integer 1) and no other legacy property.
            Assert.NotNull(on.Objects);
            var objects = on.Objects!.Objects;
            Assert.Equal(2, objects.Count);
            Assert.Equal((uint)RoadType.BridgeStart, objects[0].RoadType);
            Assert.Equal((uint)RoadType.BridgeEnd, objects[1].RoadType);

            foreach (var placed in objects)
            {
                Assert.Equal(
                    new HashSet<string> { Panopticon.Terrain.Bridge.GeneratedProvenanceKey, Panopticon.Terrain.GeneratedBridgePathHandoff.Key },
                    new HashSet<string>(placed.LegacyProperties.Keys));
                Assert.Equal(
                    Panopticon.Terrain.Bridge.GeneratedProvenanceValue,
                    Assert.IsType<MapPropertyValue.AsciiStringValue>(placed.LegacyProperties[Panopticon.Terrain.Bridge.GeneratedProvenanceKey]).Value);
                Assert.Equal(
                    1,
                    Assert.IsType<MapPropertyValue.IntegerValue>(placed.LegacyProperties[Panopticon.Terrain.GeneratedBridgePathHandoff.Key]).Value);
            }

            // Without rules no bridge road type is placed at all.
            Assert.DoesNotContain(
                off.Objects?.Objects ?? [],
                placed => placed.RoadType == (uint)RoadType.BridgeStart || placed.RoadType == (uint)RoadType.BridgeEnd);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static float ResidentGridHeight(double value)
    {
        const double step = 0.0390625d;
        return (float)(Math.Round(value / step, MidpointRounding.ToEven) * step);
    }

    private static float ReloadedLatticeHeight(MapInterchange model, int latticeWidth, int latticeX, int latticeY)
    {
        var cx = latticeX / ChunkLayout.IntervalsPerChunk;
        var cy = latticeY / ChunkLayout.IntervalsPerChunk;
        var chunk = Assert.Single(model.Layout.Chunks, candidate => candidate.Key.Cx == cx && candidate.Key.Cy == cy);
        var originX = cx * ChunkLayout.IntervalsPerChunk;
        var originY = cy * ChunkLayout.IntervalsPerChunk;
        var coreWidth = Math.Min(ChunkLayout.PointsPerChunkAxis, model.Layout.LatticeWidth - originX);

        return chunk.Heights.Samples[((latticeY - originY + 1) * (coreWidth + 2)) + (latticeX - originX + 1)];
    }

    private static string StagePayload(string payloadDir)
    {
        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(payloadDir, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(["grass", "dirt"]);
        var descriptors = new List<PayloadChunkDescriptor>();

        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

                for (var sy = 0; sy < ChunkLayout.StoredPointsPerChunkAxis; sy++)
                {
                    for (var sx = 0; sx < ChunkLayout.StoredPointsPerChunkAxis; sx++)
                    {
                        heights[(sy * ChunkLayout.StoredPointsPerChunkAxis) + sx] =
                            PayloadHeight((cx * ChunkLayout.IntervalsPerChunk) + sx - 1, (cy * ChunkLayout.IntervalsPerChunk) + sy - 1);
                    }
                }

                var terrain = writer.WriteTerrain(new TerrainChunk(cx, cy, heights));
                var materials = writer.WriteMaterial(new MaterialChunk(cx, cy, palette, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                var masks = writer.WriteMask(new TerrainMaskChunk(cx, cy, new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis]));
                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(cx, cy, terrain!, materials!, masks!, null));
            }
        }

        // One straight two-point bridge line crossing the gorge, in payload game units.
        var features = new List<GeneratedFeature>
        {
            new(
                GeneratedFeatureKind.Annotation,
                new GeoLineString([new GeoPosition(new Vector2d(BridgeEndpoints[0].X, BridgeEndpoints[0].Y)), new GeoPosition(new Vector2d(BridgeEndpoints[1].X, BridgeEndpoints[1].Y))]),
                "vectors",
                "bridge-a",
                "vectors/bridge-a#0",
                -1),
        };

        var transform = new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 0d,
            verticalExaggeration: 1d);
        var grid = new PayloadGridDefinition(
            2,
            2,
            transform,
            new GeoPolygon(new GeoRing(
            [
                new GeoPosition(new Vector2d(500000d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6000000d)),
                new GeoPosition(new Vector2d(512560d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6012560d)),
                new GeoPosition(new Vector2d(500000d, 6000000d)),
            ])),
            null,
            new SlopeThresholds(45d, 15d));
        var metadata = new GeneratedMapPayloadMetadata(grid, palette, features, new string('7', 64), [], descriptors);
        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);

        var report = new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(SamplingSummary(), 0, 0, 0),
            new PayloadMaskGenerationSummary(45d, 15d, 1d, 4, 0, 0, 0, 0, 0, 0, 0, 0),
            new PayloadFeatureGenerationSummary(
                features.Count,
                [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, features.Count(feature => feature.Kind == kind)))]),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex);
        Assert.True(writer.WriteReport(report));
        Assert.True(writer.TryCommit());

        return payloadDir;
    }

    private static float PayloadHeight(long latticeX, long latticeY)
    {
        // Plane terrain with a north-south gorge strip the bridge crosses.
        var gorge = Math.Abs(latticeY - 64) < 4 ? -80f : 0f;

        return 1000f + (1.25f * latticeX) + (0.5f * latticeY) + gorge;
    }

    private static PayloadSamplingSummary SamplingSummary()
    {
        return new PayloadSamplingSummary(
            0,
            0,
            0,
            0,
            [.. Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, 0))],
            new PayloadObservedRanges(
                gsdRatioX: new PayloadObservedRange(0.98d, 1.02d),
                gsdRatioY: new PayloadObservedRange(0.97d, 1.03d)));
    }

    private static void Convert(string payloadDir, string packageDir, bool withRules, string bridgeTemplate = "Concrete")
    {
        var diagnostics = new GeoDiagnosticBag();
        using var reader = GeneratedMapPayloadReader.Open(payloadDir, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        var textureMap = PreviewTextureMap.FromJson(
            """{"grass":"GrassType1","dirt":"DirtMediumType1"}""",
            new FixedTerrainTextureCatalog(["GrassType1", "DirtMediumType1"]),
            new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        var result = GeneratedMapPackageConverter.Convert(
            reader!,
            "r3-g8-bridge-traversal",
            packageDir,
            textureMap,
            bridgeRules: withRules ? [new BridgeRule("vectors", bridgeTemplate)] : null);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
    }

    private static string NewScratchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "r3-g8-bridge-path-handoff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FixedTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }
}
