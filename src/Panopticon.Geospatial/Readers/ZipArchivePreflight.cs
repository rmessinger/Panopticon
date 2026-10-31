using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using Panopticon.Geospatial;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Bounded central-directory scan run before <see cref="System.IO.Compression.ZipArchive"/> is
/// constructed, so the advertised and actual entry counts are enforced before any entry header or
/// name is materialized. Multi-disk and ZIP64 archives are rejected as unsupported; only the
/// end-of-central-directory record and central-directory headers are parsed, never entry data.
/// </summary>
internal static class ZipArchivePreflight
{
    private const int EndOfCentralDirectorySize = 22;
    private const int MaximumCommentLength = 0xFFFF;
    private const int CentralDirectoryHeaderSize = 46;

    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const ushort Zip64Sentinel16 = 0xFFFF;
    private const uint Zip64Sentinel32 = 0xFFFFFFFF;

    public static bool TryValidate(Stream stream, GeospatialImportLimits limits, string? sourcePath, GeoDiagnosticBag diagnostics)
    {
        var tail = ReadBoundedTail(stream);

        if (!TryFindEndOfCentralDirectory(tail, out var endOfCentralDirectory))
        {
            return Fail(diagnostics, sourcePath, "KMZ end-of-central-directory record was not found; the file is not a valid zip archive.");
        }

        var diskNumber = ReadUInt16(tail, endOfCentralDirectory + 4);
        var centralDirectoryDisk = ReadUInt16(tail, endOfCentralDirectory + 6);
        var entriesOnDisk = ReadUInt16(tail, endOfCentralDirectory + 8);
        var totalEntries = ReadUInt16(tail, endOfCentralDirectory + 10);
        var centralDirectorySize = ReadUInt32(tail, endOfCentralDirectory + 12);
        var centralDirectoryOffset = ReadUInt32(tail, endOfCentralDirectory + 16);

        if (diskNumber != 0 || centralDirectoryDisk != 0)
        {
            return Fail(diagnostics, sourcePath, "KMZ archives spanning multiple disks are not supported.");
        }

        if (totalEntries == Zip64Sentinel16 || centralDirectorySize == Zip64Sentinel32 || centralDirectoryOffset == Zip64Sentinel32)
        {
            return Fail(diagnostics, sourcePath, "ZIP64 KMZ archives are not supported; the archive exceeds the supported entry-count or size range.");
        }

        if (entriesOnDisk != totalEntries)
        {
            return Fail(
                diagnostics,
                sourcePath,
                $"KMZ metadata advertises {entriesOnDisk.ToString(CultureInfo.InvariantCulture)} entries on this disk but {totalEntries.ToString(CultureInfo.InvariantCulture)} in total; the archive is malformed.");
        }

        if (totalEntries > limits.MaxKmzEntryCount)
        {
            diagnostics.Add(new GeoDiagnostic(
                GeoDiagnosticCodes.ArchiveLimitExceeded,
                GeoStage.Archive,
                GeoSeverity.Error,
                $"KMZ metadata advertises an entry count of {totalEntries.ToString(CultureInfo.InvariantCulture)}, over the limit of {limits.MaxKmzEntryCount.ToString(CultureInfo.InvariantCulture)}.",
                sourcePath));

            return false;
        }

        if ((long)centralDirectoryOffset + centralDirectorySize > stream.Length)
        {
            return Fail(diagnostics, sourcePath, "KMZ central directory extends past the end of the archive; the metadata is malformed.");
        }

        return TryWalkCentralDirectory(stream, totalEntries, centralDirectoryOffset, centralDirectorySize, sourcePath, diagnostics);
    }

    private static bool TryWalkCentralDirectory(
        Stream stream,
        int totalEntries,
        uint centralDirectoryOffset,
        uint centralDirectorySize,
        string? sourcePath,
        GeoDiagnosticBag diagnostics)
    {
        try
        {
            var header = new byte[CentralDirectoryHeaderSize];
            var end = (long)centralDirectoryOffset + centralDirectorySize;
            var cursor = (long)centralDirectoryOffset;

            for (var index = 0; index < totalEntries; index++)
            {
                var ordinal = (index + 1).ToString(CultureInfo.InvariantCulture);

                if (cursor + CentralDirectoryHeaderSize > end)
                {
                    return Fail(diagnostics, sourcePath, $"KMZ central directory is truncated at entry {ordinal} of {totalEntries.ToString(CultureInfo.InvariantCulture)}.");
                }

                stream.Seek(cursor, SeekOrigin.Begin);
                stream.ReadExactly(header);

                if (ReadUInt32(header, 0) != CentralDirectoryHeaderSignature)
                {
                    return Fail(diagnostics, sourcePath, $"KMZ central directory entry {ordinal} does not start with a valid record signature.");
                }

                var nameLength = ReadUInt16(header, 28);
                var extraLength = ReadUInt16(header, 30);
                var commentLength = ReadUInt16(header, 32);
                cursor += CentralDirectoryHeaderSize + nameLength + extraLength + commentLength;

                if (cursor > end)
                {
                    return Fail(diagnostics, sourcePath, $"KMZ central directory entry {ordinal} overruns the end of the central directory.");
                }
            }

            if (cursor != end)
            {
                return Fail(diagnostics, sourcePath, "KMZ central directory size does not match its entry records.");
            }
        }
        catch (EndOfStreamException)
        {
            return Fail(diagnostics, sourcePath, "KMZ central directory is truncated.");
        }

        stream.Seek(0, SeekOrigin.Begin);

        return true;
    }

    private static byte[] ReadBoundedTail(Stream stream)
    {
        var tailLength = (int)Math.Min(stream.Length, EndOfCentralDirectorySize + MaximumCommentLength);
        stream.Seek(-tailLength, SeekOrigin.End);
        var tail = new byte[tailLength];
        var read = 0;

        while (read < tailLength)
        {
            var block = stream.Read(tail, read, tailLength - read);

            if (block == 0)
            {
                break;
            }

            read += block;
        }

        return read == tailLength ? tail : tail[..read];
    }

    private static bool TryFindEndOfCentralDirectory(byte[] tail, out int offset)
    {
        for (var candidate = tail.Length - EndOfCentralDirectorySize; candidate >= 0; candidate--)
        {
            if (ReadUInt32(tail, candidate) != EndOfCentralDirectorySignature)
            {
                continue;
            }

            var commentLength = ReadUInt16(tail, candidate + 20);

            if (candidate + EndOfCentralDirectorySize + commentLength <= tail.Length)
            {
                offset = candidate;

                return true;
            }
        }

        offset = -1;

        return false;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));
    }

    private static bool Fail(GeoDiagnosticBag diagnostics, string? sourcePath, string message)
    {
        diagnostics.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ArchiveFailure,
            GeoStage.Archive,
            GeoSeverity.Error,
            message,
            sourcePath,
            new InvalidDataException(message)));

        return false;
    }
}
