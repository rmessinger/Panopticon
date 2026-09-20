#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Graphics-free external model session: the single source of truth for the Model tab.
/// Source replacement cancels current work, increments a monotonic generation, disposes
/// prior mounts and artifact leases, and inspects the new source. Results carrying an
/// older generation are discarded. Snapshots are immutable and published only complete;
/// cancellation is control flow, never a diagnostic.
/// </summary>
public sealed class ExternalModelSession : IDisposable
{
    private readonly object _gate = new();
    private readonly IExternalAssetSourceMountFactory _mountFactory;
    private readonly IExternalModelArtifactStore _artifactStore;

    private int _generation;
    private CancellationTokenSource? _operationCts;
    private IExternalSourceMount? _modelMount;
    private IExternalModelArtifactLease? _artifactLease;
    private ExternalModelSnapshot _snapshot = ExternalModelSnapshot.CreateEmpty(0);
    private bool _disposed;

    /// <summary>Raised after every published snapshot, in publish order.</summary>
    public event EventHandler<ExternalModelSnapshot>? SnapshotChanged;

    public ExternalModelSession(
        IExternalAssetSourceMountFactory mountFactory,
        IExternalModelArtifactStore artifactStore)
    {
        _mountFactory = mountFactory ?? throw new ArgumentNullException(nameof(mountFactory));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    }

    public ExternalModelSnapshot CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Replaces the model source. Cancels current work, increments the generation,
    /// disposes the prior mount and artifact lease, publishes Inspecting, and inspects
    /// the new source. Returns the final snapshot for this operation; stale completions
    /// never alter current state.
    /// </summary>
    public async Task<ExternalModelSnapshot> LoadModelAsync(
        string absoluteSelectedFile,
        CancellationToken cancellationToken = default)
    {
        var (generation, mount, token) = BeginModelLoad(absoluteSelectedFile);

        if (mount is null)
        {
            return CurrentSnapshot;
        }

        return await InspectAsync(generation, mount, token, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Re-inspects the current model source under a new generation, keeping the existing
    /// mount. Used after importer-option changes.
    /// </summary>
    public async Task<ExternalModelSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        IExternalSourceMount mount;
        int generation;
        CancellationToken token;

        lock (_gate)
        {
            ThrowIfDisposed();

            mount = _modelMount!;
            if (mount is null)
            {
                throw new InvalidOperationException("No model source is loaded.");
            }

            generation = ++_generation;
            _operationCts = new CancellationTokenSource();
            token = _operationCts.Token;
            PublishLocked(InspectingSnapshot(generation, mount.RootVirtualKey));
        }

        return await InspectAsync(generation, mount, token, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adopts the preview artifact produced for one generation. Returns false and
    /// disposes the lease when the generation is stale.
    /// </summary>
    public bool AdoptPreviewArtifact(int generation, IExternalModelArtifactLease lease, long byteLength)
    {
        if (lease is null)
        {
            throw new ArgumentNullException(nameof(lease));
        }

        lock (_gate)
        {
            ThrowIfDisposed();

            if (generation != _generation)
            {
                lease.Dispose();
                return false;
            }

            _artifactLease?.Dispose();
            _artifactLease = lease;

            PublishLocked(_snapshot with
            {
                PreviewArtifact = new ExternalPreviewArtifactMetadata(lease.ArtifactId, byteLength),
            });

            return true;
        }
    }

    public void Dispose()
    {
        List<IDisposable> pending;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;

            _operationCts?.Cancel();
            pending =
            [
                _operationCts,
                _modelMount,
                _artifactLease,
                _artifactStore,
            ];

            _operationCts = null;
            _modelMount = null;
            _artifactLease = null;

            PublishLocked(_snapshot with { State = ExternalModelSessionState.Disposed });
        }

        foreach (var disposable in pending)
        {
            disposable?.Dispose();
        }
    }

    private (int Generation, IExternalSourceMount? Mount, CancellationToken Token) BeginModelLoad(
        string absoluteSelectedFile)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            _operationCts?.Cancel();
            _operationCts?.Dispose();

            var generation = ++_generation;
            _operationCts = new CancellationTokenSource();

            _modelMount?.Dispose();
            _modelMount = null;
            _artifactLease?.Dispose();
            _artifactLease = null;

            PublishLocked(ExternalModelSnapshot.CreateEmpty(generation) with
            {
                State = ExternalModelSessionState.Inspecting,
            });

            IExternalSourceMount? mount = null;
            try
            {
                mount = _modelMount = _mountFactory.Create(absoluteSelectedFile);
                PublishLocked(InspectingSnapshot(generation, mount.RootVirtualKey));
            }
            catch (Exception exception)
            {
                PublishLocked(FailedSnapshot(generation, null, DiagnosticsFor(
                    ExternalDiagnosticStage.Selection,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    exception)));
                return (generation, null, CancellationToken.None);
            }

            return (generation, mount, _operationCts.Token);
        }
    }

    private async Task<ExternalModelSnapshot> InspectAsync(
        int generation,
        IExternalSourceMount mount,
        CancellationToken internalToken,
        CancellationToken externalToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(internalToken, externalToken);

        ExternalAssetCatalog? catalog = null;
        Exception? failure = null;

        try
        {
            catalog = await Task.Run(
                () => mount.FormatSelection.Inspector.InspectModel(
                    mount.RootVirtualKey,
                    mount.ImportContext),
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is control flow, not a diagnostic. A replacement or dispose
            // already published a newer snapshot.
            return CurrentSnapshot;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return _snapshot;
            }

            if (failure is not null)
            {
                PublishLocked(FailedSnapshot(generation, mount.RootVirtualKey, DiagnosticsFor(
                    ExternalDiagnosticStage.Parse,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    failure)));
                return _snapshot;
            }

            if (catalog is null)
            {
                PublishLocked(FailedSnapshot(generation, mount.RootVirtualKey, DiagnosticsFor(
                    ExternalDiagnosticStage.Selection,
                    ExternalDiagnosticSourceRole.Model,
                    null,
                    new InvalidDataException(
                        $"Source document '{mount.RootVirtualKey}' was not found in its mount."))));
                return _snapshot;
            }

            var diagnostics = CatalogDiagnostics(catalog);
            PublishLocked(new ExternalModelSnapshot(
                generation,
                ExternalModelSessionState.Ready,
                mount.RootVirtualKey,
                catalog,
                diagnostics,
                PreviewEligibility.Evaluate(diagnostics),
                null));
            return _snapshot;
        }
    }

    private static IReadOnlyList<ExternalModelDiagnostic> CatalogDiagnostics(ExternalAssetCatalog catalog)
    {
        var warnings = catalog.Diagnostics
            .Select(d => new ExternalModelDiagnostic(
                d.Code,
                d.Severity == ExternalInspectionDiagnosticSeverity.Warning
                    ? ExternalDiagnosticSeverity.Warning
                    : ExternalDiagnosticSeverity.Info,
                ExternalDiagnosticStage.Parse,
                ExternalDiagnosticSourceRole.Model,
                null,
                d.Message))
            .ToList();

        return ExternalModelDiagnosticMapper.SortAndCollapse(warnings);
    }

    private static IReadOnlyList<ExternalModelDiagnostic> DiagnosticsFor(
        ExternalDiagnosticStage stage,
        ExternalDiagnosticSourceRole source,
        string? context,
        Exception exception)
    {
        return [ExternalModelDiagnosticMapper.Map(stage, source, context, exception)];
    }

    private static ExternalModelSnapshot InspectingSnapshot(int generation, string sourceDisplayName)
    {
        return new ExternalModelSnapshot(
            generation,
            ExternalModelSessionState.Inspecting,
            sourceDisplayName,
            null,
            [],
            PreviewEligibility.NotEvaluated,
            null);
    }

    private static ExternalModelSnapshot FailedSnapshot(
        int generation,
        string? sourceDisplayName,
        IReadOnlyList<ExternalModelDiagnostic> diagnostics)
    {
        return new ExternalModelSnapshot(
            generation,
            ExternalModelSessionState.Failed,
            sourceDisplayName,
            null,
            ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics),
            PreviewEligibility.Evaluate(diagnostics),
            null);
    }

    private void PublishLocked(ExternalModelSnapshot snapshot)
    {
        // Raised under the gate for strict publish order; Monitor reentrancy lets a
        // handler read CurrentSnapshot or start a new operation without deadlocking.
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExternalModelSession));
        }
    }
}
