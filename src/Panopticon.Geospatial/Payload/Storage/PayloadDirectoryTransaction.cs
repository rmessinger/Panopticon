using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Panopticon.Geospatial.Payload.Storage;

/// <summary>
/// Staged publish transaction for one payload output directory. The output is only ever replaced by
/// renaming a unique sibling staging directory into place; pre-existing content is moved aside, never deleted.
/// </summary>
internal sealed class PayloadDirectoryTransaction : IDisposable
{
    private const int MaxLinkResolutionDepth = 16;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly GeoDiagnosticBag _diagnostics;
    private readonly IPayloadDirectoryIo _io;
    private readonly bool _force;
    private bool _disposed;
    private bool _commitAttempted;
    private bool _committed;
    private bool _stageOwned = true;

    internal string OutputDirectory { get; }

    internal string StageDirectory { get; }

    internal string? RetainedBackupPath { get; private set; }

    internal string? RecoveryStagePath { get; private set; }

    private PayloadDirectoryTransaction(
        string outputDirectory,
        string stageDirectory,
        bool force,
        GeoDiagnosticBag diagnostics,
        IPayloadDirectoryIo io)
    {
        OutputDirectory = outputDirectory;
        StageDirectory = stageDirectory;
        _force = force;
        _diagnostics = diagnostics;
        _io = io;
    }

    internal static PayloadDirectoryTransaction? Create(
        string outputDirectory,
        bool force,
        IEnumerable<string> protectedInputPaths,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        return Create(
            outputDirectory, force, protectedInputPaths, diagnostics, cancellationToken, BclPayloadDirectoryIo.Instance);
    }

    internal static PayloadDirectoryTransaction? Create(
        string outputDirectory,
        bool force,
        IEnumerable<string> protectedInputPaths,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken,
        IPayloadDirectoryIo io)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(protectedInputPaths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(io);
        cancellationToken.ThrowIfCancellationRequested();

        string canonicalOutput;

        try
        {
            canonicalOutput = Canonicalize(outputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            AddError(
                diagnostics,
                PayloadStorageDiagnostics.OutputPathInvalid,
                $"Output directory path is invalid or unresolvable: '{outputDirectory}'.",
                outputDirectory,
                ex);

            return null;
        }

        if (IsFileSystemRoot(canonicalOutput))
        {
            AddError(
                diagnostics,
                PayloadStorageDiagnostics.OutputPathInvalid,
                $"Output directory must not be a filesystem root: '{canonicalOutput}'.",
                canonicalOutput);

            return null;
        }

        var canonicalInputs = new List<string>();

        foreach (var inputPath in protectedInputPaths)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                AddError(
                    diagnostics,
                    PayloadStorageDiagnostics.ProtectedInputInvalid,
                    "Protected input path is null or empty.",
                    inputPath);

                return null;
            }

            string canonicalInput;

            try
            {
                canonicalInput = Canonicalize(inputPath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                AddError(
                    diagnostics,
                    PayloadStorageDiagnostics.ProtectedInputInvalid,
                    $"Protected input path is invalid or unresolvable: '{inputPath}'.",
                    inputPath,
                    ex);

                return null;
            }

            canonicalInputs.Add(canonicalInput);
        }

        foreach (var canonicalInput in canonicalInputs)
        {
            // Staging or backup renames must never relocate an input tree, so overlap in either direction is fatal.
            if (PathsOverlap(canonicalOutput, canonicalInput))
            {
                AddError(
                    diagnostics,
                    PayloadStorageDiagnostics.OutputOverlapsProtectedInput,
                    $"Output directory '{canonicalOutput}' overlaps protected input '{canonicalInput}'.",
                    canonicalOutput);

                return null;
            }
        }

        if (File.Exists(canonicalOutput) && !Directory.Exists(canonicalOutput))
        {
            AddError(
                diagnostics,
                PayloadStorageDiagnostics.OutputPathIsFile,
                $"Output path exists as a file, not a directory: '{canonicalOutput}'.",
                canonicalOutput);

            return null;
        }

        if (!force && Directory.Exists(canonicalOutput))
        {
            AddError(
                diagnostics,
                PayloadStorageDiagnostics.OutputDirectoryExists,
                $"Output directory already exists and force is disabled, leaving it unchanged: '{canonicalOutput}'.",
                canonicalOutput);

            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stageDirectory = UniqueSibling(canonicalOutput, ".tmp-");

        try
        {
            io.CreateDirectory(stageDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddError(
                diagnostics,
                PayloadStorageDiagnostics.StageCreateFailed,
                $"Creating the staging directory failed: '{stageDirectory}'.",
                stageDirectory,
                ex);

            return null;
        }

        return new PayloadDirectoryTransaction(canonicalOutput, stageDirectory, force, diagnostics, io);
    }

    internal bool TryCommit(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_commitAttempted)
        {
            return _committed;
        }

        _commitAttempted = true;

        var outputExists = Directory.Exists(OutputDirectory);

        if (File.Exists(OutputDirectory) && !outputExists)
        {
            AddError(
                _diagnostics,
                PayloadStorageDiagnostics.OutputPathIsFile,
                $"Output path exists as a file, not a directory: '{OutputDirectory}'.",
                OutputDirectory);

            return false;
        }

        if (outputExists && !_force)
        {
            AddError(
                _diagnostics,
                PayloadStorageDiagnostics.OutputDirectoryExists,
                $"Output directory appeared after staging; leaving it unchanged: '{OutputDirectory}'.",
                OutputDirectory);

            return false;
        }

        // Last cancellation gate: once the first rename starts, the transaction finishes or restores.
        // A throw here leaves the owned stage for the caller's using/finally to clean up.
        cancellationToken.ThrowIfCancellationRequested();

        string? backupPath = null;

        if (outputExists)
        {
            backupPath = UniqueSibling(OutputDirectory, ".backup-");

            try
            {
                _io.MoveDirectory(OutputDirectory, backupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AddError(
                    _diagnostics,
                    PayloadStorageDiagnostics.CommitFailed,
                    $"Moving the existing output to its backup failed; the output is unchanged: '{OutputDirectory}'.",
                    OutputDirectory,
                    ex);

                return false;
            }

            RetainedBackupPath = backupPath;
        }

        try
        {
            _io.MoveDirectory(StageDirectory, OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RollBackFailedCommit(backupPath, ex);
        }

        _committed = true;
        _stageOwned = false;

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_stageOwned)
        {
            return;
        }

        try
        {
            _io.DeleteDirectory(StageDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Add(new GeoDiagnostic(
                PayloadStorageDiagnostics.CleanupFailed,
                GeoStage.Write,
                GeoSeverity.Warning,
                $"Removing the staging directory failed; staged files remain at '{StageDirectory}'.",
                StageDirectory,
                ex));
        }
    }

    private bool RollBackFailedCommit(string? backupPath, Exception publishFailure)
    {
        if (backupPath is null)
        {
            AddError(
                _diagnostics,
                PayloadStorageDiagnostics.CommitFailed,
                $"Publishing the staged payload failed and no output existed before the commit: '{StageDirectory}'.",
                StageDirectory,
                publishFailure);

            return false;
        }

        try
        {
            _io.MoveDirectory(backupPath, OutputDirectory);
        }
        catch (Exception restoreFailure) when (restoreFailure is IOException or UnauthorizedAccessException)
        {
            // Both copies are potential recovery data, so disposal must not remove the stage.
            RecoveryStagePath = StageDirectory;
            _stageOwned = false;
            RetainedBackupPath = backupPath;
            AddError(
                _diagnostics,
                PayloadStorageDiagnostics.RecoveryRequired,
                $"Publishing the staged payload failed and restoring the previous output failed. Both are preserved for manual recovery: previous output '{backupPath}', staged payload '{StageDirectory}'.",
                StageDirectory,
                restoreFailure);

            return false;
        }

        RetainedBackupPath = null;
        AddError(
            _diagnostics,
            PayloadStorageDiagnostics.CommitFailed,
            $"Publishing the staged payload failed; the previous output was restored unchanged: '{OutputDirectory}'.",
            OutputDirectory,
            publishFailure);

        return false;
    }

    private static void AddError(
        GeoDiagnosticBag diagnostics,
        string code,
        string message,
        string? sourcePath = null,
        Exception? detail = null)
    {
        diagnostics.Add(new GeoDiagnostic(code, GeoStage.Write, GeoSeverity.Error, message, sourcePath, detail));
    }

    internal static string Canonicalize(string path)
    {
        return ResolveExistingLinks(Path.GetFullPath(path), 0);
    }

    private static string ResolveExistingLinks(string path, int depth)
    {
        if (depth > MaxLinkResolutionDepth)
        {
            throw new IOException($"Symbolic link chain is too deep at '{path}'.");
        }

        var root = Path.GetPathRoot(path)!;
        var current = root;

        foreach (var segment in path[root.Length..].Split(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Path.GetFullPath(Path.Combine(current, ".."));

                continue;
            }

            current = Path.Combine(current, segment);

            // Every component is link-checked, including non-existing ones: an ordinary missing
            // ancestor resolves to null, while inaccessible or dangling links fail closed below.
            var linkTarget = TryResolveLinkTarget(current);

            if (linkTarget is null)
            {
                continue;
            }

            var resolvedTarget = Path.GetFullPath(linkTarget, Path.GetDirectoryName(current)!);

            if (!Directory.Exists(resolvedTarget) && !File.Exists(resolvedTarget))
            {
                throw new IOException($"Symbolic link target does not exist: '{current}' -> '{resolvedTarget}'.");
            }

            current = ResolveExistingLinks(resolvedTarget, depth + 1);
        }

        return current;
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

    private static bool IsFileSystemRoot(string canonicalPath)
    {
        return string.Equals(Path.GetPathRoot(canonicalPath), canonicalPath, PathComparison);
    }

    private static bool PathsOverlap(string first, string second)
    {
        var a = TrimSeparators(first);
        var b = TrimSeparators(second);

        return string.Equals(a, b, PathComparison) || IsAncestor(a, b) || IsAncestor(b, a);
    }

    private static string TrimSeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsAncestor(string ancestor, string descendant)
    {
        return descendant.StartsWith(ancestor + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string UniqueSibling(string canonicalOutput, string suffixPrefix)
    {
        var parent = Path.GetDirectoryName(canonicalOutput)!;
        var name = Path.GetFileName(canonicalOutput);
        string candidate;

        do
        {
            candidate = Path.Combine(parent, name + suffixPrefix + Guid.NewGuid().ToString("N"));
        }
        while (Directory.Exists(candidate) || File.Exists(candidate));

        return candidate;
    }
}
