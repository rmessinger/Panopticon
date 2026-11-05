using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Payload.Reading;

/// <summary>
/// Payload root resolution and exact-inventory validation; the reader never follows
/// symbolic links, at the root, in ancestors, or in section directories. See
/// <see href="../../../../docs/roadmap/implementation/track-r-r2-payload.md">track R2 payload</see>.
/// </summary>
internal static class PayloadRootInventory
{
    private const string TerrainSection = "terrain";

    private const string MaterialsSection = "materials";

    private const string MasksSection = "masks";

    private const string ImagerySection = "imagery";

    private static readonly EnumerationOptions InventoryEnumeration = new()
    {
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Returns the normalized full path of the payload root, or null with a typed
    /// diagnostic when the path is missing, not a directory, or reaches through a
    /// symbolic link at the root or any ancestor.
    /// </summary>
    internal static string? TryResolveRoot(string directory, GeoDiagnosticBag diagnostics)
    {
        string full;

        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.RootInvalid,
                $"The payload path '{directory}' is not a usable directory path: {ex.Message}",
                ex);

            return null;
        }

        return TryEnsureRealRoot(full, diagnostics) ? full : null;
    }

    /// <summary>
    /// Validates the root and every proper ancestor as real, link-free directories.
    /// Called at open and again on every lazy read, so a root or ancestor renamed away
    /// and replaced by a link after open is rejected instead of followed.
    /// </summary>
    internal static bool TryEnsureRealRoot(string root, GeoDiagnosticBag diagnostics)
    {
        try
        {
            var attributes = File.GetAttributes(root);

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.RootIsLink,
                    $"The payload root '{root}' is a symbolic link; the reader never follows links, not even at the root.");

                return false;
            }

            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.RootInvalid,
                    $"The payload root '{root}' is not a directory.");

                return false;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.RootInvalid,
                $"The payload root '{root}' does not exist.");

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.FileUnreadable,
                $"The payload root '{root}' could not be inspected: {ex.Message}",
                ex);

            return false;
        }

        return TryRejectLinkedAncestor(root, diagnostics);
    }

    /// <summary>
    /// Revalidates the whole known canonical path of one payload file on every read:
    /// the root and its ancestors as real directories, then each intermediate segment
    /// of the payload-relative path as a real, link-free directory. The final file's
    /// link check stays with the caller that opens it. This is not a full inventory
    /// recrawl, and no atomicity is claimed against concurrent swaps: a swap that
    /// lands between the checks is still followed only until the next read re-checks.
    /// </summary>
    internal static bool TryValidateKnownPathChain(string root, string relativePath, GeoDiagnosticBag diagnostics)
    {
        if (!TryEnsureRealRoot(root, diagnostics))
        {
            return false;
        }

        var current = root;

        foreach (var segment in relativePath.Split('/')[..^1])
        {
            current = Path.Combine(current, segment);

            try
            {
                var attributes = File.GetAttributes(current);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.InventoryLink,
                        $"The payload directory '{segment}' is a symbolic link; the reader never descends into links.");

                    return false;
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.InventoryMismatch,
                        $"The payload directory '{segment}' is no longer a directory.");

                    return false;
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.InventoryMismatch,
                    $"The payload directory '{segment}' no longer exists.");

                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.FileUnreadable,
                    $"The payload directory '{segment}' could not be inspected: {ex.Message}",
                    ex);

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks every proper ancestor directory of the payload root for reparse points,
    /// so a swapped ancestor link cannot redirect an already-validated payload.
    /// </summary>
    private static bool TryRejectLinkedAncestor(string full, GeoDiagnosticBag diagnostics)
    {
        var rootLength = Path.GetPathRoot(full)!.Length;

        for (var index = rootLength; index < full.Length;)
        {
            var separator = full.IndexOf(Path.DirectorySeparatorChar, index);

            if (separator == -1)
            {
                break;
            }

            if (separator == index)
            {
                index++;

                continue;
            }

            var ancestor = full[..separator];

            try
            {
                if (File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.RootIsLink,
                        $"The ancestor '{ancestor}' of the payload root is a symbolic link; the reader never reaches the payload through a link.");

                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.FileUnreadable,
                    $"The ancestor '{ancestor}' of the payload root could not be inspected: {ex.Message}",
                    ex);

                return false;
            }

            index = separator + 1;
        }

        return true;
    }

    /// <summary>
    /// Validates the exact canonical file inventory with bounded state: every entry is
    /// checked against the expected name and type sets as it is enumerated, so the
    /// first unexpected entry rejects the payload before any unbounded collection can
    /// grow, and only expected, metadata-bounded names are ever retained for the
    /// missing-entry checks. Links are rejected before classification; enumeration
    /// failures fail closed.
    /// </summary>
    internal static bool TryValidateInventory(
        string root,
        GeneratedMapPayloadMetadata metadata,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var expectedSections = new HashSet<string>(StringComparer.Ordinal) { TerrainSection, MaterialsSection, MasksSection };
        var expectedSectionFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [TerrainSection] = [],
            [MaterialsSection] = [],
            [MasksSection] = [],
        };

        foreach (var chunk in metadata.Chunks)
        {
            expectedSectionFiles[TerrainSection].Add(FileName(chunk.Terrain.Path));
            expectedSectionFiles[MaterialsSection].Add(FileName(chunk.Materials.Path));
            expectedSectionFiles[MasksSection].Add(FileName(chunk.Masks.Path));

            if (chunk.Imagery is { } imagery)
            {
                if (expectedSections.Add(ImagerySection))
                {
                    expectedSectionFiles[ImagerySection] = [];
                }

                expectedSectionFiles[ImagerySection].Add(FileName(imagery.Path));
            }
        }

        var expectedRootFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            PayloadChunkPaths.MetadataFileName,
            PayloadChunkPaths.ReportFileName,
        };
        var noDirectories = new HashSet<string>(StringComparer.Ordinal);

        if (!TryValidateEntries(root, expectedSections, expectedRootFiles, "payload inventory", diagnostics, cancellationToken, out var seenRootDirectories, out var seenRootFiles)
            || !HasNoMissing(seenRootDirectories, expectedSections, "directory", diagnostics)
            || !HasNoMissing(seenRootFiles, expectedRootFiles, "file", diagnostics))
        {
            return false;
        }

        foreach (var section in expectedSections.Order())
        {
            if (!TryValidateEntries(
                    Path.Combine(root, section),
                    noDirectories,
                    expectedSectionFiles[section],
                    $"'{section}' section",
                    diagnostics,
                    cancellationToken,
                    out _,
                    out var seenFiles)
                || !HasNoMissing(seenFiles, expectedSectionFiles[section], $"file in the '{section}' section", diagnostics))
            {
                return false;
            }
        }

        return true;
    }

    private static string FileName(string relativePath)
    {
        return Path.GetFileName(relativePath);
    }

    /// <summary>
    /// Enumerates one directory against the expected entry sets, rejecting any link
    /// first and then the first unexpected name or entry type before it is retained;
    /// the returned seen sets never grow beyond the expected, metadata-bounded names.
    /// </summary>
    private static bool TryValidateEntries(
        string directory,
        HashSet<string> expectedDirectories,
        HashSet<string> expectedFiles,
        string inventoryLabel,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken,
        out HashSet<string> seenDirectories,
        out HashSet<string> seenFiles)
    {
        seenDirectories = new HashSet<string>(StringComparer.Ordinal);
        seenFiles = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", InventoryEnumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(entry);
                var attributes = File.GetAttributes(entry);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    PayloadReaderDiagnostics.Error(
                        diagnostics,
                        PayloadReaderDiagnosticCodes.InventoryLink,
                        $"The inventory entry '{name}' under '{directory}' is a symbolic link; links are rejected before descent.");

                    return false;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!expectedDirectories.Contains(name))
                    {
                        UnexpectedEntry(diagnostics, inventoryLabel, "directory", name);

                        return false;
                    }

                    seenDirectories.Add(name);
                }
                else
                {
                    if (!expectedFiles.Contains(name))
                    {
                        UnexpectedEntry(diagnostics, inventoryLabel, "file", name);

                        return false;
                    }

                    seenFiles.Add(name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PayloadReaderDiagnostics.Error(
                diagnostics,
                PayloadReaderDiagnosticCodes.FileUnreadable,
                $"The directory '{directory}' could not be enumerated: {ex.Message}",
                ex);

            return false;
        }

        return true;
    }

    private static void UnexpectedEntry(GeoDiagnosticBag diagnostics, string inventoryLabel, string kind, string name)
    {
        PayloadReaderDiagnostics.Error(
            diagnostics,
            PayloadReaderDiagnosticCodes.InventoryMismatch,
            $"The {inventoryLabel} holds the unexpected {kind} '{name}'.");
    }

    private static bool HasNoMissing(HashSet<string> seen, HashSet<string> expected, string kind, GeoDiagnosticBag diagnostics)
    {
        foreach (var missing in expected.Order())
        {
            if (!seen.Contains(missing))
            {
                PayloadReaderDiagnostics.Error(
                    diagnostics,
                    PayloadReaderDiagnosticCodes.InventoryMismatch,
                    $"The payload inventory is missing the {kind} '{missing}'.");

                return false;
            }
        }

        return true;
    }
}
