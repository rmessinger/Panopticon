using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Features;

/// <summary>
/// Applies manifest feature-mapping rules to clipped local features (B4b deep dive
/// section 5.3). Authoring targets take the first matching rule; water, passability, and
/// buildability retain every matching rule with its manifest index; material rules belong
/// to the material generator. A feature with no authoring or region match becomes one
/// annotation, and generated points sample the terrain Z in game units.
/// </summary>
public static class FeatureMapper
{
    /// <summary>
    /// Maps clipped features to generated features. Invalid rules are reported up front
    /// and abort with an empty result; input, expanded output, and rule-comparison
    /// budgets are enforced with typed diagnostics; cancellation throws.
    /// </summary>
    public static IReadOnlyList<GeneratedFeature> Map(
        IEnumerable<LocalFeature> localFeatures,
        IReadOnlyList<FeatureMappingRule> rules,
        ITerrainHeightSampler terrain,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localFeatures);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(diagnostics);
        limits ??= new GeospatialImportLimits();

        if (!ValidateRules(rules, diagnostics, cancellationToken))
        {
            return [];
        }

        var context = new MapContext(rules, terrain, diagnostics, limits, cancellationToken);
        foreach (var feature in localFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(feature);

            context.ProcessFeature(feature);

            if (context.Stopped)
            {
                break;
            }
        }

        return context.Results;
    }

    private static bool ValidateRules(IReadOnlyList<FeatureMappingRule> rules, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken)
    {
        var valid = true;
        for (var i = 0; i < rules.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rule = rules[i];
            if (rule is null)
            {
                AddError(
                    diagnostics,
                    FeatureMappingDiagnosticCodes.MappingRuleInvalid,
                    $"featureMappings[{i.ToString(CultureInfo.InvariantCulture)}] is null.");
                valid = false;
                continue;
            }

            if (ValidateRule(rule, i, diagnostics))
            {
                continue;
            }

            valid = false;
        }

        return valid;
    }

    private static bool ValidateRule(FeatureMappingRule rule, int index, GeoDiagnosticBag diagnostics)
    {
        var valid = true;

        void Reject(string reason)
        {
            AddError(
                diagnostics,
                FeatureMappingDiagnosticCodes.MappingRuleInvalid,
                $"featureMappings[{index.ToString(CultureInfo.InvariantCulture)}] {reason}");
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(rule.SourceId))
        {
            Reject("requires a non-empty sourceId.");
        }

        if (!Enum.IsDefined(rule.Target))
        {
            Reject("has an undefined target.");
        }
        else if (rule.Geometry is { } geometry)
        {
            if (!Enum.IsDefined(geometry))
            {
                Reject("has an undefined geometry predicate.");
            }
            else if (!FeatureMappingRule.IsTargetCompatible(rule.Target, geometry))
            {
                Reject(
                    $"target '{ManifestTokens.Kebab(rule.Target)}' is not valid for geometry '{ManifestTokens.Kebab(geometry)}'.");
            }
        }

        if (rule.Attribute is not null && string.IsNullOrWhiteSpace(rule.Attribute))
        {
            Reject("attribute must be a non-empty attribute name.");
        }

        if (rule.Value is not null && rule.Attribute is null)
        {
            Reject("value requires an attribute to match.");
        }

        if (rule.OrientationDegrees is { } degrees && !double.IsFinite(degrees))
        {
            Reject("orientationDegrees must be a finite angle in degrees.");
        }

        if (rule.Target == ManifestFeatureTarget.Object)
        {
            if (string.IsNullOrWhiteSpace(rule.ObjectDefinition))
            {
                Reject("target 'object' requires objectDefinition.");
            }

            if (rule.Orientation is null)
            {
                Reject("target 'object' requires an orientation policy.");
            }
            else if (!Enum.IsDefined(rule.Orientation.Value))
            {
                Reject("has an undefined orientation policy.");
            }
            else if (rule.Orientation == ManifestFeatureOrientation.Fixed && rule.OrientationDegrees is null)
            {
                Reject("orientation 'fixed' requires orientationDegrees.");
            }
        }
        else
        {
            if (rule.Orientation is not null)
            {
                Reject("orientation applies to target 'object' only.");
            }

            if (rule.ObjectDefinition is not null)
            {
                Reject("objectDefinition applies to target 'object' only.");
            }
        }

        if (rule.Target == ManifestFeatureTarget.Material)
        {
            if (string.IsNullOrWhiteSpace(rule.Material))
            {
                Reject("target 'material' requires material.");
            }
        }
        else if (rule.Material is not null)
        {
            Reject("material applies to target 'material' only.");
        }

        if (rule.Target == ManifestFeatureTarget.Passability)
        {
            if (rule.Passable is null)
            {
                Reject("target 'passability' requires passable.");
            }
        }
        else if (rule.Passable is not null)
        {
            Reject("passable applies to target 'passability' only.");
        }

        if (rule.Target == ManifestFeatureTarget.Buildability)
        {
            if (rule.Buildable is null)
            {
                Reject("target 'buildability' requires buildable.");
            }
        }
        else if (rule.Buildable is not null)
        {
            Reject("buildable applies to target 'buildability' only.");
        }

        return valid;
    }

    private static bool RuleMatches(FeatureMappingRule rule, LocalFeature feature, ManifestFeatureGeometry geometry)
    {
        var source = feature.SourceFeature;
        if (!string.Equals(rule.SourceId, source.SourceId, StringComparison.Ordinal))
        {
            return false;
        }

        // A rule only ever applies to geometries its target can consume, even without an
        // explicit geometry predicate.
        if (!FeatureMappingRule.IsTargetCompatible(rule.Target, geometry))
        {
            return false;
        }

        if (rule.Geometry is { } predicate && predicate != geometry)
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

    private static ManifestFeatureGeometry? FeatureGeometryOf(GeoGeometry geometry)
    {
        return geometry switch
        {
            GeoPointGeometry => ManifestFeatureGeometry.Point,
            GeoLineString => ManifestFeatureGeometry.Line,
            GeoPolygon => ManifestFeatureGeometry.Polygon,
            _ => null,
        };
    }

    private static bool IsAuthoringTarget(ManifestFeatureTarget target)
    {
        return target is ManifestFeatureTarget.Landmark
            or ManifestFeatureTarget.Waypoint
            or ManifestFeatureTarget.Object
            or ManifestFeatureTarget.Road
            or ManifestFeatureTarget.Rail
            or ManifestFeatureTarget.River
            or ManifestFeatureTarget.Annotation;
    }

    private static bool IsRegionTarget(ManifestFeatureTarget target)
    {
        return target is ManifestFeatureTarget.Water
            or ManifestFeatureTarget.Passability
            or ManifestFeatureTarget.Buildability;
    }

    private static GeneratedFeatureKind ToGeneratedKind(ManifestFeatureTarget target)
    {
        return target switch
        {
            ManifestFeatureTarget.Landmark => GeneratedFeatureKind.Landmark,
            ManifestFeatureTarget.Waypoint => GeneratedFeatureKind.Waypoint,
            ManifestFeatureTarget.Object => GeneratedFeatureKind.ObjectCandidate,
            ManifestFeatureTarget.Road => GeneratedFeatureKind.Road,
            ManifestFeatureTarget.Rail => GeneratedFeatureKind.Rail,
            ManifestFeatureTarget.River => GeneratedFeatureKind.River,
            ManifestFeatureTarget.Water => GeneratedFeatureKind.Water,
            ManifestFeatureTarget.Passability => GeneratedFeatureKind.Passability,
            ManifestFeatureTarget.Buildability => GeneratedFeatureKind.Buildability,
            _ => GeneratedFeatureKind.Annotation,
        };
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360d;
        if (normalized < 0d)
        {
            normalized += 360d;
        }

        return normalized;
    }

    private static void AddError(GeoDiagnosticBag diagnostics, string code, string message)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message));
    }

    private sealed class MapContext
    {
        private readonly IReadOnlyList<FeatureMappingRule> _rules;

        private readonly ITerrainHeightSampler _terrain;

        private readonly GeoDiagnosticBag _diagnostics;

        private readonly GeospatialImportLimits _limits;

        private readonly CancellationToken _cancellationToken;

        private readonly List<GeneratedFeature> _results = [];

        private readonly IReadOnlyList<GeneratedFeature> _resultsView;

        private readonly List<(int RuleIndex, FeatureMappingRule Rule)> _regionMatches = [];

        private long _ruleComparisonsRemaining;

        private long _inputCoordinates;

        private long _inputFeatures;

        private long _partPositions;

        private long _emittedCoordinates;

        public MapContext(
            IReadOnlyList<FeatureMappingRule> rules,
            ITerrainHeightSampler terrain,
            GeoDiagnosticBag diagnostics,
            GeospatialImportLimits limits,
            CancellationToken cancellationToken)
        {
            _rules = rules;
            _terrain = terrain;
            _diagnostics = diagnostics;
            _limits = limits;
            _cancellationToken = cancellationToken;
            _resultsView = _results.AsReadOnly();
            _ruleComparisonsRemaining = limits.MaxFeatureRuleComparisons;
        }

        // A read-only view: callers must not be able to cast back to the working list.
        public IReadOnlyList<GeneratedFeature> Results => _resultsView;

        public bool Stopped { get; private set; }

        public void ProcessFeature(LocalFeature feature)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // Unsupported geometry is rejected before any traversal: a deeply nested
            // multi geometry must not be walked.
            if (FeatureGeometryOf(feature.Geometry) is not { } geometry)
            {
                AddError(
                    _diagnostics,
                    FeatureMappingDiagnosticCodes.MappingUnsupportedGeometry,
                    $"Feature part '{feature.PartId}' has geometry kind {feature.Geometry.GetType().Name}, which the mapper does not support.");
                return;
            }

            if (!ChargeInput(feature, geometry))
            {
                return;
            }

            var authoringIndex = -1;
            FeatureMappingRule? authoringRule = null;
            _regionMatches.Clear();
            for (var i = 0; i < _rules.Count; i++)
            {
                if ((i & 63) == 0)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                if (_ruleComparisonsRemaining == 0)
                {
                    LimitExceeded(
                        $"Rule comparisons exceeded the maxFeatureRuleComparisons limit {_limits.MaxFeatureRuleComparisons.ToString(CultureInfo.InvariantCulture)} at part '{feature.PartId}'.");
                    return;
                }

                _ruleComparisonsRemaining--;

                var rule = _rules[i];
                if (!RuleMatches(rule, feature, geometry))
                {
                    continue;
                }

                if (IsAuthoringTarget(rule.Target))
                {
                    // Authoring targets take the first matching rule only.
                    if (authoringRule is null)
                    {
                        authoringRule = rule;
                        authoringIndex = i;
                    }
                }
                else if (IsRegionTarget(rule.Target))
                {
                    // Collected only while it fits the remaining output budgets, so the
                    // list cannot allocate matches that can never be emitted.
                    if (!RegionWithinOutputBudget())
                    {
                        LimitExceeded(
                            $"Collecting region matches for part '{feature.PartId}' exceeded the remaining maxFeatureCount or maxCoordinateCount output budget.");
                        return;
                    }

                    _regionMatches.Add((i, rule));
                }

                // Material rules are evaluated independently by the material generator.
            }

            if (authoringRule is not null && !EmitAuthoring(feature, authoringRule, authoringIndex))
            {
                return;
            }

            foreach (var (ruleIndex, rule) in _regionMatches)
            {
                if (!Emit(feature, ToGeneratedKind(rule.Target), ruleIndex, objectPlacement: null))
                {
                    return;
                }
            }

            if (authoringRule is null && _regionMatches.Count == 0)
            {
                Emit(feature, GeneratedFeatureKind.Annotation, -1, objectPlacement: null);
            }
        }

        private bool ChargeInput(LocalFeature feature, ManifestFeatureGeometry geometry)
        {
            if (_inputFeatures >= _limits.MaxFeatureCount)
            {
                LimitExceeded(
                    $"Input feature count exceeded the maxFeatureCount limit {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)} at part '{feature.PartId}'.");
                return false;
            }

            _inputFeatures++;

            var coordinateBudget = _limits.MaxCoordinateCount - _inputCoordinates;
            if (!TryCountPositions(feature.Geometry, geometry, coordinateBudget, out var positions))
            {
                LimitExceeded(
                    $"Total input coordinates exceeded the maxCoordinateCount limit {_limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)} at part '{feature.PartId}'.");
                return false;
            }

            _inputCoordinates += positions;
            _partPositions = positions;
            return true;
        }

        private bool TryCountPositions(GeoGeometry geometry, ManifestFeatureGeometry kind, long budget, out long positions)
        {
            // Only point, line, and polygon reach this: multi geometry was rejected above,
            // so the walk never recurses. Rings are charged incrementally with
            // cancellation so a pathological polygon cannot run unbounded.
            positions = 0L;
            if (kind == ManifestFeatureGeometry.Point)
            {
                positions = 1L;
                return positions <= budget;
            }

            if (kind == ManifestFeatureGeometry.Line)
            {
                positions = ((GeoLineString)geometry).Positions.Count;
                return positions <= budget;
            }

            var polygon = (GeoPolygon)geometry;
            positions += polygon.OuterRing.Positions.Count;
            var overBudget = positions > budget;
            for (var i = 0; !overBudget && i < polygon.InnerRings.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                positions += polygon.InnerRings[i].Positions.Count;
                overBudget = positions > budget;
            }

            return !overBudget;
        }

        private bool RegionWithinOutputBudget()
        {
            if (_regionMatches.Count + 1 > (long)_limits.MaxFeatureCount - _results.Count)
            {
                return false;
            }

            // Output coordinates are bounded independently of the input charge, matching
            // the clipper's separate input and emitted-position budgets.
            return _emittedCoordinates + ((_regionMatches.Count + 1) * _partPositions)
                <= _limits.MaxCoordinateCount;
        }

        private bool EmitAuthoring(LocalFeature feature, FeatureMappingRule rule, int ruleIndex)
        {
            GeneratedObjectPlacement? placement = null;
            if (rule.Target == ManifestFeatureTarget.Object)
            {
                if (rule.Orientation == ManifestFeatureOrientation.AlongFeature)
                {
                    AddError(
                        _diagnostics,
                        FeatureMappingDiagnosticCodes.MappingOrientationUndefined,
                        $"Object rule {ruleIndex.ToString(CultureInfo.InvariantCulture)} matches point part '{feature.PartId}' with 'along-feature' orientation, which is undefined for a point; no heading is invented.");
                    return true;
                }

                var degrees = rule.Orientation == ManifestFeatureOrientation.None
                    ? 0d
                    : NormalizeDegrees(rule.OrientationDegrees!.Value);
                placement = new GeneratedObjectPlacement(rule.ObjectDefinition!, degrees);
            }

            return Emit(feature, ToGeneratedKind(rule.Target), ruleIndex, placement);
        }

        private bool Emit(LocalFeature feature, GeneratedFeatureKind kind, int ruleIndex, GeneratedObjectPlacement? objectPlacement)
        {
            GamePosition? position = null;
            if (feature.Geometry is GeoPointGeometry point)
            {
                if (!TrySamplePosition(point.Position.Coordinate.X, point.Position.Coordinate.Y, out var gameZ))
                {
                    AddError(
                        _diagnostics,
                        FeatureMappingDiagnosticCodes.MappingHeightSampleFailed,
                        $"Sampling the terrain height for point part '{feature.PartId}' failed; no height is fabricated.");

                    // The part is skipped; mapping continues with the remaining parts.
                    return true;
                }

                position = new GamePosition(point.Position.Coordinate.X, point.Position.Coordinate.Y, gameZ);
            }

            if (_results.Count >= _limits.MaxFeatureCount)
            {
                LimitExceeded(
                    $"Mapping part '{feature.PartId}' exceeded the maxFeatureCount limit {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}.");
                return false;
            }

            // Charged before the constructor allocates: an over-budget part copies no
            // attributes and constructs nothing. Output coordinates are bounded
            // independently of the input charge.
            if (_emittedCoordinates + _partPositions > _limits.MaxCoordinateCount)
            {
                LimitExceeded(
                    $"Mapping part '{feature.PartId}' exceeded the maxCoordinateCount limit {_limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");
                return false;
            }

            var rule = ruleIndex >= 0 ? _rules[ruleIndex] : null;
            var source = feature.SourceFeature;
            _results.Add(
                GeneratedFeature.CreateShared(
                    kind,
                    feature.Geometry,
                    source.SourceId,
                    source.FeatureId,
                    feature.PartId,
                    ruleIndex,
                    source.Attributes,
                    position,
                    objectPlacement,
                    kind == GeneratedFeatureKind.Passability ? rule!.Passable : null,
                    kind == GeneratedFeatureKind.Buildability ? rule!.Buildable : null,
                    rule?.Coverage));
            _emittedCoordinates += _partPositions;
            return true;
        }

        private bool TrySamplePosition(double gameX, double gameY, out double gameZ)
        {
            return _terrain.TrySample(gameX, gameY, out gameZ) && double.IsFinite(gameZ);
        }

        private void LimitExceeded(string message)
        {
            AddError(_diagnostics, FeatureMappingDiagnosticCodes.MappingLimitExceeded, message);
            Stopped = true;
        }
    }
}
