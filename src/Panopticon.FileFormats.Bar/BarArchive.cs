using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Panopticon.FileFormats.Bar;

/// <summary>
/// Read access to an Age of Empires III DE .bar archive (version 6).
/// The archive does not take ownership of the stream it reads from.
/// </summary>
public sealed class BarArchive
{
    private const uint SupportedVersion = 6;
    private const uint Signature = 0x44332211;
    private const long HeaderSize = 0x128;

    private readonly object _lockObject = new object();

    private readonly Stream _stream;

    private readonly List<BarEntry> _entries;

    private BarArchive(Stream stream, string rootPath)
    {
        _stream = stream;
        RootPath = rootPath;
        _entries = new List<BarEntry>();
    }

    public string RootPath { get; }

    public IReadOnlyList<BarEntry> Entries => _entries;

    internal object LockObject => _lockObject;

    internal Stream Stream => _stream;

    public static BarArchive Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);

        if (reader.BaseStream.Length < HeaderSize)
        {
            throw new InvalidDataException($"BAR archive is smaller than its fixed {HeaderSize}-byte header.");
        }

        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        var fourCc = reader.ReadFourCc();
        if (fourCc != "ESPN")
        {
            throw new InvalidDataException($"Expected BAR magic 'ESPN', found '{fourCc}'.");
        }

        var version = reader.ReadUInt32();
        if (version != SupportedVersion)
        {
            throw new InvalidDataException($"Unsupported BAR version {version}; expected {SupportedVersion}.");
        }

        var signature = reader.ReadUInt32();
        if (signature != Signature)
        {
            throw new InvalidDataException($"Invalid BAR header signature 0x{signature:X8}; expected 0x{Signature:X8}.");
        }

        reader.BaseStream.Seek(0x114, SeekOrigin.Begin);
        _ = reader.ReadUInt32(); // checksum, not validated
        var fileCount = reader.ReadUInt32();
        reader.BaseStream.Seek(0x120, SeekOrigin.Begin); // 0x11C is reserved
        var tableOffset = reader.ReadInt64();

        reader.BaseStream.Seek(tableOffset, SeekOrigin.Begin);
        var rootPath = ReadLengthPrefixedUnicodeString(reader);
        var entryCount = reader.ReadUInt32();
        if (entryCount != fileCount)
        {
            throw new InvalidDataException(
                $"BAR header file count {fileCount} does not match table entry count {entryCount}.");
        }

        var archive = new BarArchive(stream, rootPath);

        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = reader.ReadUInt64();
            var decompressedSize = reader.ReadUInt32();
            var storedSize = reader.ReadUInt32();
            var paddedSize = reader.ReadUInt32();
            var relativePath = ReadLengthPrefixedUnicodeString(reader);
            var compressionFlag = reader.ReadUInt32();

            archive._entries.Add(new BarEntry(
                archive,
                CombinePath(rootPath, relativePath),
                entryOffset,
                decompressedSize,
                storedSize,
                paddedSize,
                compressionFlag));
        }

        return archive;
    }

    private static string ReadLengthPrefixedUnicodeString(BinaryReader reader)
    {
        var length = reader.ReadUInt32();
        return Encoding.Unicode.GetString(reader.ReadBytes((int)(length * 2)));
    }

    // Most roots end with a separator ('Art\'); the RenderCache root does not,
    // so a separator is inserted when neither side provides one.
    private static string CombinePath(string rootPath, string relativePath)
    {
        if (rootPath.Length == 0
            || rootPath.EndsWith('\\') || rootPath.EndsWith('/')
            || relativePath.StartsWith('\\') || relativePath.StartsWith('/'))
        {
            return rootPath + relativePath;
        }

        return rootPath + "\\" + relativePath;
    }
}
