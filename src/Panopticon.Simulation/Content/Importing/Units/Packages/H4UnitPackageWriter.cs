#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Content.Importing.Units;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Writes a resolved unit-content closure as an H4 unit package atomically. The package is staged
/// into a uniquely named sibling directory, fully written and verified (length, digest, containment,
/// links, manifest validation), and then committed by renames: an existing target moves to a backup
/// name, the staged directory moves into place, and the backup is deleted. Any failure removes the
/// staging directory, restores the backup, and leaves the previous target intact, so no code path
/// leaves a partially written package at the target.
/// Closures carrying error diagnostics are refused, so no partial package is ever produced.
/// Legacy-bound records retain the exact INI block bytes supplied by the payload provider, tagged
/// with the dialect identity the reader re-issues handles from; binding handles never appear in
/// package bytes.
/// </summary>
public static class H4UnitPackageWriter
{
    private const string StagingMarker = ".staging-";

    private const string BackupMarker = ".backup-";

    private const string RecordsDirectory = "records";

    private const string RetainedDirectory = "retained";

    private const string ManifestReservedName = "manifest.json";

    private const int MaxNameSegmentLength = 100;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static UnitPackageWriteResult Write(string targetPackageRoot, UnitImportResolution resolution)
    {
        return Write(targetPackageRoot, resolution, retainedPayloads: null, options: null);
    }

    public static UnitPackageWriteResult Write(
        string targetPackageRoot,
        UnitImportResolution resolution,
        IUnitPackageRetainedPayloadProvider? retainedPayloads = null,
        UnitPackageWriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPackageRoot);
        ArgumentNullException.ThrowIfNull(resolution);

        var diagnostics = new List<UnitPackageWriteDiagnostic>();

        var errors = resolution.Diagnostics.Where(diagnostic => diagnostic.IsError).ToList();

        if (errors.Count > 0)
        {
            var first = errors[0];
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.WriteRefused,
                $"The resolution carries {errors.Count} error diagnostic(s); refusing to write a partial package. First error [{first.Code}]: {first.Message}",
                first.Subject));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        var source = resolution.Source;

        if (source is null)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.WriteRefused,
                "The resolution carries no source identity; refusing to write."));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        if (resolution.Closure.Count > UnitPackageFormat.MaxRecordCount)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.RecordCountExceeded,
                $"The closure holds {resolution.Closure.Count} records and exceeds the limit of {UnitPackageFormat.MaxRecordCount}."));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        var plan = Plan(resolution, source, retainedPayloads, diagnostics);

        if (plan is null)
        {
            return new UnitPackageWriteResult(false, diagnostics);
        }

        return Commit(targetPackageRoot, plan, retainedPayloads, options);
    }

    private static WritePlan? Plan(
        UnitImportResolution resolution,
        ContentSource source,
        IUnitPackageRetainedPayloadProvider? retainedPayloads,
        List<UnitPackageWriteDiagnostic> diagnostics)
    {
        var seenIds = new HashSet<ContentId>();
        var seenPaths = new HashSet<string>(source.CaseSensitiveNames ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        seenPaths.Add(ManifestReservedName);

        var recordEntries = new List<UnitPackageRecordIndexEntry>(resolution.Closure.Count);
        var recordPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var retainedRecordIds = new List<ContentId>();

        foreach (var record in resolution.Closure)
        {
            if (!seenIds.Add(record.Id))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.RecordDuplicate,
                    $"Record '{record.Id}' appears more than once in the closure.",
                    record.Id));
                return null;
            }

            if (record.Id.Source is null || !string.Equals(record.Id.Source.Id, source.Id, StringComparison.Ordinal))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.SourceMismatch,
                    $"Record '{record.Id}' names source '{record.Id.Source?.Id ?? "null"}' but the resolution source is '{source.Id}'.",
                    record.Id));
                return null;
            }

            if (!IsValidNameSegment(record.Id.Name))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.NameInvalid,
                    $"Record name '{record.Id.Name}' is not safe for a package path segment.",
                    record.Id));
                return null;
            }

            if (!UnitPackageRecordPayloadWriter.TryWrite(record, source, out var payload, out var payloadDiagnostic))
            {
                diagnostics.Add(payloadDiagnostic);
                return null;
            }

            if (payload.Length > UnitPackageFormat.MaxPayloadFileBytes)
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.PayloadTooLarge,
                    $"Payload for record '{record.Id}' is {payload.Length} bytes and exceeds the limit of {UnitPackageFormat.MaxPayloadFileBytes}.",
                    record.Id));
                return null;
            }

            var relativePath = $"{RecordsDirectory}/{KindFolder(record.Id.Kind)}/{record.Id.Name}.json";

            if (!UnitPackagePath.TryValidatePath(relativePath, out var pathReason))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.PathInvalid,
                    $"Payload path for record '{record.Id}' is invalid: {pathReason}",
                    record.Id));
                return null;
            }

            if (!seenPaths.Add(relativePath))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.PathDuplicate,
                    $"Multiple package entries resolve to the same relative path '{relativePath}'.",
                    record.Id));
                return null;
            }

            recordEntries.Add(new UnitPackageRecordIndexEntry(
                record.Id.Name,
                record.Id.Kind,
                relativePath,
                payload.Length,
                UnitPackagePayloadFileReader.ComputeSha256Hex(payload)));
            recordPayloads[relativePath] = payload;

            if (RecordNeedsRetention(record))
            {
                if (!IsValidNameSegment(record.Id.Name))
                {
                    diagnostics.Add(new UnitPackageWriteDiagnostic(
                        UnitPackageWriteDiagnosticCodes.NameInvalid,
                        $"Retained payload path segment for record '{record.Id}' is not safe.",
                        record.Id));
                    return null;
                }

                retainedRecordIds.Add(record.Id);
            }
        }

        if (retainedRecordIds.Count > 0 && retainedPayloads is null)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.RetainedBlockMissing,
                $"{retainedRecordIds.Count} record(s) require retained INI blocks, but no payload provider was supplied; the write cannot retain source fidelity."));
            return null;
        }

        var roots = resolution.Roots.Count > 0
            ? resolution.Roots.Select(root => new UnitPackageRootRecordEntry(root.Name, root.Kind)).ToArray()
            : recordEntries.Select(entry => new UnitPackageRootRecordEntry(entry.Name, entry.Kind)).ToArray();

        return new WritePlan(source, roots, recordEntries, recordPayloads, retainedRecordIds);
    }

    private static bool RecordNeedsRetention(SourceRecord record)
    {
        if (record.Legacy is not null)
        {
            return true;
        }

        return record is UnitRecord unit && unit.Modules.Any(edit => edit.Module?.Legacy is not null);
    }

    private static UnitPackageWriteResult Commit(
        string targetPackageRoot,
        WritePlan plan,
        IUnitPackageRetainedPayloadProvider? retainedPayloads,
        UnitPackageWriteOptions? options)
    {
        var diagnostics = new List<UnitPackageWriteDiagnostic>();

        if (!TryCanonicalizeTarget(targetPackageRoot, diagnostics, out var target))
        {
            return new UnitPackageWriteResult(false, diagnostics);
        }

        if (string.Equals(Path.GetPathRoot(target), target, PathComparison))
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.TargetPathInvalid,
                $"Package target '{target}' must not be a filesystem root."));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        if (File.Exists(target) && !Directory.Exists(target))
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.TargetPathInvalid,
                $"Package target '{target}' exists as a file, not a directory."));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        var parent = Path.GetDirectoryName(target)!;

        try
        {
            // Missing parents may be created; the target itself only ever appears by rename.
            Directory.CreateDirectory(parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.TargetPathInvalid,
                $"Creating the parent directory '{parent}' failed: {ex.Message}"));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        string stage;

        try
        {
            stage = NewUniqueSibling(target, StagingMarker);
            Directory.CreateDirectory(stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagingFailed,
                $"Creating the staging directory failed: {ex.Message}"));
            return new UnitPackageWriteResult(false, diagnostics);
        }

        var commitStarted = false;
        var committed = false;
        string? backup = null;
        var retainedEntries = new List<UnitPackageRetainedPayloadEntry>();

        try
        {
            foreach (var (relativePath, payload) in plan.RecordPayloads)
            {
                var failure = StageFile(stage, relativePath, payload);

                if (failure is not null)
                {
                    diagnostics.Add(failure);
                    return new UnitPackageWriteResult(false, diagnostics);
                }
            }

            foreach (var recordId in plan.RetainedRecordIds)
            {
                var failure = StageRetainedBlock(stage, recordId, retainedPayloads, retainedEntries);

                if (failure is not null)
                {
                    diagnostics.Add(failure);
                    return new UnitPackageWriteResult(false, diagnostics);
                }
            }

            var manifestFailure = StageManifest(stage, plan, retainedEntries, options, diagnostics);

            if (manifestFailure is not null)
            {
                diagnostics.Add(manifestFailure);
                return new UnitPackageWriteResult(false, diagnostics);
            }

            var verifyFailure = VerifyStaged(stage, plan.RecordEntries, retainedEntries);

            if (verifyFailure is not null)
            {
                diagnostics.Add(verifyFailure);
                return new UnitPackageWriteResult(false, diagnostics);
            }

            commitStarted = true;

            if (File.Exists(target) && !Directory.Exists(target))
            {
                diagnostics.Add(new UnitPackageWriteDiagnostic(
                    UnitPackageWriteDiagnosticCodes.TargetPathInvalid,
                    $"Package target '{target}' appeared as a file during staging; the previous package is left unchanged."));
                return new UnitPackageWriteResult(false, diagnostics);
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
            var code = commitStarted ? UnitPackageWriteDiagnosticCodes.CommitFailed : UnitPackageWriteDiagnosticCodes.StagingFailed;
            diagnostics.Add(new UnitPackageWriteDiagnostic(code, $"Writing the package to '{target}' failed: {ex.Message}"));
            return new UnitPackageWriteResult(false, diagnostics);
        }
        finally
        {
            if (!committed)
            {
                DeleteDirectoryBestEffort(stage, diagnostics);
                RestoreBackupIfTargetMissing(target, backup, diagnostics);
            }
        }

        return new UnitPackageWriteResult(true, diagnostics, target);
    }

    private static UnitPackageWriteDiagnostic? StageRetainedBlock(
        string stage,
        ContentId recordId,
        IUnitPackageRetainedPayloadProvider? retainedPayloads,
        List<UnitPackageRetainedPayloadEntry> retainedEntries)
    {
        UnitPackageRetainedBlock? block;

        try
        {
            block = retainedPayloads!.GetBlock(new UnitRetainedBlockRequest(recordId));
        }
        catch (Exception ex)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.PayloadProviderFailed,
                $"The payload provider failed for record '{recordId}': {ex.Message}",
                recordId);
        }

        if (block is null || block.Bytes is null || block.Bytes.Length == 0)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.RetainedBlockMissing,
                $"No retained INI block was supplied for record '{recordId}'; the binding cannot be written without its source block.",
                recordId);
        }

        if (!string.Equals(block.Dialect, UnitPackageFormat.SupportedGeneralsIniDialect, StringComparison.Ordinal)
            || block.DialectVersion != UnitPackageFormat.SupportedGeneralsIniDialectVersion)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.RetainedDialectUnsupported,
                $"Retained block for record '{recordId}' declares dialect '{block.Dialect}' version {block.DialectVersion}; the reader supports '{UnitPackageFormat.SupportedGeneralsIniDialect}' version {UnitPackageFormat.SupportedGeneralsIniDialectVersion} only.",
                recordId);
        }

        if (block.Bytes.Length > UnitPackageFormat.MaxPayloadFileBytes)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.PayloadTooLarge,
                $"Retained block for record '{recordId}' is {block.Bytes.Length} bytes and exceeds the limit of {UnitPackageFormat.MaxPayloadFileBytes}.",
                recordId);
        }

        var relativePath = RetainedPath(recordId);

        if (!UnitPackagePath.TryValidatePath(relativePath, out var pathReason))
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.PathInvalid,
                $"Retained payload path for record '{recordId}' is invalid: {pathReason}",
                recordId);
        }

        var failure = StageFile(stage, relativePath, block.Bytes);

        if (failure is not null)
        {
            return failure;
        }

        retainedEntries.Add(new UnitPackageRetainedPayloadEntry(
            relativePath,
            block.Dialect,
            block.DialectVersion,
            block.Bytes.Length,
            UnitPackagePayloadFileReader.ComputeSha256Hex(block.Bytes),
            block.Provenance));

        return null;
    }

    private static UnitPackageWriteDiagnostic? StageManifest(
        string stage,
        WritePlan plan,
        List<UnitPackageRetainedPayloadEntry> retainedEntries,
        UnitPackageWriteOptions? options,
        List<UnitPackageWriteDiagnostic> diagnostics)
    {
        var manifest = new UnitPackageManifest(
            UnitPackageFormat.ContainerKind,
            UnitPackageFormat.CurrentSchemaVersion,
            new UnitPackageSourceIdentity(plan.Source.Id, plan.Source.CaseSensitiveNames),
            plan.Roots,
            plan.RecordEntries,
            retainedEntries,
            options?.Provenance);

        var json = UnitPackageManifestJsonWriter.Serialize(manifest);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (bytes.Length > UnitPackageFormat.MaxManifestBytes)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.PayloadTooLarge,
                $"The manifest is {bytes.Length} bytes and exceeds the limit of {UnitPackageFormat.MaxManifestBytes}.");
        }

        return StageFile(stage, UnitPackageFormat.ManifestFileName, bytes);
    }

    private static UnitPackageWriteDiagnostic? StageFile(string stage, string relativePath, byte[] bytes)
    {
        if (!UnitPackageFileGuard.TryResolve(stage, relativePath, out var fullPath, out var guardErrorCode, out var guardErrorMessage))
        {
            return new UnitPackageWriteDiagnostic(UnitPackageWriteDiagnosticCodes.StagingFailed, $"{guardErrorCode}: {guardErrorMessage}");
        }

        try
        {
            EnsureStagedParentDirectories(fullPath, stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagingFailed,
                $"Preparing the staged directories for '{relativePath}' failed: {ex.Message}");
        }

        try
        {
            // CreateNew refuses to write through any existing entry, links included; the
            // staged tree is fresh, so an existing entry can only mean an attacker or a bug.
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagingFailed,
                $"Writing staged file '{relativePath}' failed: {ex.Message}");
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

    private static UnitPackageWriteDiagnostic? VerifyStaged(
        string stage,
        IReadOnlyList<UnitPackageRecordIndexEntry> recordEntries,
        IReadOnlyList<UnitPackageRetainedPayloadEntry> retainedEntries)
    {
        foreach (var entry in recordEntries)
        {
            var failure = VerifyStagedFile(stage, entry.Path, entry.ByteLength, entry.Sha256);

            if (failure is not null)
            {
                return failure;
            }
        }

        foreach (var entry in retainedEntries)
        {
            var failure = VerifyStagedFile(stage, entry.Path, entry.ByteLength, entry.Sha256);

            if (failure is not null)
            {
                return failure;
            }
        }

        return VerifyStagedManifest(stage);
    }

    private static UnitPackageWriteDiagnostic? VerifyStagedFile(string stage, string entryPath, long expectedByteLength, string expectedSha256)
    {
        // The staged file is read back through the same guarded, length-checked, digest-checked
        // path the reader uses, so write-side verification cannot drift from read-side rules.
        if (!UnitPackagePayloadFileReader.TryReadVerifiedBytes(
            stage,
            entryPath,
            expectedByteLength,
            expectedSha256,
            out _,
            out var errorCode,
            out var errorMessage))
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagedVerificationFailed,
                $"Staged file '{entryPath}' failed verification: {errorCode}: {errorMessage}");
        }

        return null;
    }

    private static UnitPackageWriteDiagnostic? VerifyStagedManifest(string stage)
    {
        var manifestPath = Path.Combine(stage, UnitPackageFormat.ManifestFileName);
        var fileInfo = new FileInfo(manifestPath);

        if (!fileInfo.Exists)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagedVerificationFailed,
                $"Staged manifest '{UnitPackageFormat.ManifestFileName}' does not exist.");
        }

        if (fileInfo.Length > UnitPackageFormat.MaxManifestBytes)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.PayloadTooLarge,
                $"Staged manifest '{UnitPackageFormat.ManifestFileName}' length {fileInfo.Length} bytes exceeds the limit of {UnitPackageFormat.MaxManifestBytes}.");
        }

        string json;

        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagedVerificationFailed,
                $"Reading the staged '{UnitPackageFormat.ManifestFileName}' failed: {ex.Message}");
        }

        var (parsed, parseDiagnostics) = UnitPackageManifestReader.ParseAndValidate(json);

        if (parsed is null || parseDiagnostics.Count > 0)
        {
            var first = parseDiagnostics.Count > 0 ? parseDiagnostics[0].ToString() : "the staged manifest deserialized to null.";
            return new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.StagedManifestInvalid,
                $"The staged manifest is invalid: {first}");
        }

        return null;
    }

    private static bool TryCanonicalizeTarget(string targetPackageRoot, List<UnitPackageWriteDiagnostic> diagnostics, out string target)
    {
        try
        {
            target = Path.GetFullPath(targetPackageRoot);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.TargetPathInvalid,
                $"Package target path '{targetPackageRoot}' is invalid or unresolvable: {ex.Message}"));
            target = string.Empty;
            return false;
        }
    }

    private static void RestoreBackupIfTargetMissing(string target, string? backup, List<UnitPackageWriteDiagnostic> diagnostics)
    {
        if (backup is null || !Directory.Exists(backup) || Directory.Exists(target))
        {
            return;
        }

        try
        {
            Directory.Move(backup, target);
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.CommitFailed,
                $"The write failed; the previous package was restored unchanged at '{target}'."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Both copies are potential recovery data, so neither is removed.
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.RecoveryRequired,
                $"The write failed and restoring the previous package failed. Both are preserved for manual recovery: previous package '{backup}', staged package files remain in the parent of '{target}': {ex.Message}"));
        }
    }

    private static void DeleteDirectoryBestEffort(string path, List<UnitPackageWriteDiagnostic> diagnostics)
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
            diagnostics.Add(new UnitPackageWriteDiagnostic(
                UnitPackageWriteDiagnosticCodes.CleanupFailed,
                $"Removing '{path}' failed; the directory is left behind for inspection: {ex.Message}"));
        }
    }

    private static string RetainedPath(ContentId recordId)
    {
        var folder = KindFolder(recordId.Kind);

        return $"{RetainedDirectory}/{folder}/{recordId.Name}.ini";
    }

    private static string KindFolder(ContentKind kind) => kind switch
    {
        ContentKind.Unit => "units",
        ContentKind.Weapon => "weapons",
        ContentKind.Armor => "armors",
        ContentKind.Locomotor => "locomotors",
        ContentKind.Projectile => "projectiles",
        ContentKind.Ability => "abilities",
        ContentKind.Upgrade => "upgrades",
        ContentKind.VisualState => "visuals",
        _ => throw new InvalidOperationException($"Record kind {kind} has no package folder."),
    };

    private static bool IsValidNameSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameSegmentLength)
        {
            return false;
        }

        if (name is "." or "..")
        {
            return false;
        }

        if (name.StartsWith('.') || name.EndsWith('.'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c) || "<>:\"/\\|?*".Contains(c))
            {
                return false;
            }
        }

        return true;
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

    private sealed record WritePlan(
        ContentSource Source,
        IReadOnlyList<UnitPackageRootRecordEntry> Roots,
        IReadOnlyList<UnitPackageRecordIndexEntry> RecordEntries,
        IReadOnlyDictionary<string, byte[]> RecordPayloads,
        IReadOnlyList<ContentId> RetainedRecordIds);
}
