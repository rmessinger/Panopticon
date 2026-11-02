using System;
using System.Collections.Generic;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Payload.Writing;

namespace Panopticon.Geospatial.Payload;

/// <summary>
/// Publication orchestration for one standalone payload directory under the R2
/// contract (docs/roadmap/implementation/track-r-r2-payload.md): staged writes through
/// one <see cref="PayloadStagedFiles"/> inside a <see cref="PayloadDirectoryTransaction"/>,
/// then a final full <see cref="GeneratedMapPayloadReader"/> validation of the stage
/// before the transaction renames. No validation runs after the critical rename;
/// disposal removes only owned staging and always retains a prior-output backup.
/// Not thread-safe.
/// </summary>
public sealed class GeneratedMapPayloadWriter : IDisposable
{
    private readonly PayloadDirectoryTransaction _transaction;
    private readonly PayloadStagedFiles _stagedFiles;
    private readonly GeoDiagnosticBag _diagnostics;
    private readonly GeospatialImportLimits _limits;
    private bool _disposed;
    private bool _commitAttempted;
    private bool _committed;

    private GeneratedMapPayloadWriter(
        PayloadDirectoryTransaction transaction,
        PayloadStagedFiles stagedFiles,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits)
    {
        _transaction = transaction;
        _stagedFiles = stagedFiles;
        _diagnostics = diagnostics;
        _limits = limits;
    }

    /// <summary>
    /// Creates the transaction and its staged-file helper for one output directory.
    /// Returns null with typed diagnostics when the output cannot be staged; without
    /// force an existing output fails unchanged. Cancellation and argument errors
    /// propagate.
    /// </summary>
    public static GeneratedMapPayloadWriter? Create(
        string outputDirectory,
        IEnumerable<string> protectedInputPaths,
        GeoDiagnosticBag diagnostics,
        bool force = false,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var transaction = PayloadDirectoryTransaction.Create(
            outputDirectory, force, protectedInputPaths, diagnostics, cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        var resolvedLimits = limits ?? new GeospatialImportLimits();

        return new GeneratedMapPayloadWriter(
            transaction,
            new PayloadStagedFiles(transaction.StageDirectory, resolvedLimits, diagnostics),
            diagnostics,
            resolvedLimits);
    }

    /// <summary>Canonical output directory the committed payload is published to.</summary>
    public string OutputDirectory => _transaction.OutputDirectory;

    /// <summary>The transaction's owned staging directory; it stops existing after a successful commit.</summary>
    public string StageDirectory => _transaction.StageDirectory;

    /// <summary>Backup of the previous output retained after a force commit; null when none was moved.</summary>
    public string? RetainedBackupPath => _transaction.RetainedBackupPath;

    /// <summary>Preserved staging path reported when both the publish and its restore failed; null otherwise.</summary>
    public string? RecoveryStagePath => _transaction.RecoveryStagePath;

    /// <summary>Lifecycle state; stays readable after disposal for result construction.</summary>
    public GeneratedMapPayloadWriterStatus Status
    {
        get
        {
            if (_committed)
            {
                return GeneratedMapPayloadWriterStatus.Committed;
            }

            return _commitAttempted ? GeneratedMapPayloadWriterStatus.Failed : GeneratedMapPayloadWriterStatus.Staging;
        }
    }

    /// <summary>
    /// The single staged-file helper behind this writer. Generation and the importer
    /// stage through this exact instance so palette binding and the metadata hash stay
    /// consistent; constructing a second helper against the same stage is an error.
    /// </summary>
    internal PayloadStagedFiles StagedFiles => _stagedFiles;

    /// <summary>Encodes and writes one terrain chunk; null with a diagnostic on recoverable failure.</summary>
    public PayloadFileReference? WriteTerrain(TerrainChunk chunk, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteTerrain(chunk, cancellationToken);
    }

    /// <summary>Encodes and writes one material chunk; null with a diagnostic on recoverable failure.</summary>
    public PayloadFileReference? WriteMaterial(MaterialChunk chunk, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteMaterial(chunk, cancellationToken);
    }

    /// <summary>Encodes and writes one terrain mask chunk; null with a diagnostic on recoverable failure.</summary>
    public PayloadFileReference? WriteMask(TerrainMaskChunk chunk, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteMask(chunk, cancellationToken);
    }

    /// <summary>Writes one imagery chunk's encoded PNG; null with a diagnostic on recoverable failure.</summary>
    public PayloadFileReference? WriteImagery(ImageryChunk chunk, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteImagery(chunk, cancellationToken);
    }

    /// <summary>Streams payload.json and returns the SHA-256 of the actually written bytes; null with a diagnostic on recoverable failure.</summary>
    public string? WriteMetadata(GeneratedMapPayloadMetadata metadata, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteMetadata(metadata, cancellationToken);
    }

    /// <summary>Writes report.json bound to the actually written payload.json hash; the rewrite contract matches the staged-file helper.</summary>
    public bool WriteReport(GeneratedMapPayloadReport report, CancellationToken cancellationToken = default)
    {
        ThrowIfNotStaging();

        return _stagedFiles.WriteReport(report, cancellationToken);
    }

    /// <summary>
    /// Fully validates the staged payload with the accepted reader and, only when it is
    /// clean, runs the transaction commit. Returns false with typed diagnostics for any
    /// recoverable validation or IO failure; cancellation propagates and the owned
    /// staging is removed by disposal. Idempotent after success: a repeated call
    /// returns the cached outcome without reopening anything.
    /// </summary>
    public bool TryCommit(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_commitAttempted)
        {
            return _committed;
        }

        if (_diagnostics.HasErrors)
        {
            _diagnostics.Add(new GeoDiagnostic(
                PayloadWriterDiagnostics.CommitBlockedByJobErrors,
                GeoStage.Write,
                GeoSeverity.Error,
                "Publishing the payload is blocked because the job diagnostics already carry errors.",
                OutputDirectory));
            _commitAttempted = true;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var reader = GeneratedMapPayloadReader.Open(StageDirectory, _diagnostics, _limits, cancellationToken);

        if (reader is null)
        {
            _commitAttempted = true;
            return false;
        }

        // The reader is fully validated by Open; it is closed before the renames so no
        // handle crosses the critical section, and nothing is validated after it.
        reader.Dispose();

        _commitAttempted = true;
        _committed = _transaction.TryCommit(cancellationToken);

        return _committed;
    }

    /// <summary>Disposes the transaction, removing only staging this writer still owns; committed outputs and backups are never touched.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transaction.Dispose();
    }

    private void ThrowIfNotStaging()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_commitAttempted)
        {
            throw new InvalidOperationException(
                _committed
                    ? "The payload was already committed; the staging directory no longer belongs to this writer."
                    : "A commit was already attempted and failed; this writer can no longer stage files.");
        }
    }
}

/// <summary>Lifecycle of one payload publication.</summary>
public enum GeneratedMapPayloadWriterStatus
{
    /// <summary>Files can still be staged; the output directory is not yet published.</summary>
    Staging,

    /// <summary>The staged payload passed full reader validation and was published.</summary>
    Committed,

    /// <summary>A commit was attempted and did not publish; disposal removes the owned staging.</summary>
    Failed,
}
