using System;
using System.Collections.Generic;
using Panopticon.Geospatial;
using Xunit;

namespace Panopticon.Geospatial.Preview.Tests;

/// <summary>
/// <see cref="PreviewTextureMap"/>: strict bare JSON object bindings, Ordinal palette
/// keys, case-insensitive catalog value canonicalization, and the used/extra distinction
/// of B4b group 5 decision 3.
/// </summary>
public sealed class PreviewTextureMapTests
{
    [Fact]
    public void FromJson_ValidObject_BindsEveryPaletteName()
    {
        var textureMap = PreviewPayloadFixture.DefaultTextureMap();

        Assert.Equal(4, textureMap.Count);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap.TryResolve("grass", out var grass));
        Assert.Equal("GrassType1", grass);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap.TryResolve("spare", out var spare));
        Assert.Equal("SandType1", spare);
    }

    [Fact]
    public void FromJson_DuplicateKey_RejectsWithTypedDiagnostic()
    {
        var diagnostics = new GeoDiagnosticBag();
        var textureMap = PreviewTextureMap.FromJson(
            """
            {
              "grass": "GrassType1",
              "grass": "DirtMediumType1"
            }
            """,
            new PreviewPayloadFixture.FakeCatalog(),
            diagnostics);

        Assert.Null(textureMap);
        var diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(PreviewDiagnosticCodes.TextureMapDuplicateKey, diagnostic.Code);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("""["grass"]""")]
    [InlineData(""""grass"""")]
    [InlineData("""{"grass": 7}""")]
    [InlineData("""{"grass": true}""")]
    [InlineData("""{"grass": null}""")]
    [InlineData("""{"grass": ["GrassType1"]}""")]
    [InlineData("""{"": "GrassType1"}""")]
    [InlineData("""{"grass": ""}""")]
    [InlineData("""{"grass": "GrassType1"}extra""")]
    public void FromJson_NonObjectRootOrNonStringValue_Rejects(string json)
    {
        var diagnostics = new GeoDiagnosticBag();
        var textureMap = PreviewTextureMap.FromJson(json, new PreviewPayloadFixture.FakeCatalog(), diagnostics);

        Assert.Null(textureMap);
        Assert.Contains(diagnostics.Items, diagnostic => diagnostic.Code == PreviewDiagnosticCodes.TextureMapJsonInvalid);
    }

    [Fact]
    public void FromJson_MalformedJson_RejectsWithDetail()
    {
        var diagnostics = new GeoDiagnosticBag();
        var textureMap = PreviewTextureMap.FromJson("{ not json", new PreviewPayloadFixture.FakeCatalog(), diagnostics);

        Assert.Null(textureMap);
        Assert.Contains(diagnostics.Items, diagnostic => diagnostic.Code == PreviewDiagnosticCodes.TextureMapJsonInvalid);
        Assert.NotNull(Assert.Single(diagnostics.Items).Detail);
    }

    [Fact]
    public void CatalogValues_CanonicalizeCaseInsensitively_ToActualBlockNames()
    {
        var textureMap = PreviewTextureMap.FromJson(
            """{"grass": "grasstype1", "dirt": "DIRTMEDIUMTYPE1", "rock": "RockType1"}""",
            new PreviewPayloadFixture.FakeCatalog(),
            new GeoDiagnosticBag());

        Assert.NotNull(textureMap);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap!.TryResolve("grass", out var grass));
        Assert.Equal("GrassType1", grass);
        Assert.Equal(PreviewTextureResolution.Resolved, textureMap.TryResolve("dirt", out var dirt));
        Assert.Equal("DirtMediumType1", dirt);
    }

    [Fact]
    public void PaletteKeys_CompareOrdinal()
    {
        var textureMap = PreviewTextureMap.FromJson("""{"grass": "GrassType1"}""", new PreviewPayloadFixture.FakeCatalog(), new GeoDiagnosticBag());

        Assert.NotNull(textureMap);
        Assert.Equal(PreviewTextureResolution.MissingBinding, textureMap!.TryResolve("Grass", out _));
        Assert.Equal(PreviewTextureResolution.MissingBinding, textureMap.TryResolve("GRASS", out _));
    }

    [Fact]
    public void UnknownValues_FailOnlyWhenUsed()
    {
        var textureMap = PreviewTextureMap.FromJson(
            """{"grass": "NotInCatalog", "dirt": "DirtMediumType1", "rock": "RockType1", "spare": "AlsoNotInCatalog"}""",
            new PreviewPayloadFixture.FakeCatalog(),
            new GeoDiagnosticBag());

        Assert.NotNull(textureMap);
        Assert.Equal(PreviewTextureResolution.UnknownTextureName, textureMap!.TryResolve("grass", out var grass));
        Assert.Null(grass);

        // Extra entries serve other payloads; their unknown values stay tolerated.
        Assert.Equal(PreviewTextureResolution.UnknownTextureName, textureMap.TryResolve("spare", out _));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        var catalog = new PreviewPayloadFixture.FakeCatalog();

        Assert.Throws<ArgumentNullException>(() => new PreviewTextureMap(null!, catalog));
        Assert.Throws<ArgumentNullException>(() => new PreviewTextureMap(new Dictionary<string, string>(), null!));

        Assert.Throws<ArgumentException>(() => new PreviewTextureMap(
            new Dictionary<string, string> { ["grass"] = "" },
            catalog));
        Assert.Throws<ArgumentException>(() => new PreviewTextureMap(
            new Dictionary<string, string> { [" "] = "GrassType1" },
            catalog));
    }

    [Fact]
    public void FromJson_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PreviewTextureMap.FromJson("{}", null!, new GeoDiagnosticBag()));
        Assert.Throws<ArgumentNullException>(() => PreviewTextureMap.FromJson("{}", new PreviewPayloadFixture.FakeCatalog(), null!));
        Assert.Throws<ArgumentException>(() => PreviewTextureMap.FromJson("", new PreviewPayloadFixture.FakeCatalog(), new GeoDiagnosticBag()));
    }
}
