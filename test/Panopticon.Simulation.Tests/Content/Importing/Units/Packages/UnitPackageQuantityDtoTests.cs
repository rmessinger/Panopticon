#nullable enable

using System.Text.Json;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageQuantityDtoTests
{
    [Fact]
    public void TryParse_ValidNumericValue_Succeeds()
    {
        using var doc = JsonDocument.Parse("""{"value": 180.0, "unit": "HitPoints"}""");
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.True(success);
        Assert.Null(reason);
        Assert.NotNull(quantity);
        Assert.Equal(180.0, quantity.Value.Value);
        Assert.Equal(QuantityUnit.HitPoints, quantity.Value.Unit);
    }

    [Fact]
    public void TryParse_ValidNumericStringValue_Succeeds()
    {
        using var doc = JsonDocument.Parse("""{"value": "180.0", "unit": "HitPoints"}""");
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.True(success);
        Assert.Null(reason);
        Assert.NotNull(quantity);
        Assert.Equal(180.0, quantity.Value.Value);
        Assert.Equal(QuantityUnit.HitPoints, quantity.Value.Unit);
    }

    [Theory]
    [InlineData("""{"value": {"value": 1, "unit": "1"}, "unit": "HitPoints"}""", "Object")]
    [InlineData("""{"value": [1, 2, 3], "unit": "HitPoints"}""", "Array")]
    [InlineData("""{"value": true, "unit": "HitPoints"}""", "True")]
    [InlineData("""{"value": false, "unit": "HitPoints"}""", "False")]
    [InlineData("""{"value": null, "unit": "HitPoints"}""", "Null")]
    [InlineData("""{"unit": "HitPoints"}""", "Undefined")]
    public void TryParse_MalformedValueKinds_ReturnFalseWithTypedReason(string json, string expectedKind)
    {
        using var doc = JsonDocument.Parse(json);
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.False(success);
        Assert.Null(quantity);
        Assert.Equal($"Quantity 'value' must be a finite number or numeric string, got {expectedKind}.", reason);
    }

    [Theory]
    [InlineData("""{"value": "not-a-number", "unit": "HitPoints"}""")]
    [InlineData("""{"value": "NaN", "unit": "HitPoints"}""")]
    [InlineData("""{"value": "Infinity", "unit": "HitPoints"}""")]
    [InlineData("""{"value": "-Infinity", "unit": "HitPoints"}""")]
    public void TryParse_NonFiniteOrNonNumericString_ReturnsMissingFiniteReason(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.False(success);
        Assert.Null(quantity);
        Assert.Equal("Quantity is missing a finite numeric 'value'.", reason);
    }

    [Fact]
    public void TryParse_NonObjectRoot_ReturnsExpectedObjectReason()
    {
        using var doc = JsonDocument.Parse("""[1, 2, 3]""");
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.False(success);
        Assert.Null(quantity);
        Assert.Equal("Expected JSON object for quantity, got Array.", reason);
    }

    [Fact]
    public void TryParse_MissingUnit_ReturnsMissingUnitReason()
    {
        using var doc = JsonDocument.Parse("""{"value": 180.0}""");
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.False(success);
        Assert.Null(quantity);
        Assert.Equal("Quantity is missing required 'unit'.", reason);
    }

    [Fact]
    public void TryParse_InvalidUnit_ReturnsUnitInvalidReason()
    {
        using var doc = JsonDocument.Parse("""{"value": 180.0, "unit": "NotAValidUnit"}""");
        var success = UnitPackageQuantityDto.TryParse(doc.RootElement, out var quantity, out var reason);

        Assert.False(success);
        Assert.Null(quantity);
        Assert.NotNull(reason);
        Assert.StartsWith("Quantity unit invalid:", reason);
    }

    [Theory]
    [InlineData("""{"value": {"value": 1, "unit": "1"}, "unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got Object.")]
    [InlineData("""{"value": [1, 2, 3], "unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got Array.")]
    [InlineData("""{"value": true, "unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got True.")]
    [InlineData("""{"value": false, "unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got False.")]
    [InlineData("""{"value": null, "unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got Null.")]
    [InlineData("""{"unit": "HitPoints"}""", "Quantity 'value' must be a finite number or numeric string, got Undefined.")]
    public void ParseQuantity_MalformedValueShapes_ReturnsOmittedWithTypedError(string json, string expectedError)
    {
        using var doc = JsonDocument.Parse(json);
        var authored = UnitPackageAuthoredHelper.ParseQuantity(doc.RootElement, ContentSource.Generals, out var error);

        Assert.False(authored.IsPresent);
        Assert.Equal(expectedError, error);
    }
}
