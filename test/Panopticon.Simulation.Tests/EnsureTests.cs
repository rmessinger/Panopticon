using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace Panopticon.Tests;

public class EnsureTests
{
    [Fact]
    public void NotNullOrEmpty_Passes_ForNonEmptyString()
    {
        Ensure.NotNullOrEmpty("texture", null);
    }

    [Fact]
    public void NotNullOrEmpty_ThrowsArgumentNullException_ForNull()
    {
        string? text = null;

        var ex = Assert.Throws<ArgumentNullException>(() => Ensure.NotNullOrEmpty(text, null));

        Assert.Equal("text", ex.ParamName);
    }

    [Fact]
    public void NotNullOrEmpty_ThrowsArgumentException_ForEmpty_WithMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => Ensure.NotNullOrEmpty("", "Id must be set.", "id"));

        Assert.Equal("id", ex.ParamName);
        Assert.Contains("Id must be set.", ex.Message);
    }

    [Fact]
    public void NotNullOrWhiteSpace_Passes_ForNonWhitespaceString()
    {
        Ensure.NotNullOrWhiteSpace(" name ", null);
    }

    [Fact]
    public void NotNullOrWhiteSpace_ThrowsArgumentNullException_ForNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => Ensure.NotNullOrWhiteSpace(null, null, "name"));

        Assert.Equal("name", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void NotNullOrWhiteSpace_ThrowsArgumentException_ForWhitespace(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => Ensure.NotNullOrWhiteSpace(value, "Name must be visible.", "name"));

        Assert.Equal("name", ex.ParamName);
        Assert.Contains("Name must be visible.", ex.Message);
    }

    [Fact]
    public void NotEmpty_Passes_ForNonEmptyCollection()
    {
        Ensure.NotEmpty<int>([1], null);
    }

    [Fact]
    public void NotEmpty_ThrowsArgumentNullException_ForNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => Ensure.NotEmpty<int>(null, null, "vertices"));

        Assert.Equal("vertices", ex.ParamName);
    }

    [Fact]
    public void NotEmpty_ThrowsArgumentException_ForEmpty_WithMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => Ensure.NotEmpty<int>([], "A mesh must have vertices.", "vertices"));

        Assert.Equal("vertices", ex.ParamName);
        Assert.Contains("A mesh must have vertices.", ex.Message);
    }

    [Fact]
    public void NoNullElements_Passes_ForListOfValues()
    {
        Ensure.NoNullElements<string>(["a", "b"], "Mesh part");
    }

    [Fact]
    public void NoNullElements_ThrowsArgumentNullException_ForNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => Ensure.NoNullElements<string>(null, "Mesh part", "parts"));

        Assert.Equal("parts", ex.ParamName);
    }

    [Fact]
    public void NoNullElements_ThrowsArgumentException_WithIndexWording()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Ensure.NoNullElements<string?>(["a", null], "Mesh part", "parts"));

        Assert.Equal("parts", ex.ParamName);
        Assert.Contains("Mesh part at index 1 must not be null.", ex.Message);
    }

    [Fact]
    public void AtLeast_Passes_ForValueAtMinimum()
    {
        Ensure.AtLeast(1, 1, null);
        Ensure.AtLeast(TimeSpan.Zero, TimeSpan.Zero, null);
    }

    [Fact]
    public void AtLeast_ThrowsArgumentOutOfRangeException_BelowMinimum_WithMessage()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.AtLeast(-1, 0, "Radius must not be negative.", "radius"));

        Assert.Equal("radius", ex.ParamName);
        Assert.Contains("Radius must not be negative.", ex.Message);
    }

    [Fact]
    public void GreaterThan_Passes_AboveBound()
    {
        Ensure.GreaterThan(2, 1, null);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void GreaterThan_ThrowsArgumentOutOfRangeException_AtOrBelowBound(int value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.GreaterThan(value, 1, "Scale must be positive.", "scale"));

        Assert.Equal("scale", ex.ParamName);
        Assert.Contains("Scale must be positive.", ex.Message);
    }

    [Fact]
    public void InRange_Passes_AtInclusiveBounds()
    {
        Ensure.InRange(0f, 0f, 1f, null);
        Ensure.InRange(1f, 0f, 1f, null);
    }

    [Fact]
    public void InRange_ThrowsArgumentOutOfRangeException_OutsideBounds_WithMessage()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.InRange(1.5f, 0f, 1f, "Weight must be normalized.", "weight"));

        Assert.Equal("weight", ex.ParamName);
        Assert.Contains("Weight must be normalized.", ex.Message);
    }

    [Fact]
    public void Finite_Passes_ForFiniteFloat()
    {
        Ensure.Finite(0f, null);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Finite_ThrowsArgumentOutOfRangeException_ForNonFinite(float value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.Finite(value, "Scale must be finite.", "scale"));

        Assert.Equal("scale", ex.ParamName);
        Assert.Contains("Scale must be finite.", ex.Message);
    }

    [Fact]
    public void FiniteVector3_Passes_ForFiniteComponents()
    {
        Ensure.Finite(Vector3.Zero, null);
    }

    [Fact]
    public void FiniteVector3_ThrowsArgumentException_ForNaNComponent()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Ensure.Finite(new Vector3(1f, float.NaN, 2f), "Offset must be finite.", "offset"));

        Assert.Equal("offset", ex.ParamName);
        Assert.Contains("Offset must be finite.", ex.Message);
    }

    [Fact]
    public void FiniteMatrix4x4_ThrowsArgumentException_ForInfiniteComponent()
    {
        var value = Matrix4x4.Identity;
        value.M33 = float.PositiveInfinity;

        var ex = Assert.Throws<ArgumentException>(() => Ensure.Finite(value, "Transform must be finite.", "transform"));

        Assert.Equal("transform", ex.ParamName);
        Assert.Contains("Transform must be finite.", ex.Message);
    }

    [Fact]
    public void FiniteNonNegative_Passes_ForZeroAndPositive()
    {
        Ensure.FiniteNonNegative(0f, null);
        Ensure.FiniteNonNegative(1.5f, null);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void FiniteNonNegative_ThrowsArgumentOutOfRangeException_ForInvalid(float value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Ensure.FiniteNonNegative(value, "Radius must be non-negative.", "radius"));

        Assert.Equal("radius", ex.ParamName);
        Assert.Contains("Radius must be non-negative.", ex.Message);
    }

    [Fact]
    public void FinitePositive_Passes_ForPositive()
    {
        Ensure.FinitePositive(0.1f, null);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void FinitePositive_ThrowsArgumentOutOfRangeException_ForInvalid(float value)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Ensure.FinitePositive(value, "Scale must be positive.", "scale"));

        Assert.Equal("scale", ex.ParamName);
        Assert.Contains("Scale must be positive.", ex.Message);
    }

    [Fact]
    public void GenericMessages_NameTheParameter()
    {
        var empty = Assert.Throws<ArgumentException>(() => Ensure.NotNullOrEmpty("", null, "id"));
        Assert.Contains("id must not be null or empty.", empty.Message);

        var range = Assert.Throws<ArgumentOutOfRangeException>(() => Ensure.AtLeast(0, 1, null, "count"));
        Assert.Contains("count must be at least 1.", range.Message);

        var items = Assert.Throws<ArgumentException>(() => Ensure.NotEmpty<int>(Array.Empty<int>(), null, "bones"));
        Assert.Contains("bones must contain at least one element.", items.Message);
    }

    [Fact]
    public void NoNullElements_AcceptsNullEntriesInListType()
    {
        IReadOnlyList<int?> values = [1, 2];

        Ensure.NoNullElements(values, "Value");
    }
}
