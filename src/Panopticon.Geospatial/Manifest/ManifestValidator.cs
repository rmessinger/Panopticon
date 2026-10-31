using System;
using System.Collections.Generic;
using System.Globalization;

namespace Panopticon.Geospatial.Manifest;

internal static class ManifestValidator
{
    public static GeospatialImportManifest? TryBuild(
        ManifestDto? dto,
        string manifestDirectory,
        string manifestPath,
        GeoDiagnosticBag diagnostics)
    {
        if (dto is null)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestSchemaInvalid,
                GeoStage.Manifest,
                GeoSeverity.Error,
                "The manifest root must be a JSON object.",
                manifestPath));
            return null;
        }

        var context = new ValidationContext(diagnostics, manifestPath);
        var sources = ValidateSources(context, dto, manifestDirectory);
        var target = ValidateTargetCoordinateSystem(context, dto.TargetCoordinateSystem);
        ValidateTopLevelFields(context, dto);
        var verticalPolicy = ValidateVerticalPolicy(context, dto.VerticalPolicy);
        var rasterPolicy = ValidateRasterPolicy(context, dto.RasterPolicy);
        var tolerances = ValidateTolerances(context, dto.Tolerances, sources);
        var materialRules = ValidateMaterialRules(context, dto.MaterialRules, sources);
        var featureMappings = ValidateFeatureMappings(context, dto.FeatureMappings, sources);
        var slopePolicy = ValidateSlopePolicy(context, dto.SlopePolicy);

        if (!context.IsValid)
        {
            return null;
        }

        return new GeospatialImportManifest
        {
            Sources = sources.Sources.AsReadOnly(),
            TargetCoordinateSystem = target!,
            MetersPerGameUnit = dto.MetersPerGameUnit!.Value,
            DefaultMaterial = dto.DefaultMaterial!.Trim(),
            ExtentFeatureId = NormalizeOptional(dto.ExtentFeatureId),
            VerticalPolicy = verticalPolicy,
            RasterPolicy = rasterPolicy,
            Tolerances = tolerances,
            MaterialRules = materialRules.AsReadOnly(),
            FeatureMappings = featureMappings.AsReadOnly(),
            SlopePolicy = slopePolicy,
            MaxScaleError = dto.MaxScaleError ?? 0.001,
        };
    }

    private sealed class SourceSummary
    {
        public List<ManifestSource> Sources { get; } = [];

        public Dictionary<string, ManifestSourceRole> RolesById { get; } = [];

        public bool HasElevation { get; set; }

        public bool HasUnknownElevationDatum { get; set; }

        public bool HasStatedVerticalAccuracy { get; set; }

        public bool HasClassificationSource { get; set; }

        public bool HasVectorSource { get; set; }

        public bool HasExtentSource { get; set; }
    }

    private sealed class ValidationContext(GeoDiagnosticBag bag, string manifestPath)
    {
        public bool IsValid { get; private set; } = true;

        public void MissingRequired(string message, string? sourcePath = null) =>
            Fail(GeoDiagnosticCodes.ManifestMissingRequired, message, sourcePath);

        public void ValueInvalid(string message, string? sourcePath = null) =>
            Fail(GeoDiagnosticCodes.ManifestValueInvalid, message, sourcePath);

        public void PathRejected(string message, string? sourcePath = null) =>
            Fail(GeoDiagnosticCodes.ManifestSourcePathRejected, message, sourcePath);

        private void Fail(string code, string message, string? sourcePath)
        {
            IsValid = false;
            bag.Add(new GeoDiagnostic(code, GeoStage.Manifest, GeoSeverity.Error, message, sourcePath ?? manifestPath));
        }
    }

    private static SourceSummary ValidateSources(
        ValidationContext context,
        ManifestDto dto,
        string manifestDirectory)
    {
        var summary = new SourceSummary();
        if (dto.Sources is null || dto.Sources.Count == 0)
        {
            context.MissingRequired("The 'sources' array must list at least one source.");
            return summary;
        }

        var extentCount = 0;
        var elevationCount = 0;
        var imageryCount = 0;
        var classificationCount = 0;
        for (var i = 0; i < dto.Sources.Count; i++)
        {
            var source = dto.Sources[i];
            if (source is null)
            {
                context.MissingRequired($"sources[{i}] is null.");
                continue;
            }

            var field = $"sources[{i}]";
            var id = RequiredText(context, source.Id, $"{field}.id");
            var relativePath = RequiredText(context, source.Path, $"{field}.path");
            if (relativePath is not null)
            {
                relativePath = ManifestPathGuard.NormalizeRelativePath(relativePath);
                var rejection = ManifestPathGuard.Validate(manifestDirectory, relativePath);
                if (rejection is not null)
                {
                    context.PathRejected(rejection, relativePath);
                }
            }

            if (source.Kind is null)
            {
                context.MissingRequired($"{field}.kind is required.");
            }

            if (source.Role is null)
            {
                context.MissingRequired($"{field}.role is required.");
            }

            if (source.Kind is not null && source.Role is not null && !RoleMatchesKind(source.Kind.Value, source.Role.Value))
            {
                context.ValueInvalid(
                    $"Source '{id ?? field}': kind '{ManifestTokens.Kebab(source.Kind.Value)}' cannot serve role '{ManifestTokens.Kebab(source.Role.Value)}'.",
                    relativePath);
            }

            var role = source.Role;
            var datum = NormalizeDatum(source.VerticalDatum);
            if (role is not null)
            {
                switch (role.Value)
                {
                    case ManifestSourceRole.Extent:
                        extentCount++;
                        summary.HasExtentSource = true;
                        break;
                    case ManifestSourceRole.Elevation:
                        elevationCount++;
                        summary.HasElevation = true;
                        if (datum == "unknown")
                        {
                            summary.HasUnknownElevationDatum = true;
                        }

                        if (source.VerticalAccuracyMeters is > 0)
                        {
                            summary.HasStatedVerticalAccuracy = true;
                        }

                        break;
                    case ManifestSourceRole.Imagery:
                        imageryCount++;
                        break;
                    case ManifestSourceRole.Classification:
                        classificationCount++;
                        summary.HasClassificationSource = true;
                        break;
                    case ManifestSourceRole.Vector:
                        summary.HasVectorSource = true;
                        break;
                }
            }

            ValidateOptionalPositiveFinite(context, source.HorizontalAccuracyMeters, $"{field}.horizontalAccuracyMeters", relativePath);
            ValidateOptionalPositiveFinite(context, source.VerticalAccuracyMeters, $"{field}.verticalAccuracyMeters", relativePath);

            var trimmedId = id ?? field;
            if (id is not null && role is not null && !summary.RolesById.TryAdd(id, role.Value))
            {
                context.ValueInvalid($"Duplicate source id '{id}'.");
            }

            summary.Sources.Add(new ManifestSource(
                trimmedId,
                source.Kind ?? ManifestSourceKind.Kml,
                relativePath ?? string.Empty,
                role ?? ManifestSourceRole.Extent,
                datum,
                NormalizeOptional(source.Label),
                NormalizeOptional(source.Attribution),
                source.HorizontalAccuracyMeters,
                source.VerticalAccuracyMeters));
        }

        if (extentCount != 1)
        {
            context.MissingRequired($"Exactly one source with role 'extent' is required; found {extentCount}.");
        }

        if (elevationCount != 1)
        {
            context.MissingRequired($"Exactly one source with role 'elevation' is required; found {elevationCount}.");
        }

        if (imageryCount > 1)
        {
            context.MissingRequired($"At most one source with role 'imagery' is allowed; found {imageryCount}.");
        }

        if (classificationCount > 1)
        {
            context.MissingRequired($"At most one source with role 'classification' is allowed; found {classificationCount}.");
        }

        return summary;
    }

    private static bool RoleMatchesKind(ManifestSourceKind kind, ManifestSourceRole role) =>
        (kind, role) switch
        {
            (ManifestSourceKind.Kml or ManifestSourceKind.Kmz, ManifestSourceRole.Extent or ManifestSourceRole.Vector) => true,
            (ManifestSourceKind.Geotiff, ManifestSourceRole.Elevation or ManifestSourceRole.Imagery or ManifestSourceRole.Classification) => true,
            _ => false,
        };

    private static TargetCoordinateSystemSpec? ValidateTargetCoordinateSystem(
        ValidationContext context,
        TargetCoordinateSystemDto? dto)
    {
        if (dto is null)
        {
            context.MissingRequired("targetCoordinateSystem is required.");
            return null;
        }

        if (dto.Type is null)
        {
            context.MissingRequired("targetCoordinateSystem.type is required.");
            return null;
        }

        var ok = true;
        int? zone = null;
        ManifestHemisphere? hemisphere = null;
        double? centralMeridian = null;
        if (dto.Type == ManifestCrsKind.Utm)
        {
            if (dto.Zone is null)
            {
                context.MissingRequired("targetCoordinateSystem.zone is required for type 'utm'.");
                ok = false;
            }
            else if (dto.Zone.Value is < 1 or > 60)
            {
                context.ValueInvalid($"targetCoordinateSystem.zone must be between 1 and 60; found {dto.Zone.Value}.");
                ok = false;
            }
            else
            {
                zone = dto.Zone;
            }

            if (dto.Hemisphere is null)
            {
                context.MissingRequired("targetCoordinateSystem.hemisphere is required for type 'utm'.");
                ok = false;
            }
            else
            {
                hemisphere = dto.Hemisphere;
            }

            if (dto.CentralMeridian is not null)
            {
                context.ValueInvalid("targetCoordinateSystem.centralMeridian applies to type 'transverse-mercator' only.");
                ok = false;
            }
        }
        else
        {
            if (dto.Zone is not null)
            {
                context.ValueInvalid("targetCoordinateSystem.zone applies to type 'utm' only.");
                ok = false;
            }

            if (dto.Hemisphere is not null)
            {
                context.ValueInvalid("targetCoordinateSystem.hemisphere applies to type 'utm' only.");
                ok = false;
            }

            if (dto.CentralMeridian is not null)
            {
                if (!double.IsFinite(dto.CentralMeridian.Value)
                    || dto.CentralMeridian.Value <= -180
                    || dto.CentralMeridian.Value > 180)
                {
                    context.ValueInvalid(
                        $"targetCoordinateSystem.centralMeridian must be a finite value in (-180, 180] degrees; found {Format(dto.CentralMeridian.Value)}.");
                    ok = false;
                }
                else
                {
                    centralMeridian = dto.CentralMeridian;
                }
            }
        }

        return ok
            ? new TargetCoordinateSystemSpec(dto.Type.Value, zone, hemisphere, centralMeridian)
            : null;
    }

    private static void ValidateTopLevelFields(ValidationContext context, ManifestDto dto)
    {
        if (dto.MetersPerGameUnit is null)
        {
            context.MissingRequired("metersPerGameUnit is required.");
        }
        else if (!IsPositiveFinite(dto.MetersPerGameUnit.Value))
        {
            context.ValueInvalid($"metersPerGameUnit must be a positive finite number; found {Format(dto.MetersPerGameUnit.Value)}.");
        }

        RequiredText(context, dto.DefaultMaterial, "defaultMaterial");

        if (dto.MaxScaleError is not null && !IsPositiveFinite(dto.MaxScaleError.Value))
        {
            context.ValueInvalid($"maxScaleError must be a positive finite number; found {Format(dto.MaxScaleError.Value)}.");
        }

        if (dto.ExtentFeatureId is not null && string.IsNullOrWhiteSpace(dto.ExtentFeatureId))
        {
            context.ValueInvalid("extentFeatureId must be a non-empty feature id when present.");
        }
    }

    private static VerticalPolicy ValidateVerticalPolicy(ValidationContext context, VerticalPolicyDto? dto)
    {
        if (dto is null)
        {
            return new VerticalPolicy();
        }

        if (dto.OriginMeters is not null && !double.IsFinite(dto.OriginMeters.Value))
        {
            context.ValueInvalid($"verticalPolicy.originMeters must be a finite elevation; found {Format(dto.OriginMeters.Value)}.");
        }

        if (dto.Exaggeration is not null && !IsPositiveFinite(dto.Exaggeration.Value))
        {
            context.ValueInvalid($"verticalPolicy.exaggeration must be a positive finite number; found {Format(dto.Exaggeration.Value)}.");
        }

        return new VerticalPolicy(dto.OriginMeters ?? 0, dto.Exaggeration ?? 1);
    }

    private static RasterPolicy ValidateRasterPolicy(ValidationContext context, RasterPolicyDto? dto)
    {
        if (dto is null)
        {
            return new RasterPolicy();
        }

        // Supplied values must be valid even when the active policy ignores them.
        ValidateOptionalPositiveFinite(context, dto.NoDataSearchRadiusMeters, "rasterPolicy.noDataSearchRadiusMeters");
        ValidateOptionalFinite(context, dto.NoDataConstantMeters, "rasterPolicy.noDataConstantMeters");
        ValidateOptionalPositiveFinite(context, dto.EdgeSearchRadiusMeters, "rasterPolicy.edgeSearchRadiusMeters");
        ValidateOptionalFinite(context, dto.EdgeConstantMeters, "rasterPolicy.edgeConstantMeters");

        if (dto.NoData == ManifestNoDataPolicy.NearestValid && dto.NoDataSearchRadiusMeters is null)
        {
            context.MissingRequired("rasterPolicy.noDataSearchRadiusMeters is required when noData is 'nearest-valid'.");
        }

        if (dto.NoData == ManifestNoDataPolicy.Constant && dto.NoDataConstantMeters is null)
        {
            context.MissingRequired("rasterPolicy.noDataConstantMeters is required when noData is 'constant'.");
        }

        if (dto.Edge == ManifestEdgePolicy.Nearest && dto.EdgeSearchRadiusMeters is null)
        {
            context.MissingRequired("rasterPolicy.edgeSearchRadiusMeters is required when edge is 'nearest'.");
        }

        if (dto.Edge == ManifestEdgePolicy.Constant && dto.EdgeConstantMeters is null)
        {
            context.MissingRequired("rasterPolicy.edgeConstantMeters is required when edge is 'constant'.");
        }

        if (dto.ImageryPixelsPerSample is < 1)
        {
            context.ValueInvalid($"rasterPolicy.imageryPixelsPerSample must be at least 1; found {dto.ImageryPixelsPerSample}.");
        }

        return new RasterPolicy(
            dto.NoData ?? ManifestNoDataPolicy.Fail,
            dto.NoDataSearchRadiusMeters,
            dto.NoDataConstantMeters,
            dto.Edge ?? ManifestEdgePolicy.Fail,
            dto.EdgeConstantMeters,
            dto.EdgeSearchRadiusMeters,
            dto.ImageryPixelsPerSample ?? 10);
    }

    private static ToleranceSpec ValidateTolerances(ValidationContext context, ToleranceDto? dto, SourceSummary sources)
    {
        if (dto is null)
        {
            dto = new ToleranceDto();
        }
        else
        {
            ValidateOptionalPositiveFinite(context, dto.HorizontalMeters, "tolerances.horizontalMeters");
            ValidateOptionalPositiveFinite(context, dto.VerticalMeters, "tolerances.verticalMeters");
            ValidateOptionalPositiveFinite(context, dto.RelativeVerticalMeters, "tolerances.relativeVerticalMeters");
        }

        if (sources.HasElevation)
        {
            if (sources.HasUnknownElevationDatum && dto.RelativeVerticalMeters is null)
            {
                context.MissingRequired(
                    "tolerances.relativeVerticalMeters is required when an elevation source declares vertical datum 'unknown'.");
            }

            if (!sources.HasUnknownElevationDatum && !sources.HasStatedVerticalAccuracy && dto.VerticalMeters is null)
            {
                context.MissingRequired(
                    "tolerances.verticalMeters is required when no elevation source states a vertical accuracy.");
            }
        }

        return new ToleranceSpec(dto.HorizontalMeters, dto.VerticalMeters, dto.RelativeVerticalMeters);
    }

    private static List<MaterialRule> ValidateMaterialRules(
        ValidationContext context,
        IReadOnlyList<MaterialRuleDto?>? dto,
        SourceSummary sources)
    {
        var rules = new List<MaterialRule>();
        if (dto is null)
        {
            return rules;
        }

        for (var i = 0; i < dto.Count; i++)
        {
            var rule = dto[i];
            if (rule is null)
            {
                context.MissingRequired($"materialRules[{i}] is null.");
                continue;
            }

            var field = $"materialRules[{i}]";
            var material = RequiredText(context, rule.Material, $"{field}.material");
            var hasClassificationValue = rule.ClassificationValue is not null;
            var hasAttribute = rule.Attribute is not null;
            if (hasClassificationValue && hasAttribute)
            {
                context.ValueInvalid($"{field} cannot set both classificationValue and attribute.");
            }
            else if (!hasClassificationValue && !hasAttribute)
            {
                context.ValueInvalid($"{field} must set classificationValue or attribute.");
            }

            if (rule.Attribute is not null && string.IsNullOrWhiteSpace(rule.Attribute))
            {
                context.ValueInvalid($"{field}.attribute must be a non-empty attribute name.");
            }

            var sourceId = NormalizeOptional(rule.SourceId);
            if (sourceId is not null)
            {
                if (!sources.RolesById.TryGetValue(sourceId, out var role))
                {
                    context.ValueInvalid($"{field}.sourceId '{sourceId}' does not match any declared source.");
                }
                else if (hasClassificationValue && role != ManifestSourceRole.Classification)
                {
                    context.ValueInvalid($"{field}.sourceId '{sourceId}' must reference a source with role 'classification'.");
                }
                else if (!hasClassificationValue && role != ManifestSourceRole.Vector && role != ManifestSourceRole.Extent)
                {
                    context.ValueInvalid($"{field}.sourceId '{sourceId}' must reference a source with role 'vector' or 'extent'.");
                }
            }

            if (hasClassificationValue && !sources.HasClassificationSource)
            {
                context.ValueInvalid($"{field} uses classificationValue but no source with role 'classification' is declared.");
            }

            if (hasAttribute && !sources.HasVectorSource && !sources.HasExtentSource)
            {
                context.ValueInvalid($"{field} matches vector attributes but no source with role 'vector' or 'extent' is declared.");
            }

            rules.Add(new MaterialRule(
                material ?? string.Empty,
                rule.ClassificationValue,
                sourceId,
                NormalizeOptional(rule.Attribute),
                NormalizeOptional(rule.Value)));
        }

        return rules;
    }

    private static List<FeatureMappingRule> ValidateFeatureMappings(
        ValidationContext context,
        IReadOnlyList<FeatureMappingDto?>? dto,
        SourceSummary sources)
    {
        var rules = new List<FeatureMappingRule>();
        if (dto is null)
        {
            return rules;
        }

        for (var i = 0; i < dto.Count; i++)
        {
            var rule = dto[i];
            if (rule is null)
            {
                context.MissingRequired($"featureMappings[{i}] is null.");
                continue;
            }

            var field = $"featureMappings[{i}]";
            var sourceId = RequiredText(context, rule.SourceId, $"{field}.sourceId");
            if (sourceId is not null)
            {
                if (!sources.RolesById.TryGetValue(sourceId, out var role))
                {
                    context.ValueInvalid($"{field}.sourceId '{sourceId}' does not match any declared source.");
                }
                else if (role != ManifestSourceRole.Vector && role != ManifestSourceRole.Extent)
                {
                    context.ValueInvalid($"{field}.sourceId '{sourceId}' must reference a source with role 'vector' or 'extent'.");
                }
            }

            if (rule.Target is null)
            {
                context.MissingRequired($"{field}.target is required.");
            }
            else if (rule.Geometry is not null && !FeatureMappingRule.IsTargetCompatible(rule.Target.Value, rule.Geometry.Value))
            {
                context.ValueInvalid(
                    $"{field}.target '{ManifestTokens.Kebab(rule.Target.Value)}' is not valid for geometry '{ManifestTokens.Kebab(rule.Geometry.Value)}'.");
            }

            if (rule.Target == ManifestFeatureTarget.Object)
            {
                RequiredText(context, rule.ObjectDefinition, $"{field}.objectDefinition for target 'object'");
                if (rule.Orientation is null)
                {
                    context.MissingRequired($"{field}.orientation is required for target 'object'.");
                }
                else if (rule.Orientation == ManifestFeatureOrientation.Fixed)
                {
                    if (rule.OrientationDegrees is null)
                    {
                        context.MissingRequired($"{field}.orientationDegrees is required when orientation is 'fixed'.");
                    }
                    else if (!double.IsFinite(rule.OrientationDegrees.Value))
                    {
                        context.ValueInvalid($"{field}.orientationDegrees must be a finite angle in degrees.");
                    }
                }
            }
            else
            {
                if (rule.Orientation is not null)
                {
                    context.ValueInvalid($"{field}.orientation applies to target 'object' only.");
                }

                if (rule.ObjectDefinition is not null)
                {
                    context.ValueInvalid($"{field}.objectDefinition applies to target 'object' only.");
                }
            }

            if (rule.Target is not null)
            {
                ValidateTargetParameters(context, rule, field);
            }

            rules.Add(new FeatureMappingRule(
                sourceId ?? string.Empty,
                rule.Target ?? ManifestFeatureTarget.Annotation,
                rule.Geometry,
                NormalizeOptional(rule.Attribute),
                NormalizeOptional(rule.Value),
                NormalizeOptional(rule.ObjectDefinition),
                rule.Orientation,
                rule.OrientationDegrees,
                NormalizeOptional(rule.Material),
                rule.Passable,
                rule.Buildable));
        }

        return rules;
    }

    private static void ValidateTargetParameters(ValidationContext context, FeatureMappingDto rule, string field)
    {
        switch (rule.Target)
        {
            case ManifestFeatureTarget.Material:
                RequiredText(context, rule.Material, $"{field}.material for target 'material'");
                RejectSupplied(context, rule.Passable is not null, $"{field}.passable", "target 'passability'");
                RejectSupplied(context, rule.Buildable is not null, $"{field}.buildable", "target 'buildability'");
                break;
            case ManifestFeatureTarget.Passability:
                if (rule.Passable is null)
                {
                    context.MissingRequired($"{field}.passable is required for target 'passability'.");
                }

                RejectSupplied(context, rule.Material is not null, $"{field}.material", "target 'material'");
                RejectSupplied(context, rule.Buildable is not null, $"{field}.buildable", "target 'buildability'");
                break;
            case ManifestFeatureTarget.Buildability:
                if (rule.Buildable is null)
                {
                    context.MissingRequired($"{field}.buildable is required for target 'buildability'.");
                }

                RejectSupplied(context, rule.Material is not null, $"{field}.material", "target 'material'");
                RejectSupplied(context, rule.Passable is not null, $"{field}.passable", "target 'passability'");
                break;
            default:
                RejectSupplied(context, rule.Material is not null, $"{field}.material", "target 'material'");
                RejectSupplied(context, rule.Passable is not null, $"{field}.passable", "target 'passability'");
                RejectSupplied(context, rule.Buildable is not null, $"{field}.buildable", "target 'buildability'");
                break;
        }
    }

    private static void RejectSupplied(ValidationContext context, bool supplied, string field, string appliesTo)
    {
        if (supplied)
        {
            context.ValueInvalid($"{field} applies to {appliesTo} only.");
        }
    }

    private static SlopePolicy ValidateSlopePolicy(ValidationContext context, SlopePolicyDto? dto)
    {
        if (dto is null)
        {
            return new SlopePolicy();
        }

        ValidateSlopeThreshold(context, dto.ImpassableSlopeDegrees, "slopePolicy.impassableSlopeDegrees");
        ValidateSlopeThreshold(context, dto.UnbuildableSlopeDegrees, "slopePolicy.unbuildableSlopeDegrees");
        return new SlopePolicy(dto.ImpassableSlopeDegrees ?? 45, dto.UnbuildableSlopeDegrees ?? 15);
    }

    private static void ValidateSlopeThreshold(ValidationContext context, double? value, string field)
    {
        if (value is null)
        {
            return;
        }

        if (!double.IsFinite(value.Value) || value.Value <= 0 || value.Value > 90)
        {
            context.ValueInvalid($"{field} must be a finite angle in (0, 90] degrees; found {Format(value.Value)}.");
        }
    }

    private static string? RequiredText(ValidationContext context, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            context.MissingRequired($"{field} is required.");
            return null;
        }

        return value.Trim();
    }

    private static void ValidateOptionalPositiveFinite(ValidationContext context, double? value, string field, string? sourcePath = null)
    {
        if (value is null)
        {
            return;
        }

        if (!IsPositiveFinite(value.Value))
        {
            context.ValueInvalid($"{field} must be a positive finite number; found {Format(value.Value)}.", sourcePath);
        }
    }

    private static void ValidateOptionalFinite(ValidationContext context, double? value, string field)
    {
        if (value is not null && !double.IsFinite(value.Value))
        {
            context.ValueInvalid($"{field} must be a finite number; found {Format(value.Value)}.");
        }
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static string NormalizeDatum(string? datum)
    {
        var trimmed = NormalizeOptional(datum);
        if (trimmed is null || string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "unknown";
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
