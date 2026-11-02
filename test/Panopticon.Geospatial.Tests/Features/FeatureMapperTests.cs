using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Xunit;

namespace Panopticon.Geospatial.Tests.Features;

public class FeatureMapperTests
{
    [Fact]
    public void PointMappedToWaypointGetsTheTerrainHeightAtThatPosition()
    {
        var sampler = FeatureMappingTestSupport.FullSampler(FeatureMappingTestSupport.CreateGrid());
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };
        var part = FeatureMappingTestSupport.PointPart(
            643.75d,
            1287.5d,
            altitudeMeters: 123.5d,
            attributes: new Dictionary<string, string> { ["name"] = "hill" });

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], rules, sampler, diagnostics);

        Assert.False(diagnostics.HasErrors);
        var waypoint = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.Waypoint, waypoint.Kind);
        Assert.Equal(0, waypoint.RuleIndex);
        Assert.Equal("vectors", waypoint.SourceId);
        Assert.Equal("p1", waypoint.FeatureId);
        Assert.Equal("vectors/p1#0", waypoint.PartId);
        Assert.Equal("hill", Assert.Contains("name", waypoint.Attributes));

        // Z is the sampled terrain height in game units; the 123.5 m source altitude is
        // provenance on the retained geometry, never the waypoint Z.
        Assert.NotNull(waypoint.Position);
        Assert.Equal(643.75d, waypoint.Position!.Value.X);
        Assert.Equal(1287.5d, waypoint.Position.Value.Y);
        Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(64.375d, 128.75d), waypoint.Position.Value.Z);
        var retained = Assert.IsType<GeoPointGeometry>(waypoint.Geometry);
        Assert.Equal(123.5d, retained.Position.AltitudeMeters);
    }

    [Fact]
    public void WaypointHeightsAreAnalyticAcrossSeamsAndFarCorners()
    {
        var grid = FeatureMappingTestSupport.CreateGrid();
        var sampler = FeatureMappingTestSupport.FullSampler(grid);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };
        var positions = new[]
        {
            (1280d, 1280d),
            (0d, 0d),
            (2560d, 0d),
            (0d, 2560d),
            (2560d, 2560d),
            (2560d, 1287.5d),
        };
        var parts = new List<LocalFeature>();
        foreach (var (x, y) in positions)
        {
            parts.Add(FeatureMappingTestSupport.PointPart(x, y));
        }

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(parts, rules, sampler, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(positions.Length, results.Count);
        for (var i = 0; i < positions.Length; i++)
        {
            var (x, y) = positions[i];
            Assert.Equal(FeatureMappingTestSupport.SurfaceHeight(x / 10d, y / 10d), results[i].Position!.Value.Z);
        }
    }

    [Fact]
    public void ObjectRuleWithoutObjectDefinitionFailsWithGenerateError()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Object, ManifestFeatureGeometry.Point, Orientation: ManifestFeatureOrientation.None),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        Assert.Empty(results);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingRuleInvalid, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void ObjectRuleWithMissingOrNonFiniteOrientationFailsWithGenerateError()
    {
        var part = FeatureMappingTestSupport.PointPart(100d, 100d);
        var missingOrientation = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Object, ManifestFeatureGeometry.Point, ObjectDefinition: "BridgeStone"),
        };
        var nonFiniteFixed = new List<FeatureMappingRule>
        {
            new(
                "vectors",
                ManifestFeatureTarget.Object,
                ManifestFeatureGeometry.Point,
                ObjectDefinition: "BridgeStone",
                Orientation: ManifestFeatureOrientation.Fixed,
                OrientationDegrees: double.NaN),
        };

        foreach (var rules in new[] { missingOrientation, nonFiniteFixed })
        {
            var diagnostics = new GeoDiagnosticBag();
            var results = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

            Assert.Empty(results);
            var diagnostic = Assert.Single(diagnostics.Items);
            Assert.Equal(FeatureMappingDiagnosticCodes.MappingRuleInvalid, diagnostic.Code);
            Assert.Contains("orientation", diagnostic.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AlongFeatureOrientationOnAPointIsATypedGenerateError()
    {
        var rules = new List<FeatureMappingRule>
        {
            new(
                "vectors",
                ManifestFeatureTarget.Object,
                ManifestFeatureGeometry.Point,
                ObjectDefinition: "BridgeStone",
                Orientation: ManifestFeatureOrientation.AlongFeature),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        Assert.Empty(results);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingOrientationUndefined, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData(ManifestFeatureOrientation.None, null, 0d)]
    [InlineData(ManifestFeatureOrientation.Fixed, 90d, 90d)]
    [InlineData(ManifestFeatureOrientation.Fixed, 450d, 90d)]
    [InlineData(ManifestFeatureOrientation.Fixed, -90d, 270d)]
    public void ObjectCandidateCarriesDefinitionAndNormalizedOrientation(
        ManifestFeatureOrientation orientation,
        double? orientationDegrees,
        double expectedDegrees)
    {
        var rules = new List<FeatureMappingRule>
        {
            new(
                "vectors",
                ManifestFeatureTarget.Object,
                ManifestFeatureGeometry.Point,
                ObjectDefinition: "BridgeStone",
                Orientation: orientation,
                OrientationDegrees: orientationDegrees),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        Assert.False(diagnostics.HasErrors);
        var candidate = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.ObjectCandidate, candidate.Kind);
        Assert.NotNull(candidate.ObjectPlacement);
        Assert.Equal("BridgeStone", candidate.ObjectPlacement!.ObjectDefinition);
        Assert.Equal(expectedDegrees, candidate.ObjectPlacement.OrientationDegrees);
    }

    public static TheoryData<string, FeatureMappingRule, bool> PredicateCases => new()
    {
        { "wrong-source-case", new FeatureMappingRule("Vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point), false },
        { "layer-exact-path", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, LayerName: "roads/primary"), true },
        { "layer-wrong-case", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, LayerName: "Roads/Primary"), false },
        { "layer-prefix-only", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, LayerName: "roads"), false },
        { "layer-parent-path", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, LayerName: "roads/primary/extra"), false },
        { "attribute-value-exact", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, "ref", "A1"), true },
        { "attribute-value-case", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, "ref", "a1"), false },
        { "attribute-presence", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, "ref"), true },
        { "attribute-absent", new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, "missing"), false },
        { "geometry-predicate-mismatch", new FeatureMappingRule("vectors", ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Line), false },
    };

    [Theory]
    [MemberData(nameof(PredicateCases))]
    public void PredicatesAreExactOnSourceLayerAttributeValueAndCase(string _, FeatureMappingRule rule, bool expectedMatch)
    {
        var part = FeatureMappingTestSupport.PointPart(
            100d,
            100d,
            attributes: new Dictionary<string, string>
            {
                ["folder"] = "roads/primary",
                ["ref"] = "A1",
            });

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], [rule], FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        var generated = Assert.Single(results);
        Assert.Equal(expectedMatch ? 0 : -1, generated.RuleIndex);
        Assert.Equal(expectedMatch ? rule.Target switch
        {
            ManifestFeatureTarget.Waypoint => GeneratedFeatureKind.Waypoint,
            ManifestFeatureTarget.Annotation => GeneratedFeatureKind.Annotation,
            _ => GeneratedFeatureKind.Annotation,
        } : GeneratedFeatureKind.Annotation, generated.Kind);
    }

    [Fact]
    public void TargetCompatibilityConstrainsRulesWithoutAGeometryPredicate()
    {
        // A waypoint rule without a geometry predicate must not consume a line feature.
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint),
        };
        var line = FeatureMappingTestSupport.LinePart([(10d, 10d), (200d, 10d)]);

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([line], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        var annotation = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.Annotation, annotation.Kind);
        Assert.Equal(-1, annotation.RuleIndex);
        Assert.Null(annotation.Position);
    }

    [Fact]
    public void AuthoringTargetsUseTheFirstMatchingRule()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("other", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
            new("vectors", ManifestFeatureTarget.Landmark, ManifestFeatureGeometry.Point, "ref", "A1"),
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, "ref", "A1"),
        };
        var part = FeatureMappingTestSupport.PointPart(
            100d,
            100d,
            attributes: new Dictionary<string, string> { ["ref"] = "A1" });

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        var landmark = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.Landmark, landmark.Kind);
        Assert.Equal(1, landmark.RuleIndex);
    }

    [Fact]
    public void RegionsRetainEveryMatchingRuleWithItsManifestIndex()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon, "kind", "river"),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, "kind", "river", Passable: false),
            new("vectors", ManifestFeatureTarget.Buildability, ManifestFeatureGeometry.Polygon, "kind", "river", Buildable: true),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, "kind", "river", Passable: true),
            new("vectors", ManifestFeatureTarget.Material, ManifestFeatureGeometry.Polygon, "kind", "river", Material: "water"),
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon, "kind", "other"),
        };
        var part = FeatureMappingTestSupport.PolygonPart(
            100d,
            100d,
            900d,
            900d,
            attributes: new Dictionary<string, string> { ["kind"] = "river" });

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(4, results.Count);

        // Stable order: manifest rule order within the feature, no annotation appended.
        Assert.Equal(GeneratedFeatureKind.Water, results[0].Kind);
        Assert.Equal(0, results[0].RuleIndex);
        Assert.Null(results[0].Passable);
        Assert.Null(results[0].Buildable);

        Assert.Equal(GeneratedFeatureKind.Passability, results[1].Kind);
        Assert.Equal(1, results[1].RuleIndex);
        Assert.False(results[1].Passable);

        Assert.Equal(GeneratedFeatureKind.Buildability, results[2].Kind);
        Assert.Equal(2, results[2].RuleIndex);
        Assert.True(results[2].Buildable);

        Assert.Equal(GeneratedFeatureKind.Passability, results[3].Kind);
        Assert.Equal(3, results[3].RuleIndex);
        Assert.True(results[3].Passable);

        Assert.All(results, static region => Assert.Null(region.Position));
    }

    [Fact]
    public void UnmatchedFeaturesRemainAnnotationsAndGenerateNoGameplayKinds()
    {
        var point = FeatureMappingTestSupport.PointPart(100d, 100d);
        var line = FeatureMappingTestSupport.LinePart([(10d, 10d), (200d, 10d)], featureId: "l1");

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([point, line], [], FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, results.Count);
        Assert.All(results, static generated => Assert.Equal(GeneratedFeatureKind.Annotation, generated.Kind));
        Assert.All(results, static generated => Assert.Equal(-1, generated.RuleIndex));

        // An unmatched point still samples the terrain Z; a line carries no position.
        Assert.Equal(7d, results[0].Position!.Value.Z);
        Assert.Null(results[1].Position);
    }

    [Fact]
    public void MaterialMatchesAreIndependentAndDoNotSuppressTheAnnotation()
    {
        var materialOnly = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, ManifestFeatureGeometry.Polygon, "kind", "forest", Material: "forest"),
        };
        var materialAndWater = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Material, ManifestFeatureGeometry.Polygon, "kind", "forest", Material: "forest"),
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon, "kind", "forest"),
        };
        var part = FeatureMappingTestSupport.PolygonPart(
            100d,
            100d,
            900d,
            900d,
            attributes: new Dictionary<string, string> { ["kind"] = "forest" });

        var materialDiagnostics = new GeoDiagnosticBag();
        var materialResults = FeatureMapper.Map([part], materialOnly, FeatureMappingTestSupport.ConstantSampler(7d), materialDiagnostics);

        Assert.False(materialDiagnostics.HasErrors);
        var annotation = Assert.Single(materialResults);
        Assert.Equal(GeneratedFeatureKind.Annotation, annotation.Kind);
        Assert.Equal(-1, annotation.RuleIndex);

        var bothDiagnostics = new GeoDiagnosticBag();
        var bothResults = FeatureMapper.Map([part], materialAndWater, FeatureMappingTestSupport.ConstantSampler(7d), bothDiagnostics);

        Assert.False(bothDiagnostics.HasErrors);
        var water = Assert.Single(bothResults);
        Assert.Equal(GeneratedFeatureKind.Water, water.Kind);
        Assert.Equal(1, water.RuleIndex);
    }

    [Fact]
    public void RoadLineMappingRetainsGeometryWithoutAPosition()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Road, ManifestFeatureGeometry.Line),
        };
        var part = FeatureMappingTestSupport.LinePart([(10d, 10d), (200d, 10d), (200d, 300d)], featureId: "road9");

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        var road = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.Road, road.Kind);
        Assert.Equal(part.Geometry, road.Geometry);
        Assert.Null(road.Position);
        Assert.Null(road.ObjectPlacement);
    }

    [Fact]
    public void OutputIsAReadOnlyViewOverCopiedAttributes()
    {
        var attributes = new Dictionary<string, string> { ["name"] = "hill" };
        var part = FeatureMappingTestSupport.PointPart(100d, 100d, attributes: attributes);

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [part],
            new FeatureMappingRule[] { new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point) },
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        var view = Assert.IsType<ReadOnlyCollection<GeneratedFeature>>(results);
        attributes["name"] = "mutated";
        attributes.Add("extra", "value");

        var waypoint = Assert.Single(view);
        Assert.Equal("hill", Assert.Contains("name", waypoint.Attributes));
        Assert.False(waypoint.Attributes.ContainsKey("extra"));
        Assert.Throws<NotSupportedException>(() => ((ICollection<GeneratedFeature>)view).Add(null!));
    }

    [Fact]
    public void GeneratedFeatureRejectsInconsistentParameters()
    {
        var geometry = new GeoPointGeometry(new GeoPosition(new Vector2d(1d, 2d), 3d));
        var attributes = new Dictionary<string, string> { ["a"] = "b" };

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, attributes));

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.Road,
            new GeoLineString([new GeoPosition(new Vector2d(0d, 0d)), new GeoPosition(new Vector2d(1d, 1d))]),
            "s", "f", "s/f#0", 0, attributes, position: new GamePosition(1d, 2d, 3d)));

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.ObjectCandidate, geometry, "s", "f", "s/f#0", 0, attributes, position: new GamePosition(1d, 2d, 3d)));

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, attributes, new GamePosition(1d, 2d, 3d), objectPlacement: new GeneratedObjectPlacement("X", 0d)));

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, attributes, new GamePosition(1d, 2d, 3d), passable: true));

        Assert.Throws<ArgumentException>(() => new GeneratedFeature(
            GeneratedFeatureKind.Water, new GeoPolygon(new GeoRing([new GeoPosition(new Vector2d(0d, 0d))])), "s", "f", "s/f#0", 0, attributes, buildable: true));

        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneratedFeature(
            (GeneratedFeatureKind)99, geometry, "s", "f", "s/f#0", 0, attributes, new GamePosition(1d, 2d, 3d)));

        // The attributes dictionary is copied, not retained.
        var copy = new GeneratedFeature(
            GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, attributes, new GamePosition(1d, 2d, 3d));
        attributes["a"] = "mutated";
        Assert.Equal("b", Assert.Contains("a", copy.Attributes));
    }

    [Fact]
    public void InputFeatureLimitStopsMappingWithTypedError()
    {
        var limits = new GeospatialImportLimits(MaxFeatureCount: 1);
        var parts = new[]
        {
            FeatureMappingTestSupport.PointPart(100d, 100d, featureId: "a"),
            FeatureMappingTestSupport.PointPart(200d, 200d, featureId: "b"),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            parts,
            new FeatureMappingRule[] { new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point) },
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics,
            limits);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Single(results);
    }

    [Fact]
    public void OutputExpansionLimitStopsMappingWithTypedError()
    {
        var limits = new GeospatialImportLimits(MaxFeatureCount: 2);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PolygonPart(100d, 100d, 900d, 900d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics,
            limits);

        // The annotation and the first region fill the output budget; the second region
        // is rejected at emission time.
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("maxFeatureCount", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void OutputCoordinateBudgetChargesEveryEmittedCopy()
    {
        // The polygon has 5 ring positions; each emitted region re-carries them, so the
        // annotation plus the first region reach the 10-coordinate output budget and the
        // second emission is rejected before any allocation.
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 10);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Annotation, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PolygonPart(100d, 100d, 900d, 900d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics,
            limits);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("maxCoordinateCount", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void RegionMatchCollectionBoundedByFeatureBudgetStopsWithTypedError()
    {
        var limits = new GeospatialImportLimits(MaxFeatureCount: 2);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
            new("vectors", ManifestFeatureTarget.Buildability, ManifestFeatureGeometry.Polygon, Buildable: false),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PolygonPart(100d, 100d, 900d, 900d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics,
            limits);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("region matches", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(results);
    }

    [Fact]
    public void RegionMatchCollectionBoundedByCoordinateBudgetStopsWithTypedError()
    {
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 5);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PolygonPart(100d, 100d, 900d, 900d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics,
            limits);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("region matches", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(results);
    }

    [Fact]
    public void RuleComparisonBudgetStopsMatchingWithTypedError()
    {
        var limits = new GeospatialImportLimits { MaxFeatureRuleComparisons = 5 };
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };
        var parts = new[]
        {
            FeatureMappingTestSupport.PointPart(100d, 100d, featureId: "a"),
            FeatureMappingTestSupport.PointPart(200d, 200d, featureId: "b"),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(parts, rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics, limits);

        // The first feature consumes three comparisons, the second exhausts the budget
        // mid-loop; the whole-map budget is charged per rule-feature pair.
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("maxFeatureRuleComparisons", diagnostic.Message, StringComparison.Ordinal);
        var waypoint = Assert.Single(results);
        Assert.Equal("a", waypoint.FeatureId);
    }

    [Fact]
    public void DeepUnsupportedMultiGeometryIsRejectedWithoutRecursion()
    {
        GeoGeometry deep = new GeoPointGeometry(new GeoPosition(new Vector2d(1d, 1d)));
        for (var i = 0; i < 10_000; i++)
        {
            deep = new GeoMultiGeometry([deep]);
        }

        var part = new LocalFeature(
            deep,
            FeatureMappingTestSupport.SourceFeature("vectors", "deep", null),
            "vectors/deep#0");
        var point = FeatureMappingTestSupport.PointPart(100d, 100d, featureId: "near");
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part, point], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingUnsupportedGeometry, diagnostic.Code);
        var waypoint = Assert.Single(results);
        Assert.Equal("near", waypoint.FeatureId);
    }

    [Fact]
    public void PolygonHoleCoordinateBudgetStopsWithTypedError()
    {
        var square = new GeoRing(
        [
            new GeoPosition(new Vector2d(10d, 10d)),
            new GeoPosition(new Vector2d(900d, 10d)),
            new GeoPosition(new Vector2d(900d, 900d)),
            new GeoPosition(new Vector2d(10d, 900d)),
            new GeoPosition(new Vector2d(10d, 10d)),
        ]);
        var hole = new GeoRing(
        [
            new GeoPosition(new Vector2d(100d, 100d)),
            new GeoPosition(new Vector2d(200d, 100d)),
            new GeoPosition(new Vector2d(200d, 200d)),
            new GeoPosition(new Vector2d(100d, 100d)),
        ]);
        var polygon = new GeoPolygon(square, [hole, hole, hole]);
        var part = new LocalFeature(
            polygon,
            FeatureMappingTestSupport.SourceFeature("vectors", "holes", null),
            "vectors/holes#0");
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 10);

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], [], FeatureMappingTestSupport.ConstantSampler(7d), diagnostics, limits);

        // 5 outer + 3 x 4 hole positions exceed the budget; counting stops at the hole
        // that crosses the limit instead of summing the whole polygon first.
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Contains("maxCoordinateCount", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(results);
    }

    [Fact]
    public void MappingOutputsShareTheImmutableSourceAttributes()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
            new("vectors", ManifestFeatureTarget.Passability, ManifestFeatureGeometry.Polygon, Passable: false),
        };
        var part = FeatureMappingTestSupport.PolygonPart(
            100d,
            100d,
            900d,
            900d,
            attributes: new Dictionary<string, string> { ["kind"] = "river" });

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, results.Count);
        Assert.Same(part.SourceFeature.Attributes, results[0].Attributes);
        Assert.Same(results[0].Attributes, results[1].Attributes);

        // The public constructor still defends arbitrary caller dictionaries.
        var attributes = new Dictionary<string, string> { ["kind"] = "river" };
        var generated = new GeneratedFeature(
            GeneratedFeatureKind.Water,
            part.Geometry,
            "vectors",
            "poly1",
            part.PartId,
            0,
            attributes);
        Assert.NotSame(attributes, generated.Attributes);
    }

    [Fact]
    public void InputAndOutputCoordinateBudgetsAreIndependent()
    {
        // A one-point feature at maxCoordinateCount 1: input 1 <= 1 and output 1 <= 1
        // both pass, so the point emits rather than being halved by a combined budget.
        var pointLimits = new GeospatialImportLimits(MaxCoordinateCount: 1);
        var pointRules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };

        var pointDiagnostics = new GeoDiagnosticBag();
        var pointResults = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            pointRules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            pointDiagnostics,
            pointLimits);

        Assert.False(pointDiagnostics.HasErrors);
        var waypoint = Assert.Single(pointResults);
        Assert.Equal(GeneratedFeatureKind.Waypoint, waypoint.Kind);

        // The same exact boundary on the output side: a 5-position polygon charges 5
        // input and 5 output coordinates, both at the limit, and still emits its region.
        var polygonLimits = new GeospatialImportLimits(MaxCoordinateCount: 5);
        var polygonRules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Polygon),
        };

        var polygonDiagnostics = new GeoDiagnosticBag();
        var polygonResults = FeatureMapper.Map(
            [FeatureMappingTestSupport.PolygonPart(100d, 100d, 900d, 900d)],
            polygonRules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            polygonDiagnostics,
            polygonLimits);

        Assert.False(polygonDiagnostics.HasErrors);
        var water = Assert.Single(polygonResults);
        Assert.Equal(GeneratedFeatureKind.Water, water.Kind);
    }

    [Fact]
    public void EmptyPublicAttributesAreImmutablyShared()
    {
        var geometry = new GeoPointGeometry(new GeoPosition(new Vector2d(1d, 2d), 3d));
        var position = new GamePosition(1d, 2d, 3d);
        var first = new GeneratedFeature(GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, position: position);
        var second = new GeneratedFeature(GeneratedFeatureKind.Waypoint, geometry, "s", "f", "s/f#0", 0, position: position);

        Assert.Empty(first.Attributes);
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)first.Attributes).Add("mutated", "value"));
        Assert.Empty(second.Attributes);
        Assert.Same(first.Attributes, second.Attributes);
    }

    [Fact]
    public void InputCoordinateLimitStopsMappingWithTypedError()
    {
        var limits = new GeospatialImportLimits(MaxCoordinateCount: 2);
        var parts = new[]
        {
            FeatureMappingTestSupport.LinePart([(10d, 10d), (200d, 10d), (400d, 10d)], featureId: "long"),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(parts, [], FeatureMappingTestSupport.ConstantSampler(7d), diagnostics, limits);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingLimitExceeded, diagnostic.Code);
        Assert.Empty(results);
    }

    [Fact]
    public void CancelledTokenThrows()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            [],
            FeatureMappingTestSupport.ConstantSampler(7d),
            new GeoDiagnosticBag(),
            cancellationToken: source.Token));
    }

    [Fact]
    public void HeightSampleFailureProducesGenerateErrorAndNoFeature()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };
        var part = FeatureMappingTestSupport.PointPart(100d, 100d);

        var failedDiagnostics = new GeoDiagnosticBag();
        var failedResults = FeatureMapper.Map([part], rules, FeatureMappingTestSupport.FailingSampler(), failedDiagnostics);

        Assert.Empty(failedResults);
        var diagnostic = Assert.Single(failedDiagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingHeightSampleFailed, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);

        // A sampler that returns a non-finite height is treated the same way: no
        // fabricated zero.
        var nonFinite = FeatureMappingTestSupport.ConstantSampler(double.NaN);
        var nonFiniteDiagnostics = new GeoDiagnosticBag();
        var nonFiniteResults = FeatureMapper.Map([part], rules, nonFinite, nonFiniteDiagnostics);

        Assert.Empty(nonFiniteResults);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingHeightSampleFailed, Assert.Single(nonFiniteDiagnostics.Items).Code);
    }

    [Fact]
    public void UnsupportedGeometryProducesGenerateErrorAndPreservesOtherFeatures()
    {
        var source = FeatureMappingTestSupport.SourceFeature("vectors", "multi", null);
        var multi = new LocalFeature(
            new GeoMultiGeometry(
            [
                new GeoPointGeometry(new GeoPosition(new Vector2d(100d, 100d))),
                new GeoPointGeometry(new GeoPosition(new Vector2d(200d, 200d))),
            ]),
            source,
            "vectors/multi#0");
        var point = FeatureMappingTestSupport.PointPart(100d, 100d);
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map([multi, point], rules, FeatureMappingTestSupport.ConstantSampler(7d), diagnostics);

        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingUnsupportedGeometry, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);

        var waypoint = Assert.Single(results);
        Assert.Equal(GeneratedFeatureKind.Waypoint, waypoint.Kind);
    }

    [Fact]
    public void ValueWithoutAttributeRuleIsRejectedUpfront()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Waypoint, ManifestFeatureGeometry.Point, Value: "A1"),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        Assert.Empty(results);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingRuleInvalid, diagnostic.Code);
        Assert.Contains("value requires an attribute", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompatibleTargetGeometryRuleIsRejectedUpfront()
    {
        var rules = new List<FeatureMappingRule>
        {
            new("vectors", ManifestFeatureTarget.Water, ManifestFeatureGeometry.Point),
        };

        var diagnostics = new GeoDiagnosticBag();
        var results = FeatureMapper.Map(
            [FeatureMappingTestSupport.PointPart(100d, 100d)],
            rules,
            FeatureMappingTestSupport.ConstantSampler(7d),
            diagnostics);

        Assert.Empty(results);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(FeatureMappingDiagnosticCodes.MappingRuleInvalid, diagnostic.Code);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        var sampler = FeatureMappingTestSupport.ConstantSampler(7d);
        var parts = new[] { FeatureMappingTestSupport.PointPart(100d, 100d) };
        var rules = new FeatureMappingRule[] { new("vectors", ManifestFeatureTarget.Waypoint) };

        Assert.Throws<ArgumentNullException>(() => FeatureMapper.Map(null!, rules, sampler, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => FeatureMapper.Map(parts, null!, sampler, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => FeatureMapper.Map(parts, rules, null!, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => FeatureMapper.Map(parts, rules, sampler, null!));
    }
}
