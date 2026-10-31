using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Serializes an XDocument into minimal XMB v8 bytes for install-free tests.
/// Element and attribute names are interned in first-appearance document order.
/// </summary>
internal static class SyntheticXmbBuilder
{
    public static byte[] Build(XDocument document)
    {
        var elements = new List<string>();
        var attributes = new List<string>();
        CollectNames(document.Root!, elements, attributes);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("X1"));
        writer.Write(0u); // payload length, informational
        writer.Write(Encoding.ASCII.GetBytes("XR"));
        writer.Write(4u);
        writer.Write(8u);

        WriteNameTable(writer, elements);
        WriteNameTable(writer, attributes);
        WriteNode(writer, document.Root!, elements, attributes);

        return stream.ToArray();
    }

    public static byte[] WithBadHeaderTag(byte[] image)
    {
        image[0] = (byte)'Y';
        return image;
    }

    public static byte[] WithBadSecondTag(byte[] image)
    {
        image[6] = (byte)'S';
        return image;
    }

    public static byte[] WithBadFormatField(byte[] image)
    {
        WriteUInt32(image, 8, 5);
        return image;
    }

    public static byte[] WithUnsupportedVersion(byte[] image)
    {
        WriteUInt32(image, 12, 7);
        return image;
    }

    public static byte[] BuildWithElementIndex(int elementIndex)
    {
        return BuildRawNode(nameIndex: elementIndex, attributeCount: 0);
    }

    public static byte[] BuildWithAttributeIndex(int attributeIndex)
    {
        return BuildRawNode(nameIndex: 0, attributeCount: 1, attributeIndex: attributeIndex);
    }

    private static void WriteNameTable(BinaryWriter writer, List<string> names)
    {
        writer.Write(names.Count);
        foreach (var name in names)
        {
            writer.Write(name.Length);
            writer.Write(Encoding.Unicode.GetBytes(name));
        }
    }

    private static void CollectNames(XElement element, List<string> elements, List<string> attributes)
    {
        if (!elements.Contains(element.Name.LocalName))
        {
            elements.Add(element.Name.LocalName);
        }

        foreach (var attribute in element.Attributes())
        {
            if (!attributes.Contains(attribute.Name.LocalName))
            {
                attributes.Add(attribute.Name.LocalName);
            }
        }

        foreach (var child in element.Elements())
        {
            CollectNames(child, elements, attributes);
        }
    }

    private static void WriteNode(BinaryWriter writer, XElement element, List<string> elements, List<string> attributes)
    {
        var innerText = string.Concat(element.Nodes().OfType<XText>());

        writer.Write(Encoding.ASCII.GetBytes("XN"));
        writer.Write(0); // node payload length, informational
        writer.Write(innerText.Length);
        if (innerText.Length > 0)
        {
            writer.Write(Encoding.Unicode.GetBytes(innerText));
        }

        writer.Write(elements.IndexOf(element.Name.LocalName));
        writer.Write(0); // source line number, informational

        var elementAttributes = element.Attributes().ToList();
        writer.Write(elementAttributes.Count);
        foreach (var attribute in elementAttributes)
        {
            writer.Write(attributes.IndexOf(attribute.Name.LocalName));
            writer.Write(attribute.Value.Length);
            writer.Write(Encoding.Unicode.GetBytes(attribute.Value));
        }

        var children = element.Elements().ToList();
        writer.Write(children.Count);
        foreach (var child in children)
        {
            WriteNode(writer, child, elements, attributes);
        }
    }

    private static byte[] BuildRawNode(int nameIndex, int attributeCount, int attributeIndex = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("X1"));
        writer.Write(0u);
        writer.Write(Encoding.ASCII.GetBytes("XR"));
        writer.Write(4u);
        writer.Write(8u);

        writer.Write(1);
        writer.Write(4);
        writer.Write(Encoding.Unicode.GetBytes("root"));

        writer.Write(attributeCount);
        if (attributeCount > 0)
        {
            writer.Write(2);
            writer.Write(Encoding.Unicode.GetBytes("id"));
        }

        writer.Write(Encoding.ASCII.GetBytes("XN"));
        writer.Write(0); // node payload length
        writer.Write(0); // inner text length
        writer.Write(nameIndex);
        writer.Write(0); // source line number

        writer.Write(attributeCount);
        if (attributeCount > 0)
        {
            writer.Write(attributeIndex);
            writer.Write(0); // attribute value length
        }

        writer.Write(0); // child count

        return stream.ToArray();
    }

    private static void WriteUInt32(byte[] image, int offset, uint value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
        image[offset + 2] = (byte)(value >> 16);
        image[offset + 3] = (byte)(value >> 24);
    }
}
