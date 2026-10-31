using System;
using Xunit;

namespace Panopticon.Geospatial.Tests;

public class GeoDiagnosticBagTests
{
    [Fact]
    public void EmptyBagHasNoErrors()
    {
        var bag = new GeoDiagnosticBag();

        Assert.False(bag.HasErrors);
        Assert.Empty(bag.Items);
    }

    [Fact]
    public void ItemsPreserveInsertionOrder()
    {
        var bag = new GeoDiagnosticBag();
        var first = new GeoDiagnostic(
            GeoDiagnosticCodes.ManifestFailure, GeoStage.Manifest, GeoSeverity.Warning, "first");
        var second = new GeoDiagnostic(
            GeoDiagnosticCodes.ParseFailure, GeoStage.Parse, GeoSeverity.Error, "second");
        var third = new GeoDiagnostic(
            GeoDiagnosticCodes.ValidateFailure, GeoStage.Validate, GeoSeverity.Info, "third");

        bag.Add(first);
        bag.Add(second);
        bag.Add(third);

        Assert.Equal([first, second, third], bag.Items);
    }

    [Fact]
    public void HasErrorsIsFalseWithOnlyInfoAndWarnings()
    {
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ManifestFailure, GeoStage.Manifest, GeoSeverity.Info, "info"));
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ParseFailure, GeoStage.Parse, GeoSeverity.Warning, "warning"));

        Assert.False(bag.HasErrors);
    }

    [Fact]
    public void HasErrorsIsTrueWhenAnyDiagnosticIsError()
    {
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ManifestFailure, GeoStage.Manifest, GeoSeverity.Info, "info"));
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.CoordinatesFailure, GeoStage.Coordinates, GeoSeverity.Warning, "warning"));

        Assert.False(bag.HasErrors);

        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.RasterFailure, GeoStage.Raster, GeoSeverity.Error, "error"));

        Assert.True(bag.HasErrors);
    }

    [Fact]
    public void HasErrorsStaysTrueAfterLaterNonErrorDiagnostics()
    {
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ArchiveFailure, GeoStage.Archive, GeoSeverity.Error, "error"));
        bag.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.GenerateFailure, GeoStage.Generate, GeoSeverity.Info, "info"));

        Assert.True(bag.HasErrors);
    }

    [Fact]
    public void AddRejectsNullDiagnostic()
    {
        var bag = new GeoDiagnosticBag();

        Assert.Throws<ArgumentNullException>(() => bag.Add(null!));
    }

    [Fact]
    public void DiagnosticsCarryOptionalSourcePathAndDetail()
    {
        var detail = new InvalidOperationException("boom");
        var diagnostic = new GeoDiagnostic(
            GeoDiagnosticCodes.WriteFailure,
            GeoStage.Write,
            GeoSeverity.Error,
            "write failed",
            "payload/terrain/0_0.bin",
            detail);

        var bag = new GeoDiagnosticBag();
        bag.Add(diagnostic);

        var item = Assert.Single(bag.Items);
        Assert.Equal("payload/terrain/0_0.bin", item.SourcePath);
        Assert.Same(detail, item.Detail);
        Assert.Equal(GeoDiagnosticCodes.WriteFailure, item.Code);
        Assert.Equal(GeoStage.Write, item.Stage);
        Assert.Equal(GeoSeverity.Error, item.Severity);
    }

    [Fact]
    public void ConstructorRejectsEmptyCodeAndMessage()
    {
        Assert.Throws<ArgumentException>(() => new GeoDiagnostic(
            "", GeoStage.Manifest, GeoSeverity.Error, "message"));
        Assert.Throws<ArgumentException>(() => new GeoDiagnostic(
            GeoDiagnosticCodes.ManifestFailure, GeoStage.Manifest, GeoSeverity.Error, ""));
    }
}
