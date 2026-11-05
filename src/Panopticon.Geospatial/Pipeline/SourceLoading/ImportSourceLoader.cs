using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Readers;

namespace Panopticon.Geospatial.Pipeline.SourceLoading;

/// <summary>
/// Prepares import inputs: bounded SHA-256 hashing of the manifest and every confined
/// local source before and after reading (snapshot change detection, not proof against
/// adversarial A-B-A file swaps), real KML, KMZ, and GeoTIFF readers under the supplied
/// limits, whole-import vector feature, coordinate, and geometry-node caps, manifest-id
/// rebinding, vertical-datum resolution, and exactly-one-polygon extent selection.
/// Readers accept no cancellation token, so the token is checked before and after each
/// read and once per hash buffer. Attribute-form material rules and feature mappings
/// must name vector or extent sources; classification-value rules the classification
/// source.
/// </summary>
internal static class ImportSourceLoader
{
    private const int MaxGeometryDepth = 128;

    private const int HashBufferBytes = 64 * 1024;

    private const string SourceFileOversizedCode = "GEO-SRC-001";

    private const string ImportVectorBudgetExceededCode = "GEO-SRC-002";

    public static PreparedImportSources? Load(
        string manifestPath,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default,
        Action<string>? afterManifestHash = null,
        Action<ManifestSource, string>? afterSourceRead = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(manifestPath);
        ArgumentNullException.ThrowIfNull(diagnostics);
        limits ??= new GeospatialImportLimits();
        cancellationToken.ThrowIfCancellationRequested();

        string manifestFullPath;

        try
        {
            manifestFullPath = Path.GetFullPath(manifestPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestFailure,
                GeoStage.Manifest,
                GeoSeverity.Error,
                $"Manifest path is invalid: {ex.Message}"));

            return null;
        }

        if (!File.Exists(manifestFullPath))
        {
            // ManifestLoader owns the canonical missing-manifest diagnostic.
            ManifestLoader.Load(manifestFullPath, diagnostics, limits);

            return null;
        }

        var manifestHashBefore = HashManifest(manifestFullPath, limits.MaxManifestBytes, diagnostics, cancellationToken);

        if (manifestHashBefore is null)
        {
            return null;
        }

        afterManifestHash?.Invoke(manifestFullPath);
        cancellationToken.ThrowIfCancellationRequested();

        var manifest = ManifestLoader.Load(manifestFullPath, diagnostics, limits);

        if (manifest is null)
        {
            return null;
        }

        var manifestHashAfter = HashManifest(manifestFullPath, limits.MaxManifestBytes, diagnostics, cancellationToken);

        if (manifestHashAfter is null)
        {
            return null;
        }

        if (manifestHashAfter != manifestHashBefore)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestFailure,
                GeoStage.Manifest,
                GeoSeverity.Error,
                "The manifest changed while it was being loaded.",
                manifestFullPath));

            return null;
        }

        var manifestDirectory = Path.GetDirectoryName(manifestFullPath) ?? Directory.GetCurrentDirectory();

        return LoadSources(
            manifest,
            manifestFullPath,
            manifestDirectory,
            manifestHashBefore,
            diagnostics,
            limits,
            cancellationToken,
            afterSourceRead);
    }

    private static PreparedImportSources? LoadSources(
        GeospatialImportManifest manifest,
        string manifestFullPath,
        string manifestDirectory,
        string manifestSha256,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken,
        Action<ManifestSource, string>? afterSourceRead)
    {
        var sources = new List<LoadedSource>(manifest.Sources.Count);
        var vectorFeatures = new List<GeospatialFeature>();
        var protectedPaths = new List<string> { manifestFullPath };
        var budget = VectorBudget.From(limits);
        LoadedRaster? primaryElevation = null;
        LoadedRaster? imagery = null;
        LoadedRaster? classification = null;
        IReadOnlyList<GeospatialFeature>? extentFeatures = null;
        string? knownDatum = null;

        foreach (var source in manifest.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Validated at load time; revalidated at use so a link swapped in between
            // cannot escape the manifest directory.
            var rejection = ManifestPathGuard.Validate(manifestDirectory, source.Path);

            if (rejection is not null)
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestSourcePathRejected,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    rejection,
                    source.Path));

                return null;
            }

            var fullPath = Path.GetFullPath(Path.Combine(manifestDirectory, source.Path));
            protectedPaths.Add(fullPath);

            if (!File.Exists(fullPath))
            {
                AddSourceError(
                    diagnostics,
                    source,
                    $"Source '{source.Id}' was not found at '{source.Path}'; dangling links are reported the same way.");

                return null;
            }

            var hashBefore = HashSource(source, fullPath, SourceFileCap(source.Kind, limits), diagnostics, cancellationToken);

            if (hashBefore.Hash is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<GeospatialFeature>? features = null;
            GeospatialRaster<double>? raster = null;

            switch (source.Kind)
            {
                case ManifestSourceKind.Kml:
                    features = new KmlReader().Read(fullPath, limits, diagnostics);
                    break;
                case ManifestSourceKind.Kmz:
                    features = new KmzArchiveReader().Read(fullPath, limits, diagnostics);
                    break;
                case ManifestSourceKind.Geotiff:
                    raster = new GeoTiffRasterReader().Read(fullPath, limits, diagnostics);
                    break;
            }

            if (diagnostics.HasErrors)
            {
                return null;
            }

            afterSourceRead?.Invoke(source, fullPath);
            cancellationToken.ThrowIfCancellationRequested();

            // The per-call seam may have swapped the entry; revalidate so the final hash
            // cannot read content outside the manifest directory.
            rejection = ManifestPathGuard.Validate(manifestDirectory, source.Path);

            if (rejection is not null)
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestSourcePathRejected,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    rejection,
                    source.Path));

                return null;
            }

            var hashAfter = HashSource(source, fullPath, SourceFileCap(source.Kind, limits), diagnostics, cancellationToken);

            if (hashAfter.Hash is null || hashAfter.Hash != hashBefore.Hash)
            {
                if (hashAfter.Hash is not null)
                {
                    AddSourceError(
                        diagnostics,
                        source,
                        $"Source '{source.Id}' changed while it was being read; the provenance hash no longer covers the parsed bytes.");
                }

                return null;
            }

            var declaredDatum = IsUnknownDatum(source.VerticalDatum) ? "unknown" : source.VerticalDatum!;
            var embeddedDatum = raster is null || IsUnknownDatum(raster.VerticalDatum) ? null : raster.VerticalDatum;

            if (declaredDatum != "unknown" && embeddedDatum is not null
                && !string.Equals(declaredDatum, embeddedDatum, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.CoordinatesFailure,
                    GeoStage.Coordinates,
                    GeoSeverity.Error,
                    $"Source '{source.Id}' declares vertical datum '{declaredDatum}' but the file embeds '{embeddedDatum}'.",
                    source.Path));

                return null;
            }

            var effectiveDatum = declaredDatum != "unknown" ? declaredDatum : embeddedDatum ?? "unknown";

            if (effectiveDatum != "unknown")
            {
                if (knownDatum is null)
                {
                    knownDatum = effectiveDatum;
                }
                else if (!string.Equals(knownDatum, effectiveDatum, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new GeoDiagnostic(
                        GeoDiagnosticCodes.CoordinatesFailure,
                        GeoStage.Coordinates,
                        GeoSeverity.Error,
                        $"The import mixes known vertical datums '{knownDatum}' and '{effectiveDatum}'; no geoid conversion runs.",
                        source.Path));

                    return null;
                }
            }

            var loaded = new LoadedSource(
                source.Id,
                source.Kind,
                source.Role,
                source.Path,
                fullPath,
                hashBefore.Hash,
                hashBefore.LengthBytes,
                raster?.CoordinateSystem ?? CoordinateSystemId.Wgs84,
                declaredDatum,
                embeddedDatum,
                effectiveDatum,
                source.Label,
                source.Attribution,
                source.HorizontalAccuracyMeters,
                source.VerticalAccuracyMeters);
            sources.Add(loaded);

            if (source.Role is ManifestSourceRole.Vector or ManifestSourceRole.Extent)
            {
                if (features is null)
                {
                    AddSourceError(diagnostics, source, $"Source '{source.Id}' produced no vector features.");

                    return null;
                }

                // Whole-import caps are charged before rebinding so aggregate growth across
                // many individually valid sources cannot outrun the per-file reader caps.
                if (!TryChargeVectorBudget(source, features, budget, cancellationToken, diagnostics))
                {
                    return null;
                }

                var rebound = Rebind(features, source.Id, cancellationToken);
                vectorFeatures.AddRange(rebound);

                if (source.Role == ManifestSourceRole.Extent)
                {
                    extentFeatures = rebound;
                }
            }
            else if (raster is null)
            {
                AddSourceError(diagnostics, source, $"Source '{source.Id}' produced no raster.");

                return null;
            }
            else if (source.Role == ManifestSourceRole.Elevation)
            {
                primaryElevation = new LoadedRaster(loaded, raster);
            }
            else if (source.Role == ManifestSourceRole.Imagery)
            {
                imagery = new LoadedRaster(loaded, raster);
            }
            else if (source.Role == ManifestSourceRole.Classification)
            {
                classification = new LoadedRaster(loaded, raster);
            }
        }

        if (primaryElevation is null)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestMissingRequired,
                GeoStage.Manifest,
                GeoSeverity.Error,
                "No elevation source was loaded.",
                manifestFullPath));

            return null;
        }

        if (!ValidateRules(manifest, classification, cancellationToken, diagnostics))
        {
            return null;
        }

        var selectedExtent = SelectExtent(manifest, extentFeatures, budget, cancellationToken, diagnostics);

        if (selectedExtent is null)
        {
            return null;
        }

        return new PreparedImportSources(
            manifest,
            manifestSha256,
            protectedPaths,
            sources,
            vectorFeatures,
            selectedExtent,
            primaryElevation,
            imagery,
            classification);
    }

    private static bool ValidateRules(
        GeospatialImportManifest manifest,
        LoadedRaster? classification,
        CancellationToken cancellationToken,
        GeoDiagnosticBag diagnostics)
    {
        foreach (var rule in manifest.MaterialRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule.ClassificationValue is null)
            {
                // Attribute-form rules select vector features; a null source id stays a
                // wildcard. A raster role id is contradictory, so it fails.
                if (rule.SourceId is not null && !NamesVectorSource(manifest, rule.SourceId, out var role))
                {
                    AddRuleSourceError(diagnostics, "Material", rule.Material, rule.SourceId!, role);

                    return false;
                }

                continue;
            }

            if (classification is null)
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestMissingRequired,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    $"Material rule '{rule.Material}' uses classificationValue but no classification raster was loaded; the rule would be silently unused."));

                return false;
            }

            if (rule.SourceId is not null && !string.Equals(rule.SourceId, classification.Source.SourceId, StringComparison.Ordinal))
            {
                diagnostics.Add(new GeoDiagnostic(
                    GeoDiagnosticCodes.ManifestValueInvalid,
                    GeoStage.Manifest,
                    GeoSeverity.Error,
                    $"Material rule '{rule.Material}' names source '{rule.SourceId}' but the classification source is '{classification.Source.SourceId}'."));

                return false;
            }
        }

        foreach (var mapping in manifest.FeatureMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (mapping.SourceId is not null && !NamesVectorSource(manifest, mapping.SourceId, out var role))
            {
                AddRuleSourceError(diagnostics, "Feature mapping", ManifestTokens.Kebab(mapping.Target), mapping.SourceId, role);

                return false;
            }
        }

        return true;
    }

    private static bool NamesVectorSource(GeospatialImportManifest manifest, string sourceId, out ManifestSourceRole? role)
    {
        role = null;

        foreach (var source in manifest.Sources)
        {
            if (string.Equals(source.Id, sourceId, StringComparison.Ordinal))
            {
                role = source.Role;

                break;
            }
        }

        return role is ManifestSourceRole.Vector or ManifestSourceRole.Extent;
    }

    private static void AddRuleSourceError(
        GeoDiagnosticBag diagnostics,
        string ruleKind,
        string ruleName,
        string sourceId,
        ManifestSourceRole? role)
    {
        var message = role is null
            ? $"{ruleKind} rule '{ruleName}' names source '{sourceId}', which no declared source uses."
            : $"{ruleKind} rule '{ruleName}' names source '{sourceId}' with role '{ManifestTokens.Kebab(role.Value)}', but these rules select vector or extent features only.";

        diagnostics.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ManifestValueInvalid,
            GeoStage.Manifest,
            GeoSeverity.Error,
            message));
    }

    private static bool TryChargeVectorBudget(
        ManifestSource source,
        IReadOnlyList<GeospatialFeature> features,
        VectorBudget budget,
        CancellationToken cancellationToken,
        GeoDiagnosticBag diagnostics)
    {
        long projectedFeatures;

        try
        {
            projectedFeatures = checked(budget.Features + features.Count);
        }
        catch (OverflowException)
        {
            AddBudgetError(diagnostics, source, $"Source '{source.Id}' overflows the whole-import vector feature count.");

            return false;
        }

        if (projectedFeatures > budget.MaxFeatures)
        {
            AddBudgetError(
                diagnostics,
                source,
                $"The import would hold {Format(projectedFeatures)} vector features over the whole-import limit of {Format(budget.MaxFeatures)}.");

            return false;
        }

        var coordinates = budget.Coordinates;

        try
        {
            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (CountGeometry(feature.Geometry, budget, ref coordinates, cancellationToken))
                {
                    case GeometryWalkStatus.Complete:
                        break;
                    case GeometryWalkStatus.DepthExceeded:
                        AddBudgetError(
                            diagnostics,
                            source,
                            $"Feature '{feature.FeatureId}' nests geometry deeper than {Format(MaxGeometryDepth)} levels.");

                        return false;
                    case GeometryWalkStatus.NodeBudgetExceeded:
                        AddBudgetError(
                            diagnostics,
                            source,
                            $"Source '{source.Id}' exceeds the whole-import geometry work budget of {Format(budget.InitialNodeBudget)} nodes.");

                        return false;
                    case GeometryWalkStatus.CoordinateCapExceeded:
                        AddBudgetError(
                            diagnostics,
                            source,
                            $"The import would hold {Format(coordinates)} vector coordinates over the whole-import limit of {Format(budget.MaxCoordinates)}.");

                        return false;
                }
            }
        }
        catch (OverflowException)
        {
            AddBudgetError(diagnostics, source, $"Source '{source.Id}' overflows the whole-import vector coordinate count.");

            return false;
        }

        budget.Features = projectedFeatures;
        budget.Coordinates = coordinates;

        return true;
    }

    private static GeometryWalkStatus CountGeometry(GeoGeometry root, VectorBudget budget, ref long coordinates, CancellationToken cancellationToken)
    {
        var pending = new Stack<(GeoGeometry Geometry, int Depth)>();
        pending.Push((root, 1));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!budget.TryChargeNode())
            {
                return GeometryWalkStatus.NodeBudgetExceeded;
            }

            var (current, depth) = pending.Pop();

            switch (current)
            {
                case GeoPointGeometry:
                    coordinates = checked(coordinates + 1);
                    break;
                case GeoLineString line:
                    coordinates = checked(coordinates + line.Positions.Count);
                    break;
                case GeoPolygon polygon:
                    coordinates = checked(coordinates + polygon.OuterRing.Positions.Count);

                    if (coordinates > budget.MaxCoordinates)
                    {
                        return GeometryWalkStatus.CoordinateCapExceeded;
                    }

                    foreach (var ring in polygon.InnerRings)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!budget.TryChargeNode())
                        {
                            return GeometryWalkStatus.NodeBudgetExceeded;
                        }

                        coordinates = checked(coordinates + ring.Positions.Count);

                        if (coordinates > budget.MaxCoordinates)
                        {
                            return GeometryWalkStatus.CoordinateCapExceeded;
                        }
                    }

                    break;
                case GeoMultiGeometry multi when depth >= MaxGeometryDepth:
                    return GeometryWalkStatus.DepthExceeded;
                case GeoMultiGeometry multi:
                    // Reserve one node per child plus one per pending sibling, so the
                    // stack never grows past what the budget can still visit.
                    if ((long)multi.Geometries.Count + pending.Count > budget.NodeBudgetRemaining)
                    {
                        return GeometryWalkStatus.NodeBudgetExceeded;
                    }

                    for (var i = multi.Geometries.Count - 1; i >= 0; i--)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pending.Push((multi.Geometries[i], depth + 1));
                    }

                    break;
            }

            if (coordinates > budget.MaxCoordinates)
            {
                return GeometryWalkStatus.CoordinateCapExceeded;
            }
        }

        return GeometryWalkStatus.Complete;
    }

    private static SelectedExtentFeature? SelectExtent(
        GeospatialImportManifest manifest,
        IReadOnlyList<GeospatialFeature>? extentFeatures,
        VectorBudget budget,
        CancellationToken cancellationToken,
        GeoDiagnosticBag diagnostics)
    {
        var features = extentFeatures ?? [];

        if (manifest.ExtentFeatureId is { } featureId)
        {
            GeospatialFeature? match = null;
            var matches = 0;

            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(feature.FeatureId, featureId, StringComparison.Ordinal))
                {
                    match = feature;
                    matches++;
                }
            }

            if (matches == 0)
            {
                AddExtentError(diagnostics, $"extentFeatureId '{featureId}' matched no feature in the extent source.");

                return null;
            }

            if (matches > 1)
            {
                AddExtentError(
                    diagnostics,
                    $"extentFeatureId '{featureId}' matched {Format(matches)} features in the extent source.");

                return null;
            }

            var polygons = new List<GeoPolygon>();

            switch (CollectPolygons(match!.Geometry, budget, polygons, cancellationToken))
            {
                case GeometryWalkStatus.DepthExceeded:
                    AddExtentError(
                        diagnostics,
                        $"Extent feature '{match.FeatureId}' nests geometry deeper than {Format(MaxGeometryDepth)} levels.");

                    return null;
                case GeometryWalkStatus.NodeBudgetExceeded:
                    AddExtentError(
                        diagnostics,
                        $"The extent source exceeds the whole-import geometry work budget of {Format(budget.InitialNodeBudget)} nodes.");

                    return null;
            }

            if (polygons.Count != 1)
            {
                AddExtentError(
                    diagnostics,
                    $"Extent feature '{match.FeatureId}' must contain exactly one polygon; found {DescribeCount(polygons.Count)}.");

                return null;
            }

            return new SelectedExtentFeature(match, polygons[0]);
        }

        GeospatialFeature? owner = null;
        GeoPolygon? found = null;
        var polygonsOfFeature = new List<GeoPolygon>();
        var total = 0;

        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            polygonsOfFeature.Clear();

            switch (CollectPolygons(feature.Geometry, budget, polygonsOfFeature, cancellationToken))
            {
                case GeometryWalkStatus.DepthExceeded:
                    AddExtentError(
                        diagnostics,
                        $"Extent feature '{feature.FeatureId}' nests geometry deeper than {Format(MaxGeometryDepth)} levels.");

                    return null;
                case GeometryWalkStatus.NodeBudgetExceeded:
                    AddExtentError(
                        diagnostics,
                        $"The extent source exceeds the whole-import geometry work budget of {Format(budget.InitialNodeBudget)} nodes.");

                    return null;
            }

            if (polygonsOfFeature.Count > 0)
            {
                owner = feature;
                found = polygonsOfFeature[0];
                total = Math.Min(checked(total + polygonsOfFeature.Count), 2);

                if (total >= 2)
                {
                    break;
                }
            }
        }

        if (total != 1)
        {
            // No automatic largest-polygon selection: exactly one polygon, or the operator
            // disambiguates with extentFeatureId.
            AddExtentError(
                diagnostics,
                $"The extent source must contain exactly one polygon; found {DescribeCount(total)}.");

            return null;
        }

        return new SelectedExtentFeature(owner!, found!);
    }

    private static GeometryWalkStatus CollectPolygons(
        GeoGeometry root,
        VectorBudget budget,
        List<GeoPolygon> polygons,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<(GeoGeometry Geometry, int Depth)>();
        pending.Push((root, 1));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!budget.TryChargeNode())
            {
                return GeometryWalkStatus.NodeBudgetExceeded;
            }

            var (current, depth) = pending.Pop();

            switch (current)
            {
                case GeoPolygon polygon:
                    polygons.Add(polygon);

                    if (polygons.Count >= 2)
                    {
                        // Two polygons already prove ambiguity; stop collecting.
                        return GeometryWalkStatus.Complete;
                    }

                    break;
                case GeoMultiGeometry multi when depth >= MaxGeometryDepth:
                    return GeometryWalkStatus.DepthExceeded;
                case GeoMultiGeometry multi:
                    // Same pending-aware reserve as the counting walk.
                    if ((long)multi.Geometries.Count + pending.Count > budget.NodeBudgetRemaining)
                    {
                        return GeometryWalkStatus.NodeBudgetExceeded;
                    }

                    for (var i = multi.Geometries.Count - 1; i >= 0; i--)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pending.Push((multi.Geometries[i], depth + 1));
                    }

                    break;
            }
        }

        return GeometryWalkStatus.Complete;
    }

    private static List<GeospatialFeature> Rebind(IReadOnlyList<GeospatialFeature> features, string sourceId, CancellationToken cancellationToken)
    {
        var rebound = new List<GeospatialFeature>(features.Count);

        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            rebound.Add(feature.SourceId == sourceId
                ? feature
                : new GeospatialFeature(
                    sourceId,
                    feature.FeatureId,
                    feature.Geometry,
                    feature.CoordinateSystem,
                    feature.Attributes,
                    feature.AltitudeMode));
        }

        return rebound;
    }

    private static string? HashManifest(
        string path,
        long capBytes,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var result = HashFileBounded(path, capBytes, cancellationToken);

        if (result.Oversized)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestOversized,
                GeoStage.Manifest,
                GeoSeverity.Error,
                $"Manifest is {Format(result.LengthBytes)} bytes, over the limit of {Format(capBytes)} bytes.",
                path));

            return null;
        }

        if (result.IoFailure is { } failure)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ManifestFailure,
                GeoStage.Manifest,
                GeoSeverity.Error,
                $"Manifest file could not be read: {failure.Message}",
                path,
                failure));

            return null;
        }

        return result.Hash;
    }

    private static FileHash HashSource(
        ManifestSource source,
        string fullPath,
        long capBytes,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var result = HashFileBounded(fullPath, capBytes, cancellationToken);

        if (result.Oversized)
        {
            AddSourceError(
                diagnostics,
                source,
                $"Source '{source.Id}' is {Format(result.LengthBytes)} bytes, over the source file limit of {Format(capBytes)} bytes.",
                SourceFileOversizedCode);

            return default;
        }

        if (result.IoFailure is { } failure)
        {
            AddSourceError(diagnostics, source, $"Source '{source.Id}' could not be read: {failure.Message}", detail: failure);

            return default;
        }

        return result;
    }

    private static FileHash HashFileBounded(string path, long capBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, HashBufferBytes);

            if (stream.CanSeek && stream.Length > capBytes)
            {
                return new FileHash(null, stream.Length, true, null);
            }

            var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[HashBufferBytes];
            long lengthBytes = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var read = stream.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                {
                    break;
                }

                lengthBytes += read;

                if (lengthBytes > capBytes)
                {
                    return new FileHash(null, lengthBytes, true, null);
                }

                hash.AppendData(buffer, 0, read);
            }

            return new FileHash(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), lengthBytes, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileHash(null, 0, false, ex);
        }
    }

    private static void AddSourceError(
        GeoDiagnosticBag diagnostics,
        ManifestSource source,
        string message,
        string? code = null,
        Exception? detail = null)
    {
        var kindCode = source.Kind switch
        {
            ManifestSourceKind.Kml => GeoDiagnosticCodes.ParseFailure,
            ManifestSourceKind.Kmz => GeoDiagnosticCodes.ArchiveFailure,
            _ => GeoDiagnosticCodes.ParseFailure,
        };
        var stage = source.Kind == ManifestSourceKind.Kmz ? GeoStage.Archive : GeoStage.Parse;
        diagnostics.Add(new GeoDiagnostic(code ?? kindCode, stage, GeoSeverity.Error, message, source.Path, detail));
    }

    private static void AddBudgetError(GeoDiagnosticBag diagnostics, ManifestSource source, string message)
    {
        diagnostics.Add(new GeoDiagnostic(
            ImportVectorBudgetExceededCode,
            GeoStage.Parse,
            GeoSeverity.Error,
            message,
            source.Path));
    }

    private static void AddExtentError(GeoDiagnosticBag diagnostics, string message)
    {
        diagnostics.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.CoordinatesInvalidExtent,
            GeoStage.Coordinates,
            GeoSeverity.Error,
            message));
    }

    private static string DescribeCount(int count) => count == 0 ? "0" : "at least 2";

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsUnknownDatum(string? datum) =>
        string.IsNullOrWhiteSpace(datum)
        || string.Equals(datum.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);

    private static long SourceFileCap(ManifestSourceKind kind, GeospatialImportLimits limits) =>
        kind == ManifestSourceKind.Kml
            ? Math.Min(limits.MaxKmlEntryBytes, limits.MaxSourceFileBytes)
            : limits.MaxSourceFileBytes;

    internal enum GeometryWalkStatus
    {
        Complete,

        DepthExceeded,

        NodeBudgetExceeded,

        CoordinateCapExceeded,
    }

    /// <summary>Test seam: runs the bounded counting walk directly so wide-geometry budget failures can be exercised without building oversized source files.</summary>
    internal static GeometryWalkStatus CountGeometryForTest(
        GeoGeometry root,
        long maxCoordinates,
        long nodeBudget,
        ref long coordinates,
        CancellationToken cancellationToken)
    {
        var budget = new VectorBudget(long.MaxValue, maxCoordinates, nodeBudget);

        return CountGeometry(root, budget, ref coordinates, cancellationToken);
    }

    /// <summary>Test seam: runs the bounded extent polygon walk directly with the given node budget.</summary>
    internal static GeometryWalkStatus CollectPolygonsForTest(
        GeoGeometry root,
        long nodeBudget,
        List<GeoPolygon> polygons,
        CancellationToken cancellationToken)
    {
        var budget = new VectorBudget(long.MaxValue, long.MaxValue, nodeBudget);

        return CollectPolygons(root, budget, polygons, cancellationToken);
    }

    private readonly record struct FileHash(string? Hash, long LengthBytes, bool Oversized, Exception? IoFailure);

    private sealed class VectorBudget
    {
        public VectorBudget(long maxFeatures, long maxCoordinates, long nodeBudget)
        {
            MaxFeatures = maxFeatures;
            MaxCoordinates = maxCoordinates;
            InitialNodeBudget = nodeBudget;
            NodeBudgetRemaining = nodeBudget;
        }

        public long MaxFeatures { get; }

        public long MaxCoordinates { get; }

        public long InitialNodeBudget { get; }

        public long NodeBudgetRemaining { get; private set; }

        public long Features { get; set; }

        public long Coordinates { get; set; }

        public static VectorBudget From(GeospatialImportLimits limits)
        {
            long nodeBudget;

            try
            {
                nodeBudget = checked((long)limits.MaxFeatureCount + limits.MaxCoordinateCount);
            }
            catch (OverflowException)
            {
                nodeBudget = long.MaxValue;
            }

            return new VectorBudget(limits.MaxFeatureCount, limits.MaxCoordinateCount, nodeBudget);
        }

        public bool TryChargeNode()
        {
            NodeBudgetRemaining--;

            return NodeBudgetRemaining >= 0;
        }
    }
}
