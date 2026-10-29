using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Panopticon.FileFormats.Bar;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class XmbDocumentTests
{
    [Fact]
    public void DecodesDocumentWithAttributesAndChildren()
    {
        var document = new XDocument(
            new XElement("proto",
                new XAttribute("version", "2"),
                new XElement("Unit",
                    new XAttribute("name", "Musketeer"),
                    "musketeer text"),
                new XElement("Unit", "plain")));

        var roundTripped = XmbDocument.Read(
            new MemoryStream(SyntheticXmbBuilder.Build(document)));

        var root = roundTripped.Root!;
        Assert.Equal("proto", root.Name.LocalName);
        Assert.Equal("2", (string?)root.Attribute("version"));

        var units = root.Elements("Unit").ToList();
        Assert.Equal(2, units.Count);
        Assert.Equal("Musketeer", (string?)units[0].Attribute("name"));
        Assert.Equal("musketeer text", units[0].Value);
        Assert.Equal("plain", units[1].Value);
        Assert.Null(units[1].Attribute("name"));
    }

    [Fact]
    public void ReadsAlz4WrappedDocument()
    {
        var document = new XDocument(new XElement("root", "wrapped text"));

        var wrapped = SyntheticBarBuilder.WrapInAlz4(SyntheticXmbBuilder.Build(document));

        var roundTripped = XmbDocument.Read(new MemoryStream(wrapped));

        Assert.Equal("root", roundTripped.Root!.Name.LocalName);
        Assert.Equal("wrapped text", roundTripped.Root.Value);
    }

    [Fact]
    public void ThrowsOnBadHeaderTag()
    {
        var image = SyntheticXmbBuilder.WithBadHeaderTag(BuildMinimalImage());

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("offset 0", exception.Message);
        Assert.Contains("'X1'", exception.Message);
    }

    [Fact]
    public void ThrowsOnBadSecondTag()
    {
        var image = SyntheticXmbBuilder.WithBadSecondTag(BuildMinimalImage());

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("offset 6", exception.Message);
        Assert.Contains("'XR'", exception.Message);
    }

    [Fact]
    public void ThrowsOnBadFormatField()
    {
        var image = SyntheticXmbBuilder.WithBadFormatField(BuildMinimalImage());

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("offset 8", exception.Message);
    }

    [Fact]
    public void ThrowsOnUnsupportedVersion()
    {
        var image = SyntheticXmbBuilder.WithUnsupportedVersion(BuildMinimalImage());

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("offset 12", exception.Message);
        Assert.Contains("version 7", exception.Message);
    }

    [Fact]
    public void ThrowsOnElementIndexOutsideTable()
    {
        var image = SyntheticXmbBuilder.BuildWithElementIndex(elementIndex: 5);

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("element name index 5", exception.Message);
        Assert.Contains("table of 1 names", exception.Message);
    }

    [Fact]
    public void ThrowsOnAttributeIndexOutsideTable()
    {
        var image = SyntheticXmbBuilder.BuildWithAttributeIndex(attributeIndex: 9);

        var exception = Assert.Throws<InvalidDataException>(
            () => XmbDocument.Read(new MemoryStream(image)));

        Assert.Contains("attribute name index 9", exception.Message);
    }

    private static byte[] BuildMinimalImage()
    {
        return SyntheticXmbBuilder.Build(new XDocument(new XElement("root", "text")));
    }
}
