using System;
using System.Collections.Generic;

namespace Panopticon.Geospatial.Readers;

/// <summary>
/// Parses and validates tag 34735 GeoKey directories (GeoTIFF 1.0 and 1.1, OGC 19-008r4,
/// section 7). Input is plain arrays so the parser stays independent of LibTiff types; structural
/// failures are reported as Parse diagnostics.
/// </summary>
internal static class GeoKeyParser
{
    internal const ushort ModelTypeKey = 1024;

    internal const ushort RasterTypeKey = 1025;

    internal const ushort GeographicTypeKey = 2048;

    internal const ushort GeogLinearUnitsKey = 2052;

    internal const ushort GeogAngularUnitsKey = 2054;

    internal const ushort ProjectedCSTypeKey = 3072;

    internal const ushort ProjectedLinearUnitsKey = 3076;

    internal const ushort VerticalCSTypeKey = 4096;

    internal const ushort VerticalUnitsKey = 4099;

    /// <summary>Tag 34735; ValueOffset indexes the whole tag, so parameter storage is the shorts past the declared entries.</summary>
    internal const ushort GeoKeyDirectoryTag = 34735;

    internal const ushort GeoDoubleParamsTag = 34736;

    private const ushort GeoAsciiParamsTag = 34737;

    private const int HeaderSize = 4;

    private const int EntrySize = 4;

    /// <summary>
    /// Validates the directory and returns its keys, or null when absent or malformed. Malformed
    /// directories add a Parse error to <paramref name="diagnostics"/>; an absent tag adds nothing.
    /// Shorts past the declared entries are legal tag 34735 parameter storage, indexed from the tag start.
    /// </summary>
    internal static GeoKeySet? TryParse(
        ushort[]? directory,
        double[]? doubleParams,
        int asciiParamsLength,
        GeoDiagnosticBag diagnostics,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (directory is null)
        {
            return null;
        }

        if (directory.Length < HeaderSize)
        {
            AddError(diagnostics, sourcePath, $"GeoKey directory carries {directory.Length} shorts, fewer than its four-short header.");
            return null;
        }

        if (directory[0] != 1)
        {
            AddError(diagnostics, sourcePath, $"GeoKey directory version {directory[0]} is not 1.");
            return null;
        }

        if (directory[1] != 1)
        {
            AddError(diagnostics, sourcePath, $"GeoKey directory key revision {directory[1]} is not 1.");
            return null;
        }

        if (directory[2] > 1)
        {
            AddError(diagnostics, sourcePath, $"GeoKey directory minor revision {directory[2]} exceeds 1.");
            return null;
        }

        var declaredCount = directory[3];
        var carriedEntries = (directory.Length - HeaderSize) / EntrySize;

        if (declaredCount > carriedEntries)
        {
            AddError(diagnostics, sourcePath, $"GeoKey directory declares {declaredCount} keys but carries {carriedEntries}.");
            return null;
        }

        var entriesEnd = HeaderSize + declaredCount * EntrySize;
        var entries = new Dictionary<ushort, GeoKeyEntry>(declaredCount);

        for (var index = 0; index < declaredCount; index++)
        {
            var offset = HeaderSize + index * EntrySize;
            var keyId = directory[offset];

            if (keyId == 0)
            {
                AddError(diagnostics, sourcePath, $"GeoKey entry {index} carries key id 0.");
                return null;
            }

            var entry = new GeoKeyEntry(directory[offset + 1], directory[offset + 2], directory[offset + 3]);

            if (!entries.TryAdd(keyId, entry))
            {
                AddError(diagnostics, sourcePath, $"GeoKey {keyId} appears more than once.");
                return null;
            }

            if (!EntryStorageIsWellFormed(entry, entriesEnd, directory.Length, doubleParams, asciiParamsLength, diagnostics, sourcePath, keyId))
            {
                return null;
            }

            if (IsScalarKey(keyId) && entry.TiffTagLocation != 0)
            {
                AddError(
                    diagnostics,
                    sourcePath,
                    $"GeoKey {keyId} is a scalar CRS, model, raster, or unit key and must carry one inline SHORT (tag 0, count 1), not tag {entry.TiffTagLocation} with count {entry.Count}.");
                return null;
            }
        }

        return new GeoKeySet(entries);
    }

    private static bool EntryStorageIsWellFormed(
        GeoKeyEntry entry,
        int entriesEnd,
        int directoryLength,
        double[]? doubleParams,
        int asciiParamsLength,
        GeoDiagnosticBag diagnostics,
        string? sourcePath,
        ushort keyId)
    {
        if (entry.Count == 0)
        {
            AddError(diagnostics, sourcePath, $"GeoKey {keyId} declares zero values.");
            return false;
        }

        if (entry.TiffTagLocation == 0)
        {
            if (entry.Count != 1)
            {
                AddError(diagnostics, sourcePath, $"Inline GeoKey {keyId} carries count {entry.Count}, not 1.");
                return false;
            }

            return true;
        }

        if (entry.TiffTagLocation == GeoKeyDirectoryTag)
        {
            if (entry.ValueOffset < entriesEnd)
            {
                AddError(
                    diagnostics,
                    sourcePath,
                    $"GeoKey {keyId} reads short params at {entry.ValueOffset}, inside the header or key entries.");
                return false;
            }

            return ReferenceIsBounded(keyId, "short params", entry, directoryLength, diagnostics, sourcePath);
        }

        if (entry.TiffTagLocation == GeoDoubleParamsTag)
        {
            if (doubleParams is null)
            {
                AddError(diagnostics, sourcePath, $"GeoKey {keyId} references GeoDoubleParams, which is absent.");
                return false;
            }

            return ReferenceIsBounded(keyId, "double params", entry, doubleParams.Length, diagnostics, sourcePath);
        }

        if (entry.TiffTagLocation == GeoAsciiParamsTag)
        {
            return ReferenceIsBounded(keyId, "ASCII params", entry, asciiParamsLength, diagnostics, sourcePath);
        }

        AddError(
            diagnostics,
            sourcePath,
            $"GeoKey {keyId} stores its value in TIFF tag {entry.TiffTagLocation}, not inline or in the GeoTIFF parameter tags 34735, 34736, and 34737.");
        return false;
    }

    private static bool ReferenceIsBounded(
        ushort keyId,
        string kind,
        GeoKeyEntry entry,
        int availableLength,
        GeoDiagnosticBag diagnostics,
        string? sourcePath)
    {
        var lastExclusive = (long)entry.ValueOffset + entry.Count;

        if (lastExclusive > availableLength)
        {
            AddError(
                diagnostics,
                sourcePath,
                $"GeoKey {keyId} reads {kind} {entry.ValueOffset}..{lastExclusive}, beyond the {availableLength} values present.");
            return false;
        }

        return true;
    }

    private static bool IsScalarKey(ushort keyId)
    {
        return keyId is ModelTypeKey
            or RasterTypeKey
            or GeographicTypeKey
            or GeogLinearUnitsKey
            or GeogAngularUnitsKey
            or ProjectedCSTypeKey
            or ProjectedLinearUnitsKey
            or VerticalCSTypeKey
            or VerticalUnitsKey;
    }

    private static void AddError(GeoDiagnosticBag diagnostics, string? sourcePath, string message)
    {
        diagnostics.Add(new GeoDiagnostic(
            GeoDiagnosticCodes.ParseFailure,
            GeoStage.Parse,
            GeoSeverity.Error,
            message,
            sourcePath));
    }
}

/// <summary>A validated GeoKey directory entry: where the key's value lives and how long it is.</summary>
internal readonly record struct GeoKeyEntry(ushort TiffTagLocation, ushort Count, ushort ValueOffset);

/// <summary>Validated key set with bounded value access.</summary>
internal sealed class GeoKeySet(Dictionary<ushort, GeoKeyEntry> entries)
{
    private readonly Dictionary<ushort, GeoKeyEntry> _entries = entries;

    internal bool Contains(ushort keyId)
    {
        return _entries.ContainsKey(keyId);
    }

    /// <summary>Reads a key's inline scalar value; parameter-tag storage returns false.</summary>
    internal bool TryGetInteger(ushort keyId, out int value)
    {
        value = 0;

        if (!_entries.TryGetValue(keyId, out var entry)
            || entry.TiffTagLocation != 0
            || entry.Count != 1)
        {
            return false;
        }

        value = entry.ValueOffset;

        return true;
    }
}
