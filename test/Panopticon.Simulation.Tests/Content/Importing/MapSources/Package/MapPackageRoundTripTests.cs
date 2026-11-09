#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.IO;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Round-trip storage fidelity tests for H3 map packages (WI-H3-04), testing synthetic models
/// with all layer types, negative coordinates, subnormals, and installed legacy Generals maps.
/// </summary>
public sealed class MapPackageRoundTripTests
{
    // Cap at the first 12 installed maps to bound execution time per the design contract.
    private const int MaxCorpusMapsToTest = 12;

    private readonly ITestOutputHelper _output;

    public MapPackageRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InstalledGeneralsCorpus_RoundTripsThroughPackageFidelity()
    {
        var coveredCount = 0;
        var isFirstMap = true;
        var evidenceDir = Environment.GetEnvironmentVariable("H3_EVIDENCE_DIR");

        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            InstalledFilesTestData.ReadFiles(".map", _output, entry =>
            {
                if (coveredCount >= MaxCorpusMapsToTest)
                {
                    return;
                }

                using var entryStream = OpenOrNull(entry);

                if (entryStream is null)
                {
                    _output.WriteLine($"{entry.FilePath}: unreadable stream, skipped.");
                    return;
                }

                MapFile mapFile;

                try
                {
                    using var mapStream = MapFile.Decompress(entryStream);
                    mapFile = MapFile.FromStream(mapStream);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"{entry.FilePath}: unparseable ({ex.GetType().Name}: {ex.Message}), skipped.");
                    return;
                }

                if (mapFile.SidesList is { } sidesList && !LegacyMapReader.IsSupportedSidesListVersion(sidesList.Version))
                {
                    _output.WriteLine($"{entry.FilePath}: unsupported SidesList version {sidesList.Version}, skipped.");
                    return;
                }

                var legacyReader = new LegacyMapReader(mapFile, entry.FilePath);
                var originalInterchange = legacyReader.Read();

                var packageDir = Path.Combine(tempRoot, $"pkg-{coveredCount}");
                var writeResult = LegacyMapPackageImporter.Import(originalInterchange, packageDir);

                Assert.True(writeResult.Success, $"Package export failed for {entry.FilePath}: {string.Join("; ", writeResult.Diagnostics)}");

                // Env-gated evidence capture: when H3_EVIDENCE_DIR is set and corpus maps are present,
                // the FIRST corpus map's written package exports manifest.json and files.txt listing.
                if (isFirstMap && !string.IsNullOrWhiteSpace(evidenceDir))
                {
                    CaptureEvidence(packageDir, evidenceDir);
                    isFirstMap = false;
                }

                var reader = new PackageReader(packageDir);
                var reloadedInterchange = reader.Read();

                AssertInterchangeRoundTripEqual(originalInterchange, reloadedInterchange);

                coveredCount++;
                _output.WriteLine($"{entry.FilePath}: successfully round-tripped ({coveredCount}/{MaxCorpusMapsToTest}).");
            });

            _output.WriteLine($"Corpus round-trip covered {coveredCount} installed maps.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SyntheticModel_WithHeightsOnly_RoundTripsBitExact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "synthetic-heights-only");

        try
        {
            var original = BuildHeightsOnlyModel();
            var writeResult = LegacyMapPackageImporter.Import(original, packageDir);

            Assert.True(writeResult.Success);
            Assert.Empty(writeResult.Diagnostics);

            var reader = new PackageReader(packageDir);
            var reloaded = reader.Read();

            AssertInterchangeRoundTripEqual(original, reloaded);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SyntheticModel_WithFullLayers_NegativeZero_Subnormals_AndNegativeCoordinates_RoundTripsBitExact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
        var packageDir = Path.Combine(tempRoot, "synthetic-full-layers");

        try
        {
            var original = BuildFullLayerModel();
            var writeResult = LegacyMapPackageImporter.Import(original, packageDir);

            Assert.True(writeResult.Success);
            Assert.Empty(writeResult.Diagnostics);

            var reader = new PackageReader(packageDir);
            var reloaded = reader.Read();

            AssertInterchangeRoundTripEqual(original, reloaded);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void CaptureEvidence(string packageDir, string evidenceDir)
    {
        Directory.CreateDirectory(evidenceDir);

        var manifestSrc = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var manifestDst = Path.Combine(evidenceDir, MapPackageFormat.ManifestFileName);
        File.Copy(manifestSrc, manifestDst, overwrite: true);

        var lines = new List<string>();

        foreach (var filePath in Directory.GetFiles(packageDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(packageDir, filePath).Replace('\\', '/');
            var bytes = File.ReadAllBytes(filePath);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            lines.Add($"{relPath} {bytes.Length} {sha256}");
        }

        lines.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.Combine(evidenceDir, "files.txt"), lines);
    }

    private static Stream? OpenOrNull(FileSystemEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void AssertInterchangeRoundTripEqual(MapInterchange original, MapInterchange reloaded)
    {
        Assert.Equal(original.SchemaVersion, reloaded.SchemaVersion);

        Assert.Equal(original.Identity.Name, reloaded.Identity.Name);
        Assert.Equal(MapSourceKind.H3Package, reloaded.Identity.SourceKind);
        Assert.Equal(original.Identity.LegacyGlobalVersion, reloaded.Identity.LegacyGlobalVersion);

        Assert.Equal(original.Layout.LatticeWidth, reloaded.Layout.LatticeWidth);
        Assert.Equal(original.Layout.LatticeHeight, reloaded.Layout.LatticeHeight);
        Assert.Equal(original.Layout.ChunksX, reloaded.Layout.ChunksX);
        Assert.Equal(original.Layout.ChunksY, reloaded.Layout.ChunksY);
        Assert.Equal(original.Layout.Border, reloaded.Layout.Border);
        Assert.Equal(original.Layout.NodataPolicy, reloaded.Layout.NodataPolicy);
        Assert.Equal(original.Layout.HeightSectionVersion, reloaded.Layout.HeightSectionVersion);
        Assert.Equal(original.Layout.MapArea, reloaded.Layout.MapArea);
        AssertQuantityEqual(original.Layout.SampleSpacing, reloaded.Layout.SampleSpacing);

        Assert.Equal(original.Layout.TerrainBorders.Count, reloaded.Layout.TerrainBorders.Count);

        for (var i = 0; i < original.Layout.TerrainBorders.Count; i++)
        {
            var expBorder = original.Layout.TerrainBorders[i];
            var actBorder = reloaded.Layout.TerrainBorders[i];
            Assert.Equal(expBorder.Corner1X, actBorder.Corner1X);
            Assert.Equal(expBorder.Corner1Y, actBorder.Corner1Y);
            Assert.Equal(expBorder.X, actBorder.X);
            Assert.Equal(expBorder.Y, actBorder.Y);
        }

        if (original.Layout.GeospatialTransform is null)
        {
            Assert.Null(reloaded.Layout.GeospatialTransform);
        }
        else
        {
            Assert.NotNull(reloaded.Layout.GeospatialTransform);
            Assert.Equal(original.Layout.GeospatialTransform.SourceCrs, reloaded.Layout.GeospatialTransform.SourceCrs);
            Assert.Equal(original.Layout.GeospatialTransform.MetersPerGameUnit, reloaded.Layout.GeospatialTransform.MetersPerGameUnit);
            Assert.Equal(original.Layout.GeospatialTransform.VerticalDatum, reloaded.Layout.GeospatialTransform.VerticalDatum);
            Assert.Equal(original.Layout.GeospatialTransform.VerticalExaggeration, reloaded.Layout.GeospatialTransform.VerticalExaggeration);
            AssertOptionalQuantityEqual(original.Layout.GeospatialTransform.OriginEasting, reloaded.Layout.GeospatialTransform.OriginEasting);
            AssertOptionalQuantityEqual(original.Layout.GeospatialTransform.OriginNorthing, reloaded.Layout.GeospatialTransform.OriginNorthing);
            AssertOptionalQuantityEqual(original.Layout.GeospatialTransform.VerticalOrigin, reloaded.Layout.GeospatialTransform.VerticalOrigin);
        }

        Assert.Equal(original.Layout.Chunks.Count, reloaded.Layout.Chunks.Count);

        for (var i = 0; i < original.Layout.Chunks.Count; i++)
        {
            var origChunk = original.Layout.Chunks[i];
            var reloChunk = reloaded.Layout.Chunks[i];

            Assert.Equal(origChunk.Key, reloChunk.Key);
            Assert.Equal(origChunk.Heights.Samples.Count, reloChunk.Heights.Samples.Count);

            for (var s = 0; s < origChunk.Heights.Samples.Count; s++)
            {
                var origBits = BitConverter.SingleToUInt32Bits(origChunk.Heights.Samples[s]);
                var reloBits = BitConverter.SingleToUInt32Bits(reloChunk.Heights.Samples[s]);
                Assert.True(origBits == reloBits, $"Chunk {origChunk.Key} height sample {s} bit mismatch: 0x{origBits:X8} vs 0x{reloBits:X8}.");
            }

            if (origChunk.Materials is null)
            {
                Assert.Null(reloChunk.Materials);
            }
            else
            {
                Assert.NotNull(reloChunk.Materials);
                Assert.Equal(origChunk.Materials.Indices, reloChunk.Materials.Indices);
            }

            if (origChunk.Masks is null)
            {
                Assert.Null(reloChunk.Masks);
            }
            else
            {
                Assert.NotNull(reloChunk.Masks);
                Assert.Equal(origChunk.Masks.Flags, reloChunk.Masks.Flags);
            }

            if (origChunk.Imagery is null)
            {
                Assert.Null(reloChunk.Imagery);
            }
            else
            {
                Assert.NotNull(reloChunk.Imagery);
                Assert.Equal(origChunk.Imagery.Width, reloChunk.Imagery.Width);
                Assert.Equal(origChunk.Imagery.Height, reloChunk.Imagery.Height);
                Assert.Equal(origChunk.Imagery.Rgba, reloChunk.Imagery.Rgba);
            }
        }

        Assert.Equal(original.Blend.Palette.Count, reloaded.Blend.Palette.Count);

        for (var i = 0; i < original.Blend.Palette.Count; i++)
        {
            var expPal = original.Blend.Palette[i];
            var actPal = reloaded.Blend.Palette[i];
            Assert.Equal(expPal.Index, actPal.Index);
            Assert.Equal(expPal.Name, actPal.Name);
            Assert.Equal(expPal.Texture.Scope, actPal.Texture.Scope);
            Assert.Equal(expPal.Texture.Name, actPal.Texture.Name);
        }

        if (original.Objects is null)
        {
            Assert.Null(reloaded.Objects);
        }
        else
        {
            Assert.NotNull(reloaded.Objects);
            Assert.Equal(original.Objects.Objects.Count, reloaded.Objects.Objects.Count);

            for (var i = 0; i < original.Objects.Objects.Count; i++)
            {
                var expObj = original.Objects.Objects[i];
                var actObj = reloaded.Objects.Objects[i];
                Assert.Equal(expObj.StableId, actObj.StableId);
                AssertQuantityEqual(expObj.X, actObj.X);
                AssertQuantityEqual(expObj.Y, actObj.Y);
                AssertQuantityEqual(expObj.Z, actObj.Z);
                Assert.Equal(expObj.OrientationRadians, actObj.OrientationRadians);
                Assert.Equal(expObj.Type.Scope, actObj.Type.Scope);
                Assert.Equal(expObj.Type.Name, actObj.Type.Name);
                Assert.Equal(expObj.RoadType, actObj.RoadType);
                Assert.Equal(expObj.LegacyProperties.Count, actObj.LegacyProperties.Count);

                foreach (var (k, v) in expObj.LegacyProperties)
                {
                    Assert.True(actObj.LegacyProperties.TryGetValue(k, out var actVal), $"Missing property '{k}' on object '{expObj.StableId}'.");
                    Assert.Equal(v, actVal);
                }
            }
        }

        if (original.Waypoints is null)
        {
            Assert.Null(reloaded.Waypoints);
        }
        else
        {
            Assert.NotNull(reloaded.Waypoints);
            Assert.Equal(original.Waypoints.Waypoints.Count, reloaded.Waypoints.Waypoints.Count);

            for (var i = 0; i < original.Waypoints.Waypoints.Count; i++)
            {
                var expWp = original.Waypoints.Waypoints[i];
                var actWp = reloaded.Waypoints.Waypoints[i];
                Assert.Equal(expWp.StableId, actWp.StableId);
                Assert.Equal(expWp.Name, actWp.Name);
                AssertQuantityEqual(expWp.X, actWp.X);
                AssertQuantityEqual(expWp.Y, actWp.Y);
                AssertQuantityEqual(expWp.Z, actWp.Z);
            }
        }

        Assert.Equal(original.Provenance.SourceSchemaVersion, reloaded.Provenance.SourceSchemaVersion);
        Assert.Equal(original.Provenance.Sources.Count, reloaded.Provenance.Sources.Count);

        for (var i = 0; i < original.Provenance.Sources.Count; i++)
        {
            var expSrc = original.Provenance.Sources[i];
            var actSrc = reloaded.Provenance.Sources[i];
            Assert.Equal(expSrc.Kind, actSrc.Kind);
            Assert.Equal(expSrc.Sha256Digests, actSrc.Sha256Digests);
            AssertOptionalQuantityEqual(expSrc.OriginX, actSrc.OriginX);
            AssertOptionalQuantityEqual(expSrc.OriginY, actSrc.OriginY);
        }

        Assert.Equal(original.Provenance.GeneratedFeatures.Count, reloaded.Provenance.GeneratedFeatures.Count);

        for (var i = 0; i < original.Provenance.GeneratedFeatures.Count; i++)
        {
            var expFeat = original.Provenance.GeneratedFeatures[i];
            var actFeat = reloaded.Provenance.GeneratedFeatures[i];
            Assert.Equal(expFeat.StableId, actFeat.StableId);
            Assert.Equal(expFeat.Kind, actFeat.Kind);
            Assert.Equal(expFeat.Attributes, actFeat.Attributes);
        }

        Assert.Equal(original.RetainedSections.Count, reloaded.RetainedSections.Count);

        for (var i = 0; i < original.RetainedSections.Count; i++)
        {
            var expSec = original.RetainedSections[i];
            var actSec = reloaded.RetainedSections[i];
            Assert.Equal(expSec.SectionName, actSec.SectionName);
            Assert.Equal(expSec.SectionVersion, actSec.SectionVersion);
            Assert.Equal(expSec.Sha256, actSec.Sha256);
            Assert.True(expSec.Bytes.Span.SequenceEqual(actSec.Bytes.Span));
        }
    }

    private static void AssertQuantityEqual(MapQuantity expected, MapQuantity actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.Unit, actual.Unit);
        Assert.Equal(expected.Frame, actual.Frame);
    }

    private static void AssertOptionalQuantityEqual(MapQuantity? expected, MapQuantity? actual)
    {
        if (expected.HasValue)
        {
            Assert.True(actual.HasValue);
            AssertQuantityEqual(expected.Value, actual.Value);
        }
        else
        {
            Assert.False(actual.HasValue);
        }
    }

    private static MapInterchange BuildHeightsOnlyModel()
    {
        const int latticePoints = 129;
        const int coreExtent = 129;
        const int storedExtent = coreExtent + 2;

        var heights = new float[storedExtent * storedExtent];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = 25.0f + (i * 0.125f);
        }

        var chunk = new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(heights),
            null,
            null,
            null);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticePoints,
            latticePoints,
            1,
            1,
            6,
            null!,
            MapNodataPolicy.Fail,
            3,
            latticePoints * latticePoints,
            [new MapTerrainBorder(0, 0, latticePoints - 1, latticePoints - 1)],
            [chunk]);

        var blend = new BlendModel([]);
        var provenance = new Provenance(null, [], []);
        var report = new ImportReport([], [], 0, 0, []);
        var retainedBytes = new byte[] { 10, 20, 30, 40 };
        var retained = new RetainedSection("GlobalVersion", 8, retainedBytes, Sha256Hex(retainedBytes));

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("HeightsOnlyMap", MapSourceKind.LegacyMap, 8),
            layout,
            blend,
            null,
            null,
            provenance,
            report,
            [retained]);
    }

    private static MapInterchange BuildFullLayerModel()
    {
        const int latticePoints = 129;
        const int coreExtent = 129;
        const int storedExtent = coreExtent + 2;

        var heights = new float[storedExtent * storedExtent];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = 100.0f + (i * 0.25f);
        }

        // Exact IEEE-754 test values: negative zero, subnormals, and large finite values.
        heights[0] = -0.0f;
        heights[1] = BitConverter.UInt32BitsToSingle(0x00000001u); // smallest positive subnormal
        heights[2] = BitConverter.UInt32BitsToSingle(0x80000001u); // negative subnormal
        heights[3] = -1234.5678f;
        heights[4] = 98765.43f;

        var materials = new byte[coreExtent * coreExtent];

        for (var i = 0; i < materials.Length; i++)
        {
            materials[i] = (byte)(i % 2);
        }

        var masks = new byte[coreExtent * coreExtent];

        for (var i = 0; i < masks.Length; i++)
        {
            masks[i] = (byte)(i % 3);
        }

        const int imgW = 32;
        const int imgH = 32;
        var rgba = new byte[imgW * imgH * 4];

        for (var i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (byte)(i & 0xFF);
        }

        var imagery = new ImageryLayer(imgW, imgH, rgba);

        var chunk = new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(heights),
            new MaterialLayer(materials),
            new MaskLayer(masks),
            imagery);

        var transform = new GeospatialTransform(
            "EPSG:32632",
            new MapQuantity(500000.5, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
            new MapQuantity(-1200000.25, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
            0.5,
            "EGM96",
            new MapQuantity(-15.0, MapQuantityUnit.Meters, MapQuantityFrame.SourceVertical),
            1.25);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticePoints,
            latticePoints,
            1,
            1,
            0,
            transform,
            MapNodataPolicy.NearestValid,
            3,
            latticePoints * latticePoints,
            [new MapTerrainBorder(0, 0, latticePoints - 1, latticePoints - 1)],
            [chunk]);

        var blend = new BlendModel([
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
            new MaterialPaletteEntry(1, "rock", new AssetReference("terrain", "rock")),
        ]);

        var legacyProps = new Dictionary<string, MapPropertyValue>
        {
            ["boolVal"] = new MapPropertyValue.BooleanValue(true),
            ["intVal"] = new MapPropertyValue.IntegerValue(-42),
            ["realVal"] = new MapPropertyValue.RealValue(-3.14159f),
            ["asciiVal"] = new MapPropertyValue.AsciiStringValue("hello-world"),
            ["unicodeVal"] = new MapPropertyValue.UnicodeStringValue("unicode-\u00e9\u00e8"),
        };

        var objects = new PlacedObjectSet([
            new PlacedObject(
                "obj-negative",
                new MapQuantity(-123.5, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(-456.75, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(-10.0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ),
                -1.570796,
                new AssetReference("custom", "UnitNegative"),
                7u,
                legacyProps),
        ]);

        var waypoints = new WaypointSet([
            new Waypoint(
                "wp-negative",
                "RallyNegative",
                new MapQuantity(-50.25, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(-75.5, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(-2.0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ)),
        ]);

        var sources = new[]
        {
            new SourceRecord(
                "dem",
                [Sha256Hex([1, 2, 3])],
                new MapQuantity(-10.5, MapQuantityUnit.Degrees, MapQuantityFrame.SourceCrs),
                new MapQuantity(-20.5, MapQuantityUnit.Degrees, MapQuantityFrame.SourceCrs)),
        };

        var features = new[]
        {
            new GeneratedFeature("feature-negative", "crater", new Dictionary<string, string> { ["radius"] = "50" }),
        };

        var provenance = new Provenance("2.0", sources, features);
        var report = new ImportReport([], [], 0, 0, []);

        var retained1 = new byte[] { 10, 20, 30, 40, 50 };
        var retained2 = new byte[] { 99, 88, 77 };

        var retainedSections = new[]
        {
            new RetainedSection("GlobalVersion", 8, retained1, Sha256Hex(retained1)),
            new RetainedSection("AssetList", 1, retained2, Sha256Hex(retained2)),
        };

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("FullLayerSyntheticMap", MapSourceKind.Generated, 42),
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            retainedSections);
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
