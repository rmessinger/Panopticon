using System;
using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// Parsed NIF header and block directory for Gamebryo 10.1.0.0 and
/// NetImmerse 10.0.1.0 files. 10.1.0.0 carries a user-version word after
/// the version; 10.0.1.0 omits it (the field is gated from 10.0.1.8 in
/// nif.xml). Both vintages carry zero groups. NetImmerse 4.2.2.0 has no
/// block directory at all and is rejected.
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
    public const string LegacyHeaderString = "NetImmerse File Format, Version 10.0.1.0";
    public const uint LegacyVersion = 0x0A000100;
    public const uint NetImmerseVersion = 0x04020200;

    /// <summary>
    /// True when the version word is followed by a user-version word
    /// (10.1.0.0 layout); false for the 10.0.1.0 layout.
    /// </summary>
    public bool HasUserVersion => Version == SupportedVersion;

    public static NifHeader Parse(NifBinaryReader reader)
    {
        var headerString = reader.ReadLineString("HeaderString");
        var version = reader.ReadUInt32("Version");

        string expectedString;
        uint userVersion;
        switch (version)
        {
            case SupportedVersion:
                expectedString = SupportedHeaderString;
                userVersion = reader.ReadUInt32("UserVersion");
                if (userVersion != SupportedUserVersion)
                {
                    throw new NotSupportedException(
                        $"NIF user version {userVersion} is not supported; only {SupportedUserVersion} is.");
                }

                break;

            case LegacyVersion:
                expectedString = LegacyHeaderString;
                userVersion = 0;
                break;

            case NetImmerseVersion:
                throw new NotSupportedException(
                    "NetImmerse File Format 4.2.2.0 (0x04020200) has no block directory and no DDF " +
                    "binding references it; C4i does not import it.");

            default:
                throw new NotSupportedException(
                    $"NIF version 0x{version:X8} is not supported; only 0x{SupportedVersion:X8} " +
                    $"(10.1.0.0) and 0x{LegacyVersion:X8} (10.0.1.0) are.");
        }

        if (headerString != expectedString)
        {
            throw new NotSupportedException(
                $"NIF header string '{headerString}' does not match version 0x{version:X8}; " +
                $"'{expectedString}' is required.");
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
