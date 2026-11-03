using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Projection;

namespace Panopticon.Geospatial.Pipeline.Validation;

/// <summary>
/// Built-in staged validator for manifest control points (WI-B4b-20). Every point
/// produces exactly six ordered checks, horizontal, absolute vertical, relative
/// vertical, red, green, blue, sampled through the staged reader against the same
/// loaded manifest generation used. Independent horizontal truth is the paired
/// expected game XY or an unambiguous generated point feature; a missing, ambiguous,
/// or non-point feature match is a failed check, not a silent first match or a
/// not-evaluated. The relative vertical tolerance is never borrowed for an absolute
/// comparison. The validator bounds its own check list against maxFeatureCount before
/// allocating it; the importer's aggregate-before-copy gate stays authoritative for
/// the combined total.
/// </summary>
public sealed class ControlPointValidator : IStagedPayloadValidator
{
    private const string Category = "control-point";

    private const long ChecksPerPoint = 6L;

    private const double LatticeSampleSpacingGameUnits = 10d;

    private readonly GeospatialImportLimits _limits;

    public ControlPointValidator(GeospatialImportLimits? limits = null)
    {
        _limits = limits ?? new GeospatialImportLimits();
    }

    public IReadOnlyList<PayloadValidationCheck> Validate(
        IGeneratedMapPayloadReader reader,
        GeospatialImportManifest manifest,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var points = manifest.ControlPoints;
        var checks = new List<PayloadValidationCheck>();
        if (points.Count == 0)
        {
            return checks;
        }

        // Bound the validator's own output before allocating it; the importer's
        // aggregate gate bounds the combined total again before any copy.
        long totalChecks;
        try
        {
            totalChecks = checked(points.Count * ChecksPerPoint);
        }
        catch (OverflowException)
        {
            totalChecks = long.MaxValue;
        }

        if (totalChecks > _limits.MaxFeatureCount)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ValidateFailure,
                GeoStage.Validate,
                GeoSeverity.Error,
                $"The {points.Count} manifest control points would produce {totalChecks.ToString(CultureInfo.InvariantCulture)} validation checks, "
                + $"beyond the maxFeatureCount bound of {_limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}."));
            return checks;
        }

        var context = ValidationContext.Create(reader, manifest, _limits, diagnostics);
        checks.Capacity = (int)totalChecks;

        // One bounded ordinal pass over the generated features; lookups then resolve
        // each point without rescanning, and ambiguity stays detectable.
        var featuresById = IndexFeaturesById(reader, cancellationToken);

        for (var index = 0; index < points.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluatePoint(context, points[index], featuresById, checks, cancellationToken);
        }

        return checks;
    }

    private static Dictionary<string, List<GeneratedFeature>> IndexFeaturesById(
        IGeneratedMapPayloadReader reader,
        CancellationToken cancellationToken)
    {
        var features = reader.Metadata.Features;
        var index = new Dictionary<string, List<GeneratedFeature>>(StringComparer.Ordinal);

        for (var index2 = 0; index2 < features.Count; index2++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var feature = features[index2];

            if (!index.TryGetValue(feature.FeatureId, out var candidates))
            {
                candidates = [];
                index.Add(feature.FeatureId, candidates);
            }

            candidates.Add(feature);
        }

        return index;
    }

    private static void EvaluatePoint(
        ValidationContext context,
        ManifestControlPoint point,
        Dictionary<string, List<GeneratedFeature>> featuresById,
        List<PayloadValidationCheck> checks,
        CancellationToken cancellationToken)
    {
        var gameXy = TryResolveGameXy(context, point, out var transformFailure);

        checks.Add(EvaluateHorizontal(context, point, featuresById, gameXy, transformFailure));

        double? elevationMeters = null;
        var elevationFailure = transformFailure;

        if (gameXy is { } position && point.ElevationMeters is not null)
        {
            if (context.Reader.TrySampleHeight(position.X, position.Y, out var gameZ, context.Diagnostics, cancellationToken))
            {
                elevationMeters = (gameZ * context.Transform.MetersPerGameUnit / context.Transform.VerticalExaggeration)
                    + context.Transform.VerticalOriginMeters;
            }
            else
            {
                elevationFailure = "the staged terrain could not be sampled at the control point position";
            }
        }

        checks.Add(EvaluateAbsoluteVertical(context, point, elevationMeters, elevationFailure));
        checks.Add(EvaluateRelativeVertical(context, point, elevationMeters, elevationFailure));

        var redGreenBlue = EvaluateImagery(context, point, gameXy, transformFailure, cancellationToken);
        checks.Add(redGreenBlue[0]);
        checks.Add(redGreenBlue[1]);
        checks.Add(redGreenBlue[2]);
    }

    /// <summary>Transforms the WGS84 control position into payload game XY; purely lexical guards, no I/O.</summary>
    private static (double X, double Y)? TryResolveGameXy(
        ValidationContext context,
        ManifestControlPoint point,
        out string? failure)
    {
        if (!context.Transformer.TryTransform(
                new Vector2d(point.Longitude, point.Latitude),
                CoordinateSystemId.Wgs84,
                context.Transform.ProjectedCoordinateSystem,
                out var projected,
                out var diagnostic))
        {
            failure = $"the coordinate transform failed: {diagnostic.Message}";
            return null;
        }

        var x = (projected.X - context.Transform.OriginEastingNorthing.X) / context.Transform.MetersPerGameUnit;
        var y = (projected.Y - context.Transform.OriginEastingNorthing.Y) / context.Transform.MetersPerGameUnit;

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            failure = "the transformed control point position is not finite";
            return null;
        }

        failure = null;

        return (x, y);
    }

    private static PayloadValidationCheck EvaluateHorizontal(
        ValidationContext context,
        ManifestControlPoint point,
        Dictionary<string, List<GeneratedFeature>> featuresById,
        (double X, double Y)? gameXy,
        string? transformFailure)
    {
        if (transformFailure is not null)
        {
            return Failed(point, "horizontal", $"{point.Name}: {transformFailure}.");
        }

        var truth = ResolveFeatureTruth(point, featuresById);
        double expectedX;
        double expectedY;

        // The tolerance tracks the truth's origin: an explicit expected game XY derives
        // from the DEM's horizontal accuracy, while a matched feature's expected XY
        // derives from that feature's own source, resolved from the unique match even
        // when no featureSourceId scopes it.
        double toleranceMeters;
        if (point.ExpectedGameX is { } explicitX)
        {
            expectedX = explicitX;
            expectedY = point.ExpectedGameY!.Value;
            toleranceMeters = context.ExplicitTruthHorizontalToleranceMeters;
        }
        else if (truth is { Status: FeatureTruthStatus.Found })
        {
            expectedX = truth.Position.X;
            expectedY = truth.Position.Y;
            toleranceMeters = context.FeatureTruthHorizontalToleranceMeters(truth.Match!);
        }
        else if (point.FeatureId is not null)
        {
            return Failed(point, "horizontal", $"{point.Name}: {truth.Failure}.");
        }
        else
        {
            return NotEvaluated(point, "horizontal", $"{point.Name}: no independent horizontal truth was declared.");
        }

        var actual = gameXy!.Value;
        var distanceMeters = Math.Sqrt(
            (Math.Pow(actual.X - expectedX, 2d) + Math.Pow(actual.Y - expectedY, 2d))
            * Math.Pow(context.Transform.MetersPerGameUnit, 2d));

        if (!double.IsFinite(distanceMeters))
        {
            return Failed(point, "horizontal", $"{point.Name}: the horizontal distance is not finite.");
        }

        if (distanceMeters > toleranceMeters)
        {
            return new PayloadValidationCheck(
                Category,
                Suffix(point, "horizontal"),
                PayloadValidationStatus.Failed,
                0d,
                distanceMeters,
                toleranceMeters,
                "meters",
                $"{point.Name}: the control point is {Format(distanceMeters)} m from its expected position, beyond the {Format(toleranceMeters)} m tolerance.");
        }

        return new PayloadValidationCheck(
            Category,
            Suffix(point, "horizontal"),
            PayloadValidationStatus.Passed,
            0d,
            distanceMeters,
            toleranceMeters,
            "meters");
    }

    private static PayloadValidationCheck EvaluateAbsoluteVertical(
        ValidationContext context,
        ManifestControlPoint point,
        double? elevationMeters,
        string? elevationFailure)
    {
        if (point.ElevationMeters is null)
        {
            return NotEvaluated(point, "absolute-vertical", $"{point.Name}: no expected elevation was declared.");
        }

        if (elevationFailure is not null)
        {
            return Failed(point, "absolute-vertical", $"{point.Name}: {elevationFailure}.");
        }

        if (!context.PayloadDatumIsKnown)
        {
            return NotEvaluated(point, "absolute-vertical", $"{point.Name}: the payload vertical datum is 'unknown'.");
        }

        if (context.AbsoluteToleranceMeters is not { } tolerance)
        {
            // Never borrow the relative tolerance for an absolute comparison.
            return Failed(
                point,
                "absolute-vertical",
                $"{point.Name}: no DEM stated vertical accuracy and no explicit tolerances.verticalMeters; the absolute elevation comparison is missing its tolerance.");
        }

        return CompareVertical(point, "absolute-vertical", point.ElevationMeters.Value, elevationMeters!.Value, tolerance);
    }

    private static PayloadValidationCheck EvaluateRelativeVertical(
        ValidationContext context,
        ManifestControlPoint point,
        double? elevationMeters,
        string? elevationFailure)
    {
        if (point.ElevationMeters is null)
        {
            return NotEvaluated(point, "relative-vertical", $"{point.Name}: no expected elevation was declared.");
        }

        if (elevationFailure is not null)
        {
            return Failed(point, "relative-vertical", $"{point.Name}: {elevationFailure}.");
        }

        if (context.RelativeToleranceMeters is not { } tolerance)
        {
            return NotEvaluated(point, "relative-vertical", $"{point.Name}: no explicit tolerances.relativeVerticalMeters was declared.");
        }

        var origin = context.Transform.VerticalOriginMeters;

        return CompareVertical(point, "relative-vertical", point.ElevationMeters.Value - origin, elevationMeters!.Value - origin, tolerance);
    }

    private static PayloadValidationCheck[] EvaluateImagery(
        ValidationContext context,
        ManifestControlPoint point,
        (double X, double Y)? gameXy,
        string? transformFailure,
        CancellationToken cancellationToken)
    {
        const string Red = "red";
        const string Green = "green";
        const string Blue = "blue";

        if (point.ImageryRgb is null)
        {
            return
            [
                NotEvaluated(point, Red, $"{point.Name}: no expected imagery RGB was declared."),
                NotEvaluated(point, Green, $"{point.Name}: no expected imagery RGB was declared."),
                NotEvaluated(point, Blue, $"{point.Name}: no expected imagery RGB was declared."),
            ];
        }

        if (transformFailure is not null)
        {
            return
            [
                Failed(point, Red, $"{point.Name}: {transformFailure}."),
                Failed(point, Green, $"{point.Name}: {transformFailure}."),
                Failed(point, Blue, $"{point.Name}: {transformFailure}."),
            ];
        }

        if (!context.HasImagery)
        {
            return
            [
                Failed(point, Red, $"{point.Name}: an expected imagery RGB was declared but the payload has no imagery layer."),
                Failed(point, Green, $"{point.Name}: an expected imagery RGB was declared but the payload has no imagery layer."),
                Failed(point, Blue, $"{point.Name}: an expected imagery RGB was declared but the payload has no imagery layer."),
            ];
        }

        if (!context.Reader.TrySampleImagery(gameXy!.Value.X, gameXy!.Value.Y, out var sample, context.Diagnostics, cancellationToken))
        {
            return
            [
                Failed(point, Red, $"{point.Name}: the staged imagery could not be sampled at the control point position."),
                Failed(point, Green, $"{point.Name}: the staged imagery could not be sampled at the control point position."),
                Failed(point, Blue, $"{point.Name}: the staged imagery could not be sampled at the control point position."),
            ];
        }

        var tolerance = point.ImageryToleranceBytes!.Value;

        return
        [
            CompareChannel(point, Red, point.ImageryRgb[0], sample.R, tolerance),
            CompareChannel(point, Green, point.ImageryRgb[1], sample.G, tolerance),
            CompareChannel(point, Blue, point.ImageryRgb[2], sample.B, tolerance),
        ];
    }

    private static PayloadValidationCheck CompareVertical(
        ManifestControlPoint point,
        string check,
        double expectedMeters,
        double actualMeters,
        double toleranceMeters)
    {
        var deviation = Math.Abs(actualMeters - expectedMeters);

        if (!double.IsFinite(deviation))
        {
            return Failed(point, check, $"{point.Name}: the vertical deviation is not finite.");
        }

        if (deviation > toleranceMeters)
        {
            return new PayloadValidationCheck(
                Category,
                Suffix(point, check),
                PayloadValidationStatus.Failed,
                expectedMeters,
                actualMeters,
                toleranceMeters,
                "meters",
                $"{point.Name}: the elevation deviates {Format(deviation)} m, beyond the {Format(toleranceMeters)} m tolerance.");
        }

        return new PayloadValidationCheck(
            Category,
            Suffix(point, check),
            PayloadValidationStatus.Passed,
            expectedMeters,
            actualMeters,
            toleranceMeters,
            "meters");
    }

    private static PayloadValidationCheck CompareChannel(
        ManifestControlPoint point,
        string channel,
        int expected,
        byte actual,
        int toleranceBytes)
    {
        var deviation = Math.Abs(actual - expected);

        if (deviation > toleranceBytes)
        {
            return new PayloadValidationCheck(
                Category,
                Suffix(point, channel),
                PayloadValidationStatus.Failed,
                expected,
                actual,
                toleranceBytes,
                "bytes",
                $"{point.Name}: the {channel} channel deviates {deviation} bytes, beyond the {toleranceBytes} byte tolerance.");
        }

        return new PayloadValidationCheck(
            Category,
            Suffix(point, channel),
            PayloadValidationStatus.Passed,
            expected,
            actual,
            toleranceBytes,
            "bytes");
    }

    private static FeatureTruth ResolveFeatureTruth(
        ManifestControlPoint point,
        Dictionary<string, List<GeneratedFeature>> featuresById)
    {
        if (point.FeatureId is null)
        {
            return FeatureTruth.Unresolved;
        }

        if (!featuresById.TryGetValue(point.FeatureId, out var candidates))
        {
            return FeatureTruth.Missing(point.FeatureId, point.FeatureSourceId);
        }

        GeneratedFeature? match = null;
        var matches = 0;

        foreach (var candidate in candidates)
        {
            if (point.FeatureSourceId is not null && !string.Equals(candidate.SourceId, point.FeatureSourceId, StringComparison.Ordinal))
            {
                continue;
            }

            matches++;
            match = candidate;
        }

        if (matches == 0)
        {
            return FeatureTruth.Missing(point.FeatureId, point.FeatureSourceId);
        }

        if (matches > 1)
        {
            return FeatureTruth.Ambiguous(point.FeatureId, point.FeatureSourceId);
        }

        if (match!.Position is null)
        {
            return FeatureTruth.NonPoint(point.FeatureId, point.FeatureSourceId);
        }

        return FeatureTruth.FromPosition(match);
    }

    private static PayloadValidationCheck Failed(ManifestControlPoint point, string check, string message)
    {
        return new PayloadValidationCheck(Category, Suffix(point, check), PayloadValidationStatus.Failed, message: message);
    }

    private static PayloadValidationCheck NotEvaluated(ManifestControlPoint point, string check, string message)
    {
        return new PayloadValidationCheck(Category, Suffix(point, check), PayloadValidationStatus.NotEvaluated, message: message);
    }

    private static string Suffix(ManifestControlPoint point, string check) => point.Name + "/" + check;

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private readonly record struct FeatureTruth(FeatureTruthStatus Status, (double X, double Y) Position, GeneratedFeature? Match, string? Failure)
    {
        public static FeatureTruth Unresolved { get; } = new(FeatureTruthStatus.Unresolved, (0d, 0d), null, null);

        public static FeatureTruth FromPosition(GeneratedFeature match) => new(FeatureTruthStatus.Found, (match.Position!.Value.X, match.Position!.Value.Y), match, null);

        public static FeatureTruth Missing(string featureId, string? featureSourceId) =>
            new(FeatureTruthStatus.Missing, (0d, 0d), null, Describe("no generated feature matches", featureId, featureSourceId));

        public static FeatureTruth Ambiguous(string featureId, string? featureSourceId) =>
            new(FeatureTruthStatus.Ambiguous, (0d, 0d), null, Describe("multiple generated features match", featureId, featureSourceId));

        public static FeatureTruth NonPoint(string featureId, string? featureSourceId) =>
            new(FeatureTruthStatus.NonPoint, (0d, 0d), null, Describe("the matching generated feature is not a point feature", featureId, featureSourceId));

        private static string Describe(string outcome, string featureId, string? featureSourceId)
        {
            var scope = featureSourceId is null
                ? $"feature id '{featureId}'"
                : $"feature id '{featureId}' in source '{featureSourceId}'";

            return $"{outcome} {scope}";
        }
    }

    private enum FeatureTruthStatus
    {
        Unresolved,

        Found,

        Missing,

        Ambiguous,

        NonPoint,
    }

    private sealed record ValidationContext
    {
        private ValidationContext(
            IGeneratedMapPayloadReader reader,
            WgsTransverseMercatorTransformer transformer,
            GeospatialMapTransform transform,
            bool payloadDatumIsKnown,
            bool hasImagery,
            double spacingMeters,
            double explicitHorizontalMeters,
            double demHorizontalAccuracyMeters,
            IReadOnlyDictionary<string, ManifestSource?> sourcesById,
            double? absoluteToleranceMeters,
            double? relativeToleranceMeters,
            GeoDiagnosticBag diagnostics)
        {
            Reader = reader;
            Transformer = transformer;
            Transform = transform;
            PayloadDatumIsKnown = payloadDatumIsKnown;
            HasImagery = hasImagery;
            SpacingMeters = spacingMeters;
            ExplicitHorizontalMeters = explicitHorizontalMeters;
            DemHorizontalAccuracyMeters = demHorizontalAccuracyMeters;
            SourcesById = sourcesById;
            AbsoluteToleranceMeters = absoluteToleranceMeters;
            RelativeToleranceMeters = relativeToleranceMeters;
            Diagnostics = diagnostics;
        }

        public IGeneratedMapPayloadReader Reader { get; }

        public WgsTransverseMercatorTransformer Transformer { get; }

        public GeospatialMapTransform Transform { get; }

        public bool PayloadDatumIsKnown { get; }

        public bool HasImagery { get; }

        private double SpacingMeters { get; }

        private double ExplicitHorizontalMeters { get; }

        private double DemHorizontalAccuracyMeters { get; }

        private IReadOnlyDictionary<string, ManifestSource?> SourcesById { get; }

        public double? AbsoluteToleranceMeters { get; }

        public double? RelativeToleranceMeters { get; }

        public GeoDiagnosticBag Diagnostics { get; }

        /// <summary>
        /// An explicit expected game XY derives from the DEM, so the DEM's horizontal
        /// accuracy applies; a null accuracy contributes zero.
        /// </summary>
        public double ExplicitTruthHorizontalToleranceMeters =>
            Math.Max(SpacingMeters, Math.Max(DemHorizontalAccuracyMeters, ExplicitHorizontalMeters));

        /// <summary>
        /// A matched feature's expected XY derives from that feature's own source, so
        /// the matched source's horizontal accuracy applies, never the DEM's; the
        /// source id resolves from the unique match itself.
        /// </summary>
        public double FeatureTruthHorizontalToleranceMeters(GeneratedFeature match)
        {
            SourcesById.TryGetValue(match.SourceId, out var source);

            return Math.Max(SpacingMeters, Math.Max(source?.HorizontalAccuracyMeters ?? 0d, ExplicitHorizontalMeters));
        }

        public static ValidationContext Create(
            IGeneratedMapPayloadReader reader,
            GeospatialImportManifest manifest,
            GeospatialImportLimits limits,
            GeoDiagnosticBag diagnostics)
        {
            var transform = reader.Metadata.Grid.Transform;
            ManifestSource? elevation = null;
            var sourcesById = new Dictionary<string, ManifestSource?>(StringComparer.Ordinal);

            foreach (var source in manifest.Sources)
            {
                if (source.Role == ManifestSourceRole.Elevation)
                {
                    elevation = source;
                }

                sourcesById.TryAdd(source.Id, source);
            }

            var payloadDatumIsKnown = !string.Equals(transform.VerticalDatum.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);

            // User-approved contract reconciliation: the absolute vertical tolerance binds as the
            // operator-approved maximum of the DEM's stated accuracy and the explicit tolerance;
            // a stricter explicit tolerance never undercuts the stated accuracy floor.
            var absoluteToleranceMeters = Max(elevation?.VerticalAccuracyMeters, manifest.Tolerances.VerticalMeters);

            return new ValidationContext(
                reader,
                new WgsTransverseMercatorTransformer(),
                transform,
                payloadDatumIsKnown,
                reader.Metadata.Grid.ImageryPixelsPerSample is not null,
                LatticeSampleSpacingGameUnits * transform.MetersPerGameUnit,
                manifest.Tolerances.HorizontalMeters ?? 0d,
                elevation?.HorizontalAccuracyMeters ?? 0d,
                sourcesById,
                absoluteToleranceMeters,
                manifest.Tolerances.RelativeVerticalMeters,
                diagnostics);
        }

        private static double? Max(double? left, double? right)
        {
            if (left is null)
            {
                return right;
            }

            if (right is null)
            {
                return left;
            }

            return Math.Max(left.Value, right.Value);
        }
    }
}
