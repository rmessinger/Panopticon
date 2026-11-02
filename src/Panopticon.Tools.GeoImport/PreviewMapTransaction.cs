using System;
using System.IO;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.Geospatial;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// Publication transaction of one preview `.map` (card 24 decisions 3, 4, 5, 9, 10): the
/// payload root and the textures file are read inputs and are never written; the output
/// file is `&lt;maps directory&gt;/&lt;canonical payload directory name&gt;.map`. The map is staged in
/// a unique sibling temp, the token is observed exactly once before the first critical
/// move and never between moves, a force replacement always retains the previous `.map`
/// as a backup, a failed final move restores it, and a failed restore preserves both with
/// reported recovery paths. Cleanup removes only the owned temp file.
/// </summary>
internal sealed class PreviewMapTransaction : IDisposable
{
    private readonly bool _force;
    private readonly GeoDiagnosticBag _diagnostics;
    private readonly string _tempPath;
    private bool _disposed;
    private bool _published;

    private PreviewMapTransaction(string payloadRoot, string texturesPath, string mapsDirectory, string targetPath, bool force, GeoDiagnosticBag diagnostics)
    {
        PayloadRoot = payloadRoot;
        TexturesPath = texturesPath;
        MapsDirectory = mapsDirectory;
        TargetPath = targetPath;
        _force = force;
        _diagnostics = diagnostics;
        _tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>Per-instance move seam for rollback and recovery tests; returns a fault to throw or null to move.</summary>
    internal Func<string, string, Exception?>? MoveFault { get; set; }

    internal string PayloadRoot { get; }

    internal string TexturesPath { get; }

    internal string MapsDirectory { get; }

    internal string TargetPath { get; }

    /// <summary>Backup of the replaced previous .map, always retained after a successful force publish.</summary>
    internal string? RetainedBackupPath { get; private set; }

    /// <summary>Preserved backup path after a failed publish whose restore also failed.</summary>
    internal string? RecoveryBackupPath { get; private set; }

    /// <summary>Preserved staged temp path after a failed publish whose restore also failed.</summary>
    internal string? RecoveryTempPath { get; private set; }

    /// <summary>
    /// Validates the read inputs and the output location before creating anything: the
    /// payload root and textures file must be real, non-link entries; the maps directory
    /// and target must not sit inside the payload root; the target must not alias the
    /// textures file; the payload directory basename must be a safe cross-platform file
    /// name, used verbatim without rewriting.
    /// </summary>
    internal static PreviewMapTransaction? Create(
        string payloadPath,
        string texturesPath,
        string mapsDirectoryPath,
        bool force,
        GeoDiagnosticBag diagnostics)
    {
        string payloadRoot;
        string textures;
        string mapsDirectory;

        try
        {
            payloadRoot = Path.GetFullPath(payloadPath);
            textures = Path.GetFullPath(texturesPath);
            mapsDirectory = Path.GetFullPath(mapsDirectoryPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Fail(diagnostics, GeoImportCliDiagnostics.PayloadInvalid, $"A preview path is invalid: {ex.Message}");
            return null;
        }

        var baseName = Path.GetFileName(payloadRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(baseName)
            || baseName == "."
            || baseName == ".."
            || baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Fail(
                diagnostics,
                GeoImportCliDiagnostics.PayloadInvalid,
                $"The payload directory name '{baseName}' is not a safe cross-platform map file name; it is used verbatim.");
            return null;
        }

        try
        {
            if (IsLink(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                Fail(diagnostics, GeoImportCliDiagnostics.PayloadInvalid, $"The payload directory is missing, not a directory, or a link: '{payloadRoot}'.");
                return null;
            }

            if (IsLink(textures) || !File.Exists(textures))
            {
                Fail(diagnostics, GeoImportCliDiagnostics.PayloadInvalid, $"The textures file is missing or a link: '{textures}'.");
                return null;
            }

            if (IsLink(mapsDirectory) || (Directory.Exists(mapsDirectory) && File.Exists(mapsDirectory)))
            {
                Fail(diagnostics, GeoImportCliDiagnostics.OutputRejected, $"The maps directory is a file or a link: '{mapsDirectory}'.");
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(diagnostics, GeoImportCliDiagnostics.OutputRejected, $"Inspecting the preview paths failed: {ex.Message}");
            return null;
        }

        var targetPath = Path.Combine(mapsDirectory, baseName + ".map");

        if (IsInside(targetPath, payloadRoot) || IsInside(mapsDirectory, payloadRoot))
        {
            Fail(
                diagnostics,
                GeoImportCliDiagnostics.OutputRejected,
                $"The output location lies inside the payload root, which is a read input: '{targetPath}' in '{payloadRoot}'.");
            return null;
        }

        if (string.Equals(targetPath, textures, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapsDirectory, textures, StringComparison.OrdinalIgnoreCase))
        {
            Fail(
                diagnostics,
                GeoImportCliDiagnostics.OutputRejected,
                $"The output location aliases the textures file, which is a read input: '{targetPath}'.");
            return null;
        }

        try
        {
            if (IsLink(targetPath))
            {
                Fail(diagnostics, GeoImportCliDiagnostics.OutputRejected, $"The output .map exists as a link and is never followed: '{targetPath}'.");
                return null;
            }

            if (!force && File.Exists(targetPath))
            {
                Fail(
                    diagnostics,
                    GeoImportCliDiagnostics.TargetExists,
                    $"The output .map already exists and force is disabled; the existing file is preserved: '{targetPath}'.");
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(diagnostics, GeoImportCliDiagnostics.OutputRejected, $"Inspecting the output .map failed: {ex.Message}");
            return null;
        }

        return new PreviewMapTransaction(payloadRoot, textures, mapsDirectory, targetPath, force, diagnostics);
    }

    /// <summary>
    /// Stages the map through a cancellation-aware seekable stream, then, behind a single
    /// cancellation gate, moves a force-replaced target to a retained backup and renames
    /// the temp into place, restoring the backup if the final move fails.
    /// </summary>
    internal bool TryPublish(MapFile mapFile, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Directory.CreateDirectory(MapsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(_diagnostics, GeoImportCliDiagnostics.WriteFailed, $"Creating the maps directory failed: {ex.Message}");
            return false;
        }

        try
        {
            using (var file = new FileStream(_tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var stream = new PreviewMapWriteStream(file, cancellationToken))
            {
                mapFile.WriteTo(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeleteOwnedTemp();
            Fail(_diagnostics, GeoImportCliDiagnostics.WriteFailed, $"Writing the staged .map failed: {ex.Message}", _tempPath, ex);
            return false;
        }
        catch (OperationCanceledException)
        {
            DeleteOwnedTemp();
            throw;
        }

        // The last cancellation gate: once the first move starts, the pair finishes or
        // restores without another check, and success is never re-observed.
        cancellationToken.ThrowIfCancellationRequested();

        string? backupPath = null;

        try
        {
            if (_force && File.Exists(TargetPath))
            {
                backupPath = TargetPath + ".backup-" + Guid.NewGuid().ToString("N");
                MoveOrFault(TargetPath, backupPath);
            }

            try
            {
                MoveOrFault(_tempPath, TargetPath);
            }
            catch (OperationCanceledException)
            {
                RestoreOrPreserve(backupPath);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                RestoreOrPreserve(backupPath, ex);
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeleteOwnedTemp();
            Fail(_diagnostics, GeoImportCliDiagnostics.WriteFailed, $"Moving the previous .map to its backup failed: {ex.Message}", TargetPath, ex);
            return false;
        }

        RetainedBackupPath = backupPath;
        _published = true;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_published && RecoveryTempPath is null)
        {
            DeleteOwnedTemp();
        }
    }

    private void RestoreOrPreserve(string? backupPath, Exception? publishFailure = null)
    {
        if (backupPath is null)
        {
            DeleteOwnedTemp();
            Fail(
                _diagnostics,
                GeoImportCliDiagnostics.PublishFailedRestored,
                $"Publishing the staged .map failed and no previous .map existed; the staged file was removed.{Describe(publishFailure)}");
            return;
        }

        try
        {
            MoveOrFault(backupPath, TargetPath);
        }
        catch (Exception restoreFailure) when (restoreFailure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Both copies are potential recovery data, so nothing is removed.
            RecoveryBackupPath = backupPath;
            RecoveryTempPath = _tempPath;
            Fail(
                _diagnostics,
                GeoImportCliDiagnostics.PublishRecoveryRequired,
                $"Publishing the staged .map failed and restoring the previous .map failed too. Both are preserved for manual recovery: previous .map '{backupPath}', staged .map '{_tempPath}'.{Describe(publishFailure)} {restoreFailure.Message}");
            return;
        }

        DeleteOwnedTemp();
        Fail(
            _diagnostics,
            GeoImportCliDiagnostics.PublishFailedRestored,
            $"Publishing the staged .map failed; the previous .map was restored unchanged.{Describe(publishFailure)}");
    }

    private static string Describe(Exception? failure)
    {
        return failure is null ? string.Empty : $" Cause: {failure.Message}";
    }

    private void MoveOrFault(string source, string destination)
    {
        var fault = MoveFault?.Invoke(source, destination);

        if (fault is not null)
        {
            throw fault;
        }

        File.Move(source, destination);
    }

    private void DeleteOwnedTemp()
    {
        try
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _diagnostics.Add(new GeoDiagnostic(
                "GEO-CLI-011",
                GeoStage.Write,
                GeoSeverity.Warning,
                $"Removing the staged .map '{_tempPath}' failed; it remains for manual cleanup.",
                _tempPath,
                ex));
        }
    }

    private static bool IsInside(string path, string ancestor)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return path.StartsWith(ancestor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }

    // Entry-level no-follow check; links deeper on the chain are not descended into.
    private static bool IsLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void Fail(GeoDiagnosticBag diagnostics, string code, string message, string? sourcePath = null, Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Write, GeoSeverity.Error, message, sourcePath, detail));
    }
}
