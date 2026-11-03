#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Pipeline.Validation;
using Panopticon.Geospatial.Tests.Pipeline;
using Xunit;

namespace Panopticon.Geospatial.Tests.Validation;

/// <summary>
/// Fixtures for ControlPointValidatorTests over the accepted importer project fixture:
/// control points are authored at game (105, 105), an exact imagery pixel center on
/// the analytic plane (13.15 m, channels R=10 G=10 B=60), so every expected value is
/// analytic. Manifest control points and tolerances are injected into the fixture's
/// own manifest text after a control-free import committed the payload under test.
/// </summary>
internal static class ControlPointTestSupport
{
    public const double GameX = 105d;

    public const double GameY = 105d;

    public const double PlaneAtPoint = 13.15d;

    /// <summary>Control point JSON at game (105, 105); every member optional per the schema.</summary>
    public static string PointJson(
        string name,
        double? elevationMeters = PlaneAtPoint,
        double? expectedGameX = null,
        double? expectedGameY = null,
        string? featureId = null,
        string? featureSourceId = null,
        int[]? imageryRgb = null,
        int? imageryToleranceBytes = null,
        double gameX = GameX,
        double gameY = GameY)
    {
        var lonLat = ImportProjectFixture.LonLat(gameX, gameY).Split(',');
        var members = new List<string>
        {
            "\"name\": \"" + name + "\"",
            "\"latitude\": " + lonLat[1],
            "\"longitude\": " + lonLat[0],
        };

        if (elevationMeters is { } elevation)
        {
            members.Add("\"elevationMeters\": " + Format(elevation));
        }

        if (expectedGameX is { } expectedX)
        {
            members.Add("\"expectedGameX\": " + Format(expectedX));
        }

        if (expectedGameY is { } expectedY)
        {
            members.Add("\"expectedGameY\": " + Format(expectedY));
        }

        if (featureId is not null)
        {
            members.Add("\"featureId\": \"" + featureId + "\"");
        }

        if (featureSourceId is not null)
        {
            members.Add("\"featureSourceId\": \"" + featureSourceId + "\"");
        }

        if (imageryRgb is not null)
        {
            members.Add("\"imageryRgb\": [" + string.Join(", ", imageryRgb.Select(static value => value.ToString(CultureInfo.InvariantCulture))) + "]");
        }

        if (imageryToleranceBytes is { } tolerance)
        {
            members.Add("\"imageryToleranceBytes\": " + tolerance.ToString(CultureInfo.InvariantCulture));
        }

        return "{ " + string.Join(", ", members) + " }";
    }

    /// <summary>Injects control points, tolerances, per-source accuracies, DEM vertical accuracy overrides, and the unknown-DEM-datum variant.</summary>
    public static void RewriteManifest(
        ImportProjectFixture fixture,
        string? controlPoints = null,
        string? tolerances = null,
        bool demDatumUnknown = false,
        double? demHorizontalAccuracy = null,
        double? vectorHorizontalAccuracy = null,
        double? demVerticalAccuracy = null,
        bool demVerticalAccuracyOmitted = false)
    {
        var text = File.ReadAllText(fixture.ManifestPath);

        if (demDatumUnknown)
        {
            text = text.Replace(
                "\"verticalDatum\": \"EGM96\", \"verticalAccuracyMeters\": 1.5, \"horizontalAccuracyMeters\": 0.6",
                "\"verticalDatum\": \"unknown\"");
        }
        else if (demHorizontalAccuracy is { } demAccuracy)
        {
            text = text.Replace("\"horizontalAccuracyMeters\": 0.6", $"\"horizontalAccuracyMeters\": {Format(demAccuracy)}");
        }

        if (demVerticalAccuracyOmitted)
        {
            text = text.Replace(" \"verticalAccuracyMeters\": 1.5,", string.Empty);
        }
        else if (demVerticalAccuracy is { } demVertical)
        {
            text = text.Replace("\"verticalAccuracyMeters\": 1.5", $"\"verticalAccuracyMeters\": {Format(demVertical)}");
        }

        if (vectorHorizontalAccuracy is { } vectorAccuracy)
        {
            text = text.Replace("\"role\": \"vector\" }", $"\"role\": \"vector\", \"horizontalAccuracyMeters\": {Format(vectorAccuracy)} }}");
        }

        var insertions = new List<string>();
        if (tolerances is not null)
        {
            insertions.Add("\"tolerances\": " + tolerances);
        }

        if (controlPoints is not null)
        {
            insertions.Add("\"controlPoints\": [" + controlPoints + "]");
        }

        if (insertions.Count > 0)
        {
            text = text.Insert(text.IndexOf('{') + 1, "\n  " + string.Join(",\n  ", insertions) + ",");
        }

        File.WriteAllText(fixture.ManifestPath, text);
    }

    /// <summary>Commits the fixture payload without control points and opens a validated reader on it.</summary>
    public static (IGeneratedMapPayloadReader Reader, string PayloadPath) ImportAndOpen(ImportProjectFixture fixture)
    {
        var output = fixture.OutputPath();
        var result = GeospatialImporter.Run(fixture.ManifestPath, output);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

        var bag = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(output, bag);

        Assert.NotNull(reader);
        Assert.False(bag.HasErrors);

        return (reader!, output);
    }

    public static GeospatialImportManifest LoadManifest(ImportProjectFixture fixture)
    {
        var bag = new GeoDiagnosticBag();
        var manifest = ManifestLoader.Load(fixture.ManifestPath, bag);

        Assert.False(bag.HasErrors);
        Assert.NotNull(manifest);

        return manifest!;
    }

    public static IReadOnlyList<PayloadValidationCheck> Validate(
        IGeneratedMapPayloadReader reader,
        GeospatialImportManifest manifest,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        return new ControlPointValidator(limits).Validate(reader, manifest, diagnostics, cancellationToken);
    }

    public static PayloadValidationCheck Check(IReadOnlyList<PayloadValidationCheck> checks, string name)
    {
        return Assert.Single(checks, check => check.Name == name);
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>
/// Reader stub overriding only the metadata view, so validator branches that depend on
/// payload metadata (duplicate feature ids, unknown declared datum) run against an
/// otherwise fully validated staged payload.
/// </summary>
internal sealed class ReaderWithMetadata : IGeneratedMapPayloadReader
{
    private readonly IGeneratedMapPayloadReader _inner;

    public ReaderWithMetadata(IGeneratedMapPayloadReader inner, GeneratedMapPayloadMetadata metadata)
    {
        _inner = inner;
        Metadata = metadata;
    }

    public GeneratedMapPayloadMetadata Metadata { get; }

    public GeneratedMapPayloadReport Report => _inner.Report;

    public void Dispose() => _inner.Dispose();

    public TerrainChunk? ReadTerrain(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.ReadTerrain(chunkX, chunkY, diagnostics, cancellationToken);

    public MaterialChunk? ReadMaterial(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.ReadMaterial(chunkX, chunkY, diagnostics, cancellationToken);

    public TerrainMaskChunk? ReadMask(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.ReadMask(chunkX, chunkY, diagnostics, cancellationToken);

    public ImageryChunk? ReadImagery(int chunkX, int chunkY, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.ReadImagery(chunkX, chunkY, diagnostics, cancellationToken);

    public bool TrySampleHeight(double gameX, double gameY, out double gameZ, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.TrySampleHeight(gameX, gameY, out gameZ, diagnostics, cancellationToken);

    public bool TrySampleImagery(double gameX, double gameY, out RgbaValue value, GeoDiagnosticBag diagnostics, CancellationToken cancellationToken = default) =>
        _inner.TrySampleImagery(gameX, gameY, out value, diagnostics, cancellationToken);
}
