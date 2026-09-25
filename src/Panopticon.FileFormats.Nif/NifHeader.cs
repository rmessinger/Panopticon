using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Parsed NIF 10.1.0.0 header and block directory. Only
/// Gamebryo 10.1.0.0 with user version 0 and zero groups is accepted.
/// </summary>
public sealed record NifHeader(
    string HeaderString,
    uint Version,
    uint UserVersion,
    int NumBlocks,
    IReadOnlyList<string> BlockTypes,
    IReadOnlyList<NifBlockDirectoryEntry> Blocks)
{
    public const string SupportedHeaderString = "Gamebryo File Format, Version 10.1.0.0";
    public const uint SupportedVersion = 0x0A010000;
    public const uint SupportedUserVersion = 0;

    public static NifHeader Parse(NifBinaryReader reader)
    {
        var headerString = reader.ReadLineString("HeaderString");
        if (headerString != SupportedHeaderString)
        {
            throw new NotSupportedException(
                $"NIF header string '{headerString}' is not '{SupportedHeaderString}'.");
        }

        var version = reader.ReadUInt32("Version");
        if (version != SupportedVersion)
        {
            throw new NotSupportedException(
                $"NIF version 0x{version:X8} is not supported; only 0x{SupportedVersion:X8} (10.1.0.0) is.");
        }

        var userVersion = reader.ReadUInt32("UserVersion");
        if (userVersion != SupportedUserVersion)
        {
            throw new NotSupportedException(
                $"NIF user version {userVersion} is not supported; only {SupportedUserVersion} is.");
        }

        // The smallest block body is the leading u32 plus an empty sized string,
        // so 8 bytes per block bounds the count against the remaining data.
        var numBlocks = reader.ReadElementCount(8, "NumBlocks");

        var numTypes = reader.ReadUInt16("NumBlockTypes");
        var blockTypes = new string[numTypes];
        for (var i = 0; i < numTypes; i++)
        {
            blockTypes[i] = reader.ReadSizedString($"BlockTypes[{i}]");
        }

        var entries = new NifBlockDirectoryEntry[numBlocks];
        for (var i = 0; i < numBlocks; i++)
        {
            var typeIndex = reader.ReadUInt16($"BlockTypeIndex[{i}]");
            if (typeIndex >= numTypes)
            {
                throw new InvalidDataException(
                    $"NIF block {i} has type index {typeIndex} but only {numTypes} block types exist.");
            }

            entries[i] = new NifBlockDirectoryEntry(i, typeIndex, blockTypes[typeIndex]);
        }

        var numGroups = reader.ReadUInt32("NumGroups");
        if (numGroups != 0)
        {
            throw new InvalidDataException(
                $"NIF group count is {numGroups}; only 0 is valid for this format.");
        }

        return new NifHeader(headerString, version, userVersion, numBlocks, blockTypes, entries);
    }
}
