#nullable enable

using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Serializes a unit package manifest to JSON with the member names the H4 reader expects.</summary>
public static class UnitPackageManifestJsonWriter
{
    public static string Serialize(UnitPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("containerKind", manifest.ContainerKind);
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);

            writer.WritePropertyName("source");

            if (manifest.Source is { } source)
            {
                writer.WriteStartObject();
                writer.WriteString("id", source.Id);
                writer.WriteBoolean("caseSensitiveNames", source.CaseSensitiveNames);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStartArray("roots");

            foreach (var root in manifest.Roots)
            {
                writer.WriteStartObject();
                writer.WriteString("name", root.Name);
                writer.WriteString("kind", root.Kind.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("records");

            foreach (var record in manifest.Records)
            {
                writer.WriteStartObject();
                writer.WriteString("name", record.Name);
                writer.WriteString("kind", record.Kind.ToString());
                writer.WriteString("path", record.Path);
                writer.WriteNumber("byteLength", record.ByteLength);
                writer.WriteString("sha256", record.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("retainedPayloads");

            foreach (var retained in manifest.RetainedPayloads)
            {
                writer.WriteStartObject();
                writer.WriteString("path", retained.Path);
                writer.WriteString("dialect", retained.Dialect);
                writer.WriteNumber("dialectVersion", retained.DialectVersion);
                writer.WriteNumber("byteLength", retained.ByteLength);
                writer.WriteString("sha256", retained.Sha256);

                if (retained.Provenance is { } provenance)
                {
                    WriteProvenance(writer, provenance);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (manifest.Provenance is { } packageProvenance)
            {
                WriteProvenance(writer, packageProvenance);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteProvenance(Utf8JsonWriter writer, UnitPackageProvenance provenance)
    {
        writer.WriteStartObject("provenance");
        writer.WriteString("sourceId", provenance.SourceId);
        writer.WriteString("filePath", provenance.FilePath);
        writer.WriteNumber("line", provenance.Line);

        if (provenance.ArchiveLayer is { } layer)
        {
            writer.WriteString("archiveLayer", layer);
        }

        writer.WriteEndObject();
    }
}
