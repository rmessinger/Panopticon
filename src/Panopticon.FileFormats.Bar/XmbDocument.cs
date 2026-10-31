using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// Reader for AoE3 DE XMB binary XML ('X1' header, version 8). Reads either a
/// raw XMB stream or an alz4-wrapped payload.
/// </summary>
public static class XmbDocument
{
    private const uint ExpectedFormat = 4;
    private const uint ExpectedVersion = 8;

    public static XDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Zip-backed entry streams are forward-only; the decoder needs Length and seek.
        if (!stream.CanSeek)
        {
            var seekable = new MemoryStream();
            stream.CopyTo(seekable);
            seekable.Position = 0;
            stream = seekable;
        }

        if (Alz4.IsAlz4(stream))
        {
            var compressed = new byte[stream.Length - stream.Position];
            stream.ReadExactly(compressed);
            using var decompressed = new MemoryStream(Alz4.Decompress(compressed));

            return ReadUncompressed(decompressed);
        }

        return ReadUncompressed(stream);
    }

    private static XDocument ReadUncompressed(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

        ExpectTag(reader, "X1");
        _ = reader.ReadUInt32(); // payload length, informational
        ExpectTag(reader, "XR");

        var format = reader.ReadUInt32();
        if (format != ExpectedFormat)
        {
            throw new InvalidDataException($"XMB format field at offset 8 must be {ExpectedFormat}, found {format}.");
        }

        var version = reader.ReadUInt32();
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException($"Unsupported XMB version {version} at offset 12; expected {ExpectedVersion}.");
        }

        var elements = ReadNameTable(reader);
        var attributes = ReadNameTable(reader);
        var root = ReadNode(reader, elements, attributes);

        return new XDocument(root);
    }

    private static void ExpectTag(BinaryReader reader, string expectedTag)
    {
        var offset = reader.BaseStream.Position;
        var tag = Encoding.ASCII.GetString(reader.ReadBytes(2));
        if (tag != expectedTag)
        {
            var found = tag.Length == 2 ? $"'{tag}'" : "end of stream";
            throw new InvalidDataException($"Expected XMB tag '{expectedTag}' at offset {offset}, found {found}.");
        }
    }

    private static List<string> ReadNameTable(BinaryReader reader)
    {
        var offset = reader.BaseStream.Position;
        var count = reader.ReadUInt32();
        if (count > (uint)(reader.BaseStream.Length - reader.BaseStream.Position) / 4)
        {
            throw new InvalidDataException($"XMB name table count {count} at offset {offset} exceeds the remaining data.");
        }

        var names = new List<string>((int)count);
        for (var i = 0; i < count; i++)
        {
            names.Add(ReadLengthPrefixedString(reader));
        }

        return names;
    }

    private static string ReadLengthPrefixedString(BinaryReader reader)
    {
        var offset = reader.BaseStream.Position;
        var length = reader.ReadInt32();
        if (length < 0 || length * 2 > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException($"XMB string length {length} at offset {offset} exceeds the remaining data.");
        }

        return Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
    }

    private static XElement ReadNode(BinaryReader reader, List<string> elements, List<string> attributes)
    {
        ExpectTag(reader, "XN");
        _ = reader.ReadInt32(); // node payload length, informational

        var innerText = ReadLengthPrefixedString(reader);
        var element = new XElement(elements[ReadTableIndex(reader, elements, "element")]);
        if (innerText.Length > 0)
        {
            element.Add(innerText);
        }

        _ = reader.ReadInt32(); // source line number, informational

        var attributeCount = ReadCount(reader, "attribute count");
        for (var i = 0; i < attributeCount; i++)
        {
            var nameIndex = ReadTableIndex(reader, attributes, "attribute");
            element.SetAttributeValue(attributes[nameIndex], ReadLengthPrefixedString(reader));
        }

        var childCount = ReadCount(reader, "child count");
        for (var i = 0; i < childCount; i++)
        {
            element.Add(ReadNode(reader, elements, attributes));
        }

        return element;
    }

    private static int ReadTableIndex(BinaryReader reader, List<string> names, string tableKind)
    {
        var offset = reader.BaseStream.Position;
        var index = reader.ReadInt32();
        if (index < 0 || index >= names.Count)
        {
            throw new InvalidDataException(
                $"XMB {tableKind} name index {index} at offset {offset} is outside the table of {names.Count} names.");
        }

        return index;
    }

    private static int ReadCount(BinaryReader reader, string field)
    {
        var offset = reader.BaseStream.Position;
        var count = reader.ReadInt32();
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException($"XMB {field} {count} at offset {offset} is negative or exceeds the remaining data.");
        }

        return count;
    }
}
