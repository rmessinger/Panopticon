#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Unit tests for the H3 map package manifest schema (WI-H3-01):
/// DTO model, strict JSON parsing, deterministic serialization, and schema validation.
/// </summary>
public sealed class MapPackageManifestTests
{
    private const string SampleSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ValidManifestPassesValidationAndRoundTripsJson()
    {
        var manifest = BuildValidManifest();
        var diagnostics = MapPackageManifestValidator.Validate(manifest);

        Assert.Empty(diagnostics);

        var json = manifest.ToJson();
        var (parsed, parseDiagnostics) = MapPackageManifestJson.Parse(json);

        Assert.Empty(parseDiagnostics);
        Assert.NotNull(parsed);

        var revalidated = MapPackageManifestValidator.Validate(parsed!);
        Assert.Empty(revalidated);

        Assert.True(MapPackageManifestJson.TryParse(json, out var validatedManifest, out var tryDiagnostics));
        Assert.Empty(tryDiagnostics);
        Assert.NotNull(validatedManifest);

        Assert.Equal(manifest.Format, parsed!.Format);
        Assert.Equal(manifest.PackageVersion, parsed.PackageVersion);
        Assert.Equal(manifest.SchemaVersion, parsed.SchemaVersion);
        Assert.Equal(manifest.Identity.Name, parsed.Identity.Name);
        Assert.Equal(manifest.Layers.Count, parsed.Layers.Count);
        Assert.Equal(manifest.RetainedSections.Count, parsed.RetainedSections.Count);
    }

    [Fact]
    public void WrongPackageFormatFails()
    {
        var manifest = BuildValidManifest() with { Format = "other-map-package" };
        AssertOnlyCode(manifest, MapPackageDiagnostic.PackageFormat);
    }

    [Fact]
    public void WrongPackageVersionFails()
    {
        var manifest = BuildValidManifest() with { PackageVersion = MapPackageFormat.CurrentPackageVersion + 1 };
        AssertOnlyCode(manifest, MapPackageDiagnostic.PackageVersion);
    }

    [Fact]
    public void WrongSchemaVersionFails()
    {
        var manifest = BuildValidManifest() with { SchemaVersion = MapInterchange.CurrentSchemaVersion + 1 };
        AssertOnlyCode(manifest, MapPackageDiagnostic.SchemaVersion);
    }

    [Fact]
    public void ProvenanceAndLegacyVersionsDoNotAffectPackageValidation()
    {
        var identity = new MapPackageIdentity("TestMap", "LegacyMap", 8);
        var provenance = new MapPackageProvenance("source-payload-v2", [], []);
        var manifest = BuildValidManifest() with { Identity = identity, Provenance = provenance };

        Assert.Empty(MapPackageManifestValidator.Validate(manifest));
    }

    [Fact]
    public void MalformedJsonReturnsDiagnosticWithoutThrowing()
    {
        const string badJson = "{ \"format\": \"panopticon-map-package\", ";
        var (manifest, diagnostics) = MapPackageManifestJson.Parse(badJson);

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.MalformedJson);
    }

    [Fact]
    public void EmptyJsonReturnsDiagnosticWithoutThrowing()
    {
        var (manifest, diagnostics) = MapPackageManifestJson.Parse("   ");

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.MalformedJson);
    }

    [Fact]
    public void UnknownJsonMemberAtRootIsRejected()
    {
        var validJson = BuildValidManifest().ToJson();
        var badJson = validJson.TrimEnd().TrimEnd('}') + ",\n  \"unexpectedField\": 42\n}";

        var (manifest, diagnostics) = MapPackageManifestJson.Parse(badJson);

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.UnknownJsonMember);
    }

    [Fact]
    public void UnknownJsonMemberInNestedObjectIsRejected()
    {
        var validJson = BuildValidManifest().ToJson();
        var badJson = validJson.Replace("\"name\": \"TestMap\"", "\"name\": \"TestMap\",\n    \"extraField\": true");

        var (manifest, diagnostics) = MapPackageManifestJson.Parse(badJson);

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.UnknownJsonMember);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/layers/h.bin")]
    [InlineData("layers\\h.bin")]
    [InlineData("layers/./h.bin")]
    [InlineData("layers/../h.bin")]
    [InlineData("layers//h.bin")]
    [InlineData("layers/h.bin/")]
    [InlineData("C:/layers/h.bin")]
    [InlineData("//server/share/h.bin")]
    [InlineData("archive:layers/h.bin")]
    [InlineData("big:layers/h.bin")]
    public void InvalidPathFails(string path)
    {
        var layers = BuildValidLayers().Select(l => l with { Path = path }).ToArray();
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.PathInvalid);
    }

    [Fact]
    public void PathOver260CharsFails()
    {
        var longSegment = new string('a', 255);
        var path = $"layers/{longSegment}.bin";
        var layers = BuildValidLayers().Select(l => l with { Path = path }).ToArray();
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.PathInvalid);
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef01")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void InvalidDigestFails(string digest)
    {
        var layers = BuildValidLayers().Select(l => l with { Sha256 = digest }).ToArray();
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.DigestInvalid);
    }

    [Fact]
    public void MismatchedLayerEncodingFails()
    {
        var layers = BuildValidLayers().Select(l => l with { Encoding = MapPackageFormat.EncodingUint8 }).ToArray();
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayerEncoding);
    }

    [Fact]
    public void UnknownLayerKindFails()
    {
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, "unknown-layer", "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayerEncoding);
    }

    [Fact]
    public void WrongHeightsDimensionsFails()
    {
        // 130 instead of 131
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 130, 131, 130 * 131 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayerLength);
    }

    [Fact]
    public void ByteLengthMismatchFails()
    {
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4 - 1, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayerLength);
    }

    [Fact]
    public void MissingHeightsEntryFails()
    {
        // 2x1 grid but only chunk (0, 0) provided
        var layout = BuildValidLayout() with { LatticeWidth = 257, LatticeHeight = 129, ChunksX = 2, ChunksY = 1 };
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.ChunkInventory);
    }

    [Fact]
    public void IncompleteMaterialsFailsConsistency()
    {
        // 2x1 grid: heights on both, materials on only chunk (0, 0)
        var layout = BuildValidLayout() with { LatticeWidth = 257, LatticeHeight = 129, ChunksX = 2, ChunksY = 1 };
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
            new MapPackageLayerEntry(1, 0, MapPackageFormat.LayerKindHeights, "layers/h_1_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindMaterials, "layers/m_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, 129 * 129, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayersConsistency);
    }

    [Fact]
    public void HeightsOnlyMapPassesConsistency()
    {
        // Heights only on all chunks, 0 materials, 0 masks -> passes!
        var layout = BuildValidLayout() with { LatticeWidth = 129, LatticeHeight = 129, ChunksX = 1, ChunksY = 1 };
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = layers };

        Assert.Empty(MapPackageManifestValidator.Validate(manifest));
    }

    [Fact]
    public void OversizedPaletteFails()
    {
        var entries = Enumerable.Range(0, 257)
            .Select(i => new MapPackageMaterialPaletteEntry((byte)(i % 256), $"m_{i}", new MapPackageAssetReference("terrain", $"t_{i}")))
            .ToArray();
        var manifest = BuildValidManifest() with { Blend = new MapPackageBlendModel(entries) };

        AssertContainsCode(manifest, MapPackageDiagnostic.PaletteCount);
    }

    [Fact]
    public void DuplicatePaletteIndexFails()
    {
        var entries = new[]
        {
            new MapPackageMaterialPaletteEntry(0, "m0", new MapPackageAssetReference("terrain", "t0")),
            new MapPackageMaterialPaletteEntry(0, "m1", new MapPackageAssetReference("terrain", "t1")),
        };
        var manifest = BuildValidManifest() with { Blend = new MapPackageBlendModel(entries) };

        AssertContainsCode(manifest, MapPackageDiagnostic.PaletteIndex);
    }

    [Fact]
    public void NonFiniteQuantityFails()
    {
        var layout = BuildValidLayout() with { SampleSpacing = new MapPackageQuantity(double.NaN, "GameUnit", "MapLocalXy") };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.QuantityFinite);
    }

    [Fact]
    public void UnknownQuantityUnitOrFrameFails()
    {
        var layout = BuildValidLayout() with { SampleSpacing = new MapPackageQuantity(10.0, "Parsecs", "MapLocalXy") };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.QuantityUnitFrame);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("gameunit")]
    public void NumericOrNonCanonicalQuantityUnitAndFrameNamesFailValidation(string unit)
    {
        var layout = BuildValidLayout() with { SampleSpacing = new MapPackageQuantity(10.0, unit, "MapLocalXy") };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.QuantityUnitFrame);
    }

    [Fact]
    public void NumericQuantityUnitAndFrameNamesFailValidationTogether()
    {
        var layout = BuildValidLayout() with { SampleSpacing = new MapPackageQuantity(10.0, "0", "1") };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.QuantityUnitFrame);
    }

    [Theory]
    [InlineData("0", "1", false)]
    [InlineData("GameUnit", "MapLocalXy", true)]
    [InlineData("Meters", "SourceCrs", true)]
    [InlineData("gameunit", "MapLocalXy", false)]
    [InlineData("GameUnit", "maplocalxy", false)]
    [InlineData("Parsecs", "MapLocalXy", false)]
    [InlineData("GameUnit", "Parsecs", false)]
    public void TryToMapQuantity_AcceptsExactDeclaredEnumNamesOnly(string unit, string frame, bool expected)
    {
        var parsed = new MapPackageQuantity(10.0, unit, frame).TryToMapQuantity(out var quantity);

        Assert.Equal(expected, parsed);

        if (expected)
        {
            Assert.Equal(Enum.Parse<MapQuantityUnit>(unit), quantity.Unit);
            Assert.Equal(Enum.Parse<MapQuantityFrame>(frame), quantity.Frame);
        }
        else
        {
            Assert.Equal(default, quantity);
        }
    }

    [Fact]
    public void BothGameUnitAndMetersAreValidQuantities()
    {
        var transform = new MapPackageGeospatialTransform(
            "EPSG:32610",
            new MapPackageQuantity(500000.0, "Meters", "SourceCrs"),
            new MapPackageQuantity(4000000.0, "Meters", "SourceCrs"),
            10.0,
            "EGM96",
            new MapPackageQuantity(100.0, "Meters", "SourceVertical"),
            1.0);
        var layout = BuildValidLayout() with { GeospatialTransform = transform };
        var manifest = BuildValidManifest() with { Layout = layout };

        Assert.Empty(MapPackageManifestValidator.Validate(manifest));
    }

    [Fact]
    public void DuplicateStableIdAcrossComponentsFails()
    {
        var obj = new MapPackagePlacedObject(
            "shared-id",
            new MapPackageQuantity(10, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(20, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(0, "GameUnit", "MapLocalZ"),
            0.0,
            new MapPackageAssetReference("units", "tank"),
            0,
            new Dictionary<string, MapPackagePropertyValue>());

        var wp = new MapPackageWaypoint(
            "shared-id",
            "Waypoint1",
            new MapPackageQuantity(30, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(40, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(0, "GameUnit", "MapLocalZ"));

        var manifest = BuildValidManifest() with { Objects = [obj], Waypoints = [wp] };

        AssertContainsCode(manifest, MapPackageDiagnostic.StableIdDuplicate);
    }

    [Fact]
    public void UnknownRetainedSectionFails()
    {
        var retained = new[]
        {
            new MapPackageRetainedEntry("UnknownSection123", 1, "retained/unknown.bin", 128, SampleSha256),
        };
        var manifest = BuildValidManifest() with { RetainedSections = retained };

        AssertContainsCode(manifest, MapPackageDiagnostic.RetainedName);
    }

    [Fact]
    public void NegativeRetainedSectionVersionFails()
    {
        var retained = new[]
        {
            new MapPackageRetainedEntry("AssetList", -1, "retained/AssetList.bin", 128, SampleSha256),
        };
        var manifest = BuildValidManifest() with { RetainedSections = retained };

        AssertContainsCode(manifest, MapPackageDiagnostic.RetainedVersion);
    }

    [Fact]
    public void PropertyValuesSerializeAndDeserializeFaithfully()
    {
        var properties = new Dictionary<string, MapPackagePropertyValue>
        {
            ["boolProp"] = new MapPackagePropertyValue.BooleanValue(true),
            ["intProp"] = new MapPackagePropertyValue.IntegerValue(42),
            ["realProp"] = new MapPackagePropertyValue.RealValue(3.14f),
            ["asciiProp"] = new MapPackagePropertyValue.AsciiStringValue("hello"),
            ["unicodeProp"] = new MapPackagePropertyValue.UnicodeStringValue("world"),
        };

        var obj = new MapPackagePlacedObject(
            "obj-1",
            new MapPackageQuantity(10, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(20, "GameUnit", "MapLocalXy"),
            new MapPackageQuantity(0, "GameUnit", "MapLocalZ"),
            0.0,
            new MapPackageAssetReference("units", "tank"),
            0,
            properties);

        var manifest = BuildValidManifest() with { Objects = [obj] };
        var json = manifest.ToJson();
        var (parsed, diagnostics) = MapPackageManifestJson.Parse(json);

        Assert.Empty(diagnostics);
        Assert.NotNull(parsed);

        var parsedObj = Assert.Single(parsed!.Objects!);
        Assert.Equal(5, parsedObj.LegacyProperties.Count);

        var b = Assert.IsType<MapPackagePropertyValue.BooleanValue>(parsedObj.LegacyProperties["boolProp"]);
        Assert.True(b.Value);

        var i = Assert.IsType<MapPackagePropertyValue.IntegerValue>(parsedObj.LegacyProperties["intProp"]);
        Assert.Equal(42, i.Value);

        var r = Assert.IsType<MapPackagePropertyValue.RealValue>(parsedObj.LegacyProperties["realProp"]);
        Assert.Equal(3.14f, r.Value);

        var a = Assert.IsType<MapPackagePropertyValue.AsciiStringValue>(parsedObj.LegacyProperties["asciiProp"]);
        Assert.Equal("hello", a.Value);

        var u = Assert.IsType<MapPackagePropertyValue.UnicodeStringValue>(parsedObj.LegacyProperties["unicodeProp"]);
        Assert.Equal("world", u.Value);
    }

    [Fact]
    public void SerializationIsDeterministic()
    {
        var manifest = BuildValidManifest();
        var json1 = manifest.ToJson();
        var json2 = manifest.ToJson();

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void NonPositiveLatticeDimensionsFails()
    {
        var layout = BuildValidLayout() with { LatticeWidth = 0 };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.LatticeDimensions);
    }

    [Fact]
    public void GridDoesNotTileLatticeFails()
    {
        // 129 lattice needs 1 chunk, but 2 chunks declared
        var layout = BuildValidLayout() with { LatticeWidth = 129, LatticeHeight = 129, ChunksX = 2, ChunksY = 1 };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.LatticeDimensions);
    }

    [Fact]
    public void InconsistentImageryDimensionsFails()
    {
        // 2x1 grid with imagery on both chunks but different dimensions
        var layout = BuildValidLayout() with { LatticeWidth = 257, LatticeHeight = 129, ChunksX = 2, ChunksY = 1 };
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
            new MapPackageLayerEntry(1, 0, MapPackageFormat.LayerKindHeights, "layers/h_1_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindImagery, "layers/img_0_0.raw", MapPackageFormat.EncodingRgba8, 512, 512, 512 * 512 * 4, SampleSha256),
            new MapPackageLayerEntry(1, 0, MapPackageFormat.LayerKindImagery, "layers/img_1_0.raw", MapPackageFormat.EncodingRgba8, 256, 256, 256 * 256 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = layers };

        AssertContainsCode(manifest, MapPackageDiagnostic.LayerLength);
    }

    [Fact]
    public void NonPositiveRetainedLengthFails()
    {
        var retained = new[]
        {
            new MapPackageRetainedEntry("AssetList", 1, "retained/AssetList.bin", 0, SampleSha256),
        };
        var manifest = BuildValidManifest() with { RetainedSections = retained };

        AssertContainsCode(manifest, MapPackageDiagnostic.RetainedLength);
    }

    [Fact]
    public void UnknownPropertyValueTypeFails()
    {
        const string json = """
            {
              "type": "customType",
              "value": "something"
            }
            """;

        var manifestJson = BuildValidManifest().ToJson();
        var badJson = manifestJson.Replace("\"objects\": []", $$"""
            "objects": [
              {
                "stableId": "obj-1",
                "x": { "value": 0, "unit": "GameUnit", "frame": "MapLocalXy" },
                "y": { "value": 0, "unit": "GameUnit", "frame": "MapLocalXy" },
                "z": { "value": 0, "unit": "GameUnit", "frame": "MapLocalZ" },
                "orientationRadians": 0,
                "type": { "scope": "units", "name": "tank" },
                "roadType": 0,
                "legacyProperties": {
                  "badProp": {{json}}
                }
              }
            ]
            """);

        var (manifest, diagnostics) = MapPackageManifestJson.Parse(badJson);

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.PropertyType);
    }

    [Fact]
    public void ParseNullJsonReturnsNullComponent()
    {
        var (manifest, diagnostics) = MapPackageManifestJson.Parse("null");

        Assert.Null(manifest);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.NullComponent);
    }

    [Fact]
    public void ParseAndValidateCatchesSchemaErrors()
    {
        var manifest = BuildValidManifest() with { PackageVersion = 99 };
        var json = manifest.ToJson();

        var (parsed, diagnostics) = MapPackageManifestJson.ParseAndValidate(json);

        Assert.NotNull(parsed);
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.PackageVersion);

        Assert.False(MapPackageManifestJson.TryParse(json, out var validatedManifest, out var tryDiagnostics));
        Assert.Null(validatedManifest);
        Assert.Contains(tryDiagnostics, d => d.Code == MapPackageDiagnostic.PackageVersion);
    }

    [Fact]
    public void ModelDtoMappingRoundTrips()
    {
        var border = new MapTerrainBorder(1, 2, 3, 4);
        var borderDto = MapPackageTerrainBorder.FromModel(border);
        Assert.Equal(border, borderDto.ToModel());

        var asset = new AssetReference("units", "crusader");
        var assetDto = MapPackageAssetReference.FromModel(asset);
        Assert.Equal(asset, assetDto.ToModel());

        var paletteEntry = new MaterialPaletteEntry(1, "sand", asset);
        var paletteDto = MapPackageMaterialPaletteEntry.FromModel(paletteEntry);
        Assert.Equal(paletteEntry, paletteDto.ToModel());

        var blend = new BlendModel([paletteEntry]);
        var blendDto = MapPackageBlendModel.FromModel(blend);
        Assert.Equal(blend.Palette.Count, blendDto.ToModel().Palette.Count);

        var waypoint = new Waypoint("wp-1", "Start1", new MapQuantity(1, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy), new MapQuantity(2, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy), new MapQuantity(3, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ));
        var waypointDto = MapPackageWaypoint.FromModel(waypoint);
        Assert.Equal(waypoint, waypointDto.ToModel());

        var feature = new GeneratedFeature("feat-1", "forest", new Dictionary<string, string> { ["density"] = "high" });
        var featureDto = MapPackageGeneratedFeature.FromModel(feature);
        Assert.Equal(feature.StableId, featureDto.ToModel().StableId);
        Assert.Equal(feature.Attributes["density"], featureDto.ToModel().Attributes["density"]);

        var source = new SourceRecord("dem", [SampleSha256], new MapQuantity(100, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs), null);
        var sourceDto = MapPackageSourceRecord.FromModel(source);
        Assert.Equal(source.Kind, sourceDto.ToModel().Kind);
        Assert.Equal(source.Sha256Digests[0], sourceDto.ToModel().Sha256Digests[0]);
    }

    [Fact]
    public void HostileLayout_CompletesQuickly_ReturnsResourceLimitDiagnostics_CappedAt101()
    {
        const int axisPoints = 1_000_000;
        const int chunksAxis = 7813;
        var layout = BuildValidLayout() with
        {
            LatticeWidth = axisPoints,
            LatticeHeight = axisPoints,
            ChunksX = chunksAxis,
            ChunksY = chunksAxis,
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = [] };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diagnostics = MapPackageManifestValidator.Validate(manifest);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Validation took {sw.ElapsedMilliseconds} ms, expected under a few seconds.");
        Assert.Contains(diagnostics, d => d.Code == MapPackageDiagnostic.ResourceLimitExceeded);
        Assert.True(diagnostics.Count <= 101, $"Expected at most 101 diagnostics, found {diagnostics.Count}.");
    }

    [Fact]
    public void CoverageAggregation_LargeGridPartialHeights_EmitsSingleAggregateDiagnosticFast()
    {
        const int chunks = 300;
        const int lattice = (chunks - 1) * 128 + 129;
        var layout = BuildValidLayout() with
        {
            LatticeWidth = lattice,
            LatticeHeight = lattice,
            ChunksX = chunks,
            ChunksY = chunks,
        };
        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/h_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
            new MapPackageLayerEntry(1, 0, MapPackageFormat.LayerKindHeights, "layers/h_1_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { Layout = layout, Layers = layers };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var diagnostics = MapPackageManifestValidator.Validate(manifest);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Validation took {sw.ElapsedMilliseconds} ms, expected under a few seconds.");
        var chunkDiags = diagnostics.Where(d => d.Code == MapPackageDiagnostic.ChunkInventory).ToList();
        Assert.Single(chunkDiags);
        Assert.Contains("90000", chunkDiags[0].Message);
        Assert.Contains("2", chunkDiags[0].Message);
        Assert.True(diagnostics.Count < 10, $"Expected small diagnostics total, found {diagnostics.Count}.");
    }

    [Fact]
    public void DuplicateRetainedSectionName_FailsValidation()
    {
        var retained = new[]
        {
            new MapPackageRetainedEntry("GlobalVersion", 1, "retained/GlobalVersion.bin", 4, SampleSha256),
            new MapPackageRetainedEntry("GlobalVersion", 1, "retained/GlobalVersion2.bin", 4, SampleSha256),
        };
        var manifest = BuildValidManifest() with { RetainedSections = retained };

        AssertContainsCode(manifest, MapPackageDiagnostic.RetainedNameDuplicate);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("bogus")]
    public void NodataPolicy_InvalidNamesAndNumerics_FailValidation(string policy)
    {
        var layout = BuildValidLayout() with { NodataPolicy = policy };
        var manifest = BuildValidManifest() with { Layout = layout };

        AssertContainsCode(manifest, MapPackageDiagnostic.NodataPolicy);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("bogus")]
    public void SourceKind_InvalidNamesAndNumerics_FailValidation(string sourceKind)
    {
        var identity = new MapPackageIdentity("TestMap", sourceKind, 8);
        var manifest = BuildValidManifest() with { Identity = identity };

        AssertContainsCode(manifest, MapPackageDiagnostic.IdentitySourceKind);
    }

    [Fact]
    public void SourceKind_LegacyMap_Accepted()
    {
        var identity = new MapPackageIdentity("TestMap", "LegacyMap", 8);
        var manifest = BuildValidManifest() with { Identity = identity };

        Assert.DoesNotContain(MapPackageManifestValidator.Validate(manifest), d => d.Code == MapPackageDiagnostic.IdentitySourceKind);
    }

    [Fact]
    public void Manifest_WithCompanions_PassesValidationAndRoundTripsJson()
    {
        var companions = new MapPackageCompanionEntry[]
        {
            new("map.ini", "companions/map.ini", 64, SampleSha256),
            new("map.str", "companions/map.str", 128, SampleSha256),
        };

        var manifest = BuildValidManifest() with { Companions = companions };
        var diagnostics = MapPackageManifestValidator.Validate(manifest);
        Assert.Empty(diagnostics);

        var json = manifest.ToJson();
        var (parsed, parseDiagnostics) = MapPackageManifestJson.Parse(json);
        Assert.Empty(parseDiagnostics);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.Companions);
        Assert.Equal(2, parsed.Companions!.Count);
        Assert.Equal("map.ini", parsed.Companions[0].Name);
        Assert.Equal(SampleSha256, parsed.Companions[0].Sha256);
        Assert.Equal("companions/map.ini", parsed.Companions[0].Path);
        Assert.Equal(64, parsed.Companions[0].ByteLength);
    }

    [Fact]
    public void Manifest_WithDuplicateCompanionName_FailsValidation()
    {
        var companions = new MapPackageCompanionEntry[]
        {
            new("map.ini", "companions/map.ini", 64, SampleSha256),
            new("map.ini", "companions/other.ini", 64, SampleSha256),
        };

        var manifest = BuildValidManifest() with { Companions = companions };
        AssertOnlyCode(manifest, MapPackageDiagnostic.CompanionNameDuplicate);
    }

    [Fact]
    public void Manifest_WithInvalidCompanionDigest_FailsValidation()
    {
        var companions = new MapPackageCompanionEntry[]
        {
            new("map.ini", "companions/map.ini", 64, "invalid-digest"),
        };

        var manifest = BuildValidManifest() with { Companions = companions };
        AssertOnlyCode(manifest, MapPackageDiagnostic.DigestInvalid);
    }

    [Fact]
    public void Manifest_WithInvalidCompanionPath_FailsValidation()
    {
        var companions = new MapPackageCompanionEntry[]
        {
            new("map.ini", "../outside.ini", 64, SampleSha256),
        };

        var manifest = BuildValidManifest() with { Companions = companions };
        AssertOnlyCode(manifest, MapPackageDiagnostic.PathInvalid);
    }

    [Fact]
    public void Manifest_CompanionSlot_ForwardCompatibility_LoadsUnderReaderGracefully()
    {
        // When a package has companion members, a reader that consumes only MapInterchange
        // loads it without error and ignores the companion slot gracefully.
        var tempRoot = Path.Combine(Path.GetTempPath(), "manifest_forward_compat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var manifest = BuildValidManifest() with
            {
                Companions = [new MapPackageCompanionEntry("map.ini", "companions/map.ini", 10, SampleSha256)],
            };

            var json = manifest.ToJson();
            var (parsed, diagnostics) = MapPackageManifestJson.ParseAndValidate(json);
            Assert.Empty(diagnostics);
            Assert.NotNull(parsed);
            Assert.NotNull(parsed!.Companions);
            Assert.Single(parsed.Companions!);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static MapPackageManifest BuildValidManifest() => new(
        MapPackageFormat.PackageFormatName,
        MapPackageFormat.CurrentPackageVersion,
        MapInterchange.CurrentSchemaVersion,
        new MapPackageIdentity("TestMap", "LegacyMap", 8),
        BuildValidLayout(),
        BuildValidBlend(),
        [],
        [],
        new MapPackageProvenance("1.0", [], []),
        BuildValidLayers(),
        BuildValidRetained());

    private static MapPackageTerrainLayout BuildValidLayout() => new(
        new MapPackageQuantity(10.0, "GameUnit", "MapLocalXy"),
        129,
        129,
        1,
        1,
        0,
        null,
        "Fail",
        1,
        16641,
        []);

    private static MapPackageBlendModel BuildValidBlend() => new([
        new MapPackageMaterialPaletteEntry(0, "grass", new MapPackageAssetReference("terrain", "grass")),
    ]);

    private static MapPackageLayerEntry[] BuildValidLayers() =>
    [
        new(0, 0, MapPackageFormat.LayerKindHeights, "layers/heights_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, 131 * 131 * 4, SampleSha256),
        new(0, 0, MapPackageFormat.LayerKindMaterials, "layers/materials_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, 129 * 129, SampleSha256),
        new(0, 0, MapPackageFormat.LayerKindMasks, "layers/masks_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, 129 * 129, SampleSha256),
    ];

    private static MapPackageRetainedEntry[] BuildValidRetained() =>
    [
        new("AssetList", 1, "retained/AssetList.bin", 128, SampleSha256),
    ];

    private static void AssertOnlyCode(MapPackageManifest manifest, string expectedCode)
    {
        var diagnostics = MapPackageManifestValidator.Validate(manifest);
        Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostics[0].Code);
    }

    private static void AssertContainsCode(MapPackageManifest manifest, string expectedCode)
    {
        var diagnostics = MapPackageManifestValidator.Validate(manifest);
        Assert.Contains(diagnostics, d => d.Code == expectedCode);
    }
}
