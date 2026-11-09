#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Writes a whole map package atomically. The package is staged into a uniquely named
/// sibling directory on the same filesystem, fully written and verified (length, digest,
/// containment, links, manifest validation), and then committed by renames: an existing
/// target moves to a backup name, the staged directory moves into place, and the backup
/// is deleted. Any failure removes the staging directory, restores the backup, and
/// leaves the previous target intact, so no code path leaves a partially written
/// package at the target.
/// </summary>
public static class MapPackageDirectoryWriter
{
    private const string StagingMarker = ".staging-";

    private const string BackupMarker = ".backup-";

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static MapPackageWriteResult Write(string targetPackageRoot, MapPackageManifest manifest, IMapPackagePayloadProvider payloads)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(payloads);

        var diagnostics = new List<MapPackageDiagnostic>();

        if (!TryCanonicalizeTarget(targetPackageRoot, diagnostics, out var target))
        {
            return new MapPackageWriteResult(false, diagnostics);
        }

        if (string.Equals(Path.GetPathRoot(target), target, PathComparison))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.TargetPathInvalid,
                $"Package target '{target}' must not be a filesystem root."));
            return new MapPackageWriteResult(false, diagnostics);
        }

        if (File.Exists(target) && !Directory.Exists(target))
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.TargetPathInvalid,
                $"Package target '{target}' exists as a file, not a directory."));
            return new MapPackageWriteResult(false, diagnostics);
        }

        var parent = Path.GetDirectoryName(target)!;

        try
        {
            // Missing parents may be created; the target itself only ever appears by rename.
            Directory.CreateDirectory(parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.TargetPathInvalid,
                $"Creating the parent directory '{parent}' failed: {ex.Message}"));
            return new MapPackageWriteResult(false, diagnostics);
        }

        string stage;

        try
        {
            stage = NewUniqueSibling(target, StagingMarker);
            Directory.CreateDirectory(stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagingFailed,
                $"Creating the staging directory failed: {ex.Message}"));
            return new MapPackageWriteResult(false, diagnostics);
        }

        var commitStarted = false;
        var committed = false;
        string? backup = null;

        try
        {
            foreach (var entry in manifest.Layers)
            {
                var failure = StageLayerFile(stage, entry, payloads);

                if (failure is not null)
                {
                    diagnostics.Add(failure);
                    return new MapPackageWriteResult(false, diagnostics);
                }
            }

            foreach (var entry in manifest.RetainedSections)
            {
                var failure = StageRetainedFile(stage, entry, payloads);

                if (failure is not null)
                {
                    diagnostics.Add(failure);
                    return new MapPackageWriteResult(false, diagnostics);
                }
            }

            var manifestFailure = StageManifest(stage, manifest);

            if (manifestFailure is not null)
            {
                diagnostics.Add(manifestFailure);
                return new MapPackageWriteResult(false, diagnostics);
            }

            var verifyFailure = VerifyStaged(stage, manifest);

            if (verifyFailure is not null)
            {
                diagnostics.Add(verifyFailure);
                return new MapPackageWriteResult(false, diagnostics);
            }

            commitStarted = true;

            if (File.Exists(target) && !Directory.Exists(target))
            {
                diagnostics.Add(new MapPackageDiagnostic(
                    MapPackageChunkDiagnostic.TargetPathInvalid,
                    $"Package target '{target}' appeared as a file during staging; the previous package is left unchanged."));
                return new MapPackageWriteResult(false, diagnostics);
            }

            if (Directory.Exists(target))
            {
                backup = NewUniqueSibling(target, BackupMarker);
                Directory.Move(target, backup);
            }

            Directory.Move(stage, target);
            committed = true;

            if (backup is not null)
            {
                DeleteDirectoryBestEffort(backup, diagnostics);
                backup = null;
            }
        }
        catch (Exception ex)
        {
            var code = commitStarted ? MapPackageChunkDiagnostic.CommitFailed : MapPackageChunkDiagnostic.StagingFailed;
            diagnostics.Add(new MapPackageDiagnostic(code, $"Writing the package to '{target}' failed: {ex.Message}"));
            return new MapPackageWriteResult(false, diagnostics);
        }
        finally
        {
            if (!committed)
            {
                DeleteDirectoryBestEffort(stage, diagnostics);
                RestoreBackupIfTargetMissing(target, backup, diagnostics);
            }
        }

        return new MapPackageWriteResult(true, diagnostics);
    }

    private static bool TryCanonicalizeTarget(string targetPackageRoot, List<MapPackageDiagnostic> diagnostics, out string target)
    {
        try
        {
            target = Path.GetFullPath(targetPackageRoot);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.TargetPathInvalid,
                $"Package target path '{targetPackageRoot}' is invalid or unresolvable: {ex.Message}"));
            target = string.Empty;
            return false;
        }
    }

    private static MapPackageDiagnostic? StageLayerFile(string stage, MapPackageLayerEntry? entry, IMapPackagePayloadProvider payloads)
    {
        if (entry is null)
        {
            return new MapPackageDiagnostic(
                MapPackageDiagnostic.NullComponent,
                "A layer entry is null; the package cannot be staged.");
        }

        byte[] encoded;

        try
        {
            encoded = entry.LayerKind switch
            {
                MapPackageFormat.LayerKindHeights => MapPackageChunkCodec.EncodeHeights(payloads.GetHeights(entry)),
                MapPackageFormat.LayerKindMaterials => MapPackageChunkCodec.EncodeMaterials(payloads.GetMaterials(entry)),
                MapPackageFormat.LayerKindMasks => MapPackageChunkCodec.EncodeMasks(payloads.GetMasks(entry)),
                MapPackageFormat.LayerKindImagery => MapPackageChunkCodec.EncodeImagery(payloads.GetImagery(entry)),
                _ => throw new InvalidOperationException($"Unknown layer kind '{entry.LayerKind}'."),
            };
        }
        catch (Exception ex)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.PayloadProviderFailed,
                $"Staging payload data for layer entry ({entry.ChunkX}, {entry.ChunkY}, {entry.LayerKind}) failed: {ex.Message}");
        }

        return WriteStagedFile(stage, entry.Path, encoded);
    }

    private static MapPackageDiagnostic? StageRetainedFile(string stage, MapPackageRetainedEntry? entry, IMapPackagePayloadProvider payloads)
    {
        if (entry is null)
        {
            return new MapPackageDiagnostic(
                MapPackageDiagnostic.NullComponent,
                "A retained section entry is null; the package cannot be staged.");
        }

        byte[] encoded;

        try
        {
            encoded = MapPackageChunkCodec.EncodeRetained(payloads.GetRetainedSection(entry));
        }
        catch (Exception ex)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.PayloadProviderFailed,
                $"Staging payload data for retained section '{entry.SectionName}' failed: {ex.Message}");
        }

        return WriteStagedFile(stage, entry.Path, encoded);
    }

    private static MapPackageDiagnostic? StageManifest(string stage, MapPackageManifest manifest)
    {
        try
        {
            File.WriteAllText(Path.Combine(stage, MapPackageFormat.ManifestFileName), MapPackageManifestJson.Serialize(manifest));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagingFailed,
                $"Writing '{MapPackageFormat.ManifestFileName}' failed: {ex.Message}");
        }
    }

    private static MapPackageDiagnostic? WriteStagedFile(string stage, string entryPath, byte[] encoded)
    {
        if (!MapPackageFileGuard.TryResolve(stage, entryPath, out var fullPath, out var guardError))
        {
            return guardError;
        }

        try
        {
            EnsureStagedParentDirectories(fullPath, stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagingFailed,
                $"Preparing the staged directories for '{entryPath}' failed: {ex.Message}");
        }

        try
        {
            // CreateNew refuses to write through any existing entry, links included; the
            // staged tree is fresh, so an existing entry can only mean an attacker or a bug.
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(encoded);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagingFailed,
                $"Writing staged file '{entryPath}' failed: {ex.Message}");
        }
    }

    private static void EnsureStagedParentDirectories(string fullPath, string stage)
    {
        var relative = fullPath[stage.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = stage;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = Path.Combine(current, segments[i]);

            if (TryResolveLinkTarget(current) is not null)
            {
                throw new IOException($"Staged subdirectory '{current}' is a symbolic link.");
            }

            if (!Directory.Exists(current))
            {
                Directory.CreateDirectory(current);

                // A link can appear between the check and the create; the created entry is
                // re-checked so a followed link is rejected before anything is written.
                if (TryResolveLinkTarget(current) is not null)
                {
                    throw new IOException($"Staged subdirectory '{current}' was created through a symbolic link.");
                }
            }
        }
    }

    private static MapPackageDiagnostic? VerifyStaged(string stage, MapPackageManifest manifest)
    {
        foreach (var entry in manifest.Layers)
        {
            if (entry is null)
            {
                return new MapPackageDiagnostic(MapPackageDiagnostic.NullComponent, "A layer entry is null; the staged package cannot be verified.");
            }

            var failure = VerifyStagedFile(stage, entry.Path, entry.ByteLength, entry.Sha256);

            if (failure is not null)
            {
                return failure;
            }
        }

        foreach (var entry in manifest.RetainedSections)
        {
            if (entry is null)
            {
                return new MapPackageDiagnostic(MapPackageDiagnostic.NullComponent, "A retained section entry is null; the staged package cannot be verified.");
            }

            var failure = VerifyStagedFile(stage, entry.Path, entry.ByteLength, entry.Sha256);

            if (failure is not null)
            {
                return failure;
            }
        }

        return VerifyStagedManifest(stage);
    }

    private static MapPackageDiagnostic? VerifyStagedFile(string stage, string entryPath, long expectedByteLength, string expectedSha256)
    {
        if (!MapPackageFileGuard.TryResolve(stage, entryPath, out var fullPath, out var guardError))
        {
            return guardError;
        }

        var fileInfo = new FileInfo(fullPath);

        if (!fileInfo.Exists)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagedVerificationFailed,
                $"Staged file '{entryPath}' is missing after staging.");
        }

        if (fileInfo.Length != expectedByteLength)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.LengthMismatch,
                $"Staged file '{entryPath}' holds {fileInfo.Length} bytes; the manifest entry declares {expectedByteLength}.");
        }

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagedVerificationFailed,
                $"Reading staged file '{entryPath}' for verification failed: {ex.Message}");
        }

        var actualSha256 = ComputeSha256Hex(bytes);

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.DigestMismatch,
                $"Staged file '{entryPath}' hashes to {actualSha256}; the manifest entry declares {expectedSha256}.");
        }

        return null;
    }

    private static MapPackageDiagnostic? VerifyStagedManifest(string stage)
    {
        string json;

        try
        {
            json = File.ReadAllText(Path.Combine(stage, MapPackageFormat.ManifestFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagedVerificationFailed,
                $"Reading the staged '{MapPackageFormat.ManifestFileName}' failed: {ex.Message}");
        }

        var (parsed, diagnostics) = MapPackageManifestJson.ParseAndValidate(json);

        if (parsed is null || diagnostics.Count > 0)
        {
            var first = diagnostics.Count > 0 ? diagnostics[0].ToString() : "the staged manifest deserialized to null.";
            return new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.StagedManifestInvalid,
                $"The staged manifest is invalid: {first}");
        }

        return null;
    }

    private static void RestoreBackupIfTargetMissing(string target, string? backup, List<MapPackageDiagnostic> diagnostics)
    {
        if (backup is null || !Directory.Exists(backup) || Directory.Exists(target))
        {
            return;
        }

        try
        {
            Directory.Move(backup, target);
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.CommitFailed,
                $"The write failed; the previous package was restored unchanged at '{target}'."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Both copies are potential recovery data, so neither is removed.
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.RecoveryRequired,
                $"The write failed and restoring the previous package failed. Both are preserved for manual recovery: previous package '{backup}', staged package files remain in the parent of '{target}': {ex.Message}"));
        }
    }

    private static void DeleteDirectoryBestEffort(string path, List<MapPackageDiagnostic> diagnostics)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.CleanupFailed,
                $"Removing '{path}' failed; the directory is left behind for inspection: {ex.Message}"));
        }
    }

    private static string NewUniqueSibling(string target, string marker)
    {
        var parent = Path.GetDirectoryName(target)!;
        var name = Path.GetFileName(target);
        string candidate;

        do
        {
            candidate = Path.Combine(parent, name + marker + Guid.NewGuid().ToString("N"));
        }
        while (Directory.Exists(candidate) || File.Exists(candidate));

        return candidate;
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // Returns null only for an ordinary missing entry or a plain non-link; link resolution failures propagate.
    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
