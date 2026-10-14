using System;
using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Graphics;

public class VerticalSyncOptionTests : IDisposable
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", false)]
    public void PanopticonNovsyncAcceptsBooleanValues(string? value, bool expected)
    {
        Environment.SetEnvironmentVariable("PANOPTICON_NOVSYNC", value);
        Assert.Equal(expected, GraphicsDeviceUtility.VerticalSyncDisabled);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PANOPTICON_NOVSYNC", null);
    }
}
