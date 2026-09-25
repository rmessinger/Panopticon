#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Content.Importing;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Graphics-free external model session: the single source of truth for the Model tab.
/// Source replacement cancels current work, increments a monotonic generation, disposes
/// prior mounts and artifact leases, and inspects the new source. Results carrying an
/// older generation are discarded. Snapshots are immutable and published only complete;
/// cancellation is control flow, never a diagnostic. Animation pairing selects by stable
/// C4 key from the model document or a separately mounted animation document; importer
/// option changes rerun inspection while preview-only changes republish without touching
/// sources.
/// </summary>
public sealed class ExternalModelSession : IDisposable
{
    private readonly object _gate = new();
    private readonly IExternalAssetSourceMountFactory _mountFactory;
    private readonly IExternalModelArtifactStore _artifactStore;

    private int _generation;
    private CancellationTokenSource? _operationCts;

    private string? _modelFilePath;
    private IExternalSourceMount? _modelMount;
    private string? _animationFilePath;
    private IExternalSourceMount? _animationMount;
    private ExternalAssetCatalog? _animationCatalog;

    private ExternalModelImportOptions _importOptions = ExternalModelImportOptions.Default;
    private ExternalModelPreviewOptions _previewOptions = ExternalModelPreviewOptions.Default;
    private ExternalAnimationPairing? _pairing;
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

    /// <summary>
    /// Union of the model document's and the separately mounted animation document's
    /// animation descriptors, keyed by their stable C4 keys.
    /// </summary>
    public IReadOnlyList<ExternalAnimationDescriptor> GetAvailableAnimations()
    {
        lock (_gate)
        {
            return
            [
                .. (_snapshot.Catalog?.Animations ?? []),
                .. (_animationCatalog?.Animations ?? []),
            ];
        }
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
    /// disposes prior mounts and artifact leases, clears source-specific state (including
    /// any separate-document pairing), publishes Inspecting, and inspects the new source.
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

        return await InspectModelAsync(generation, mount, token, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Re-inspects the current sources under a new generation with current importer
    /// options, re-creating the mounts. Used after importer-option changes.
    /// </summary>
    public Task<ExternalModelSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_modelFilePath is null)
            {
                throw new InvalidOperationException("No model source is loaded.");
            }

            return LoadModelAsync(_modelFilePath, cancellationToken);
        }
    }

    /// <summary>
    /// Replaces the optional animation source document. Cancels current work, increments
    /// the generation, disposes the old animation mount, inspects the new document, and
    /// clears a prior selection that referenced the replaced document.
    /// </summary>
    public async Task<ExternalModelSnapshot> LoadAnimationAsync(
        string absoluteSelectedFile,
        CancellationToken cancellationToken = default)
    {
        var (generation, mount, token) = BeginAnimationLoad(absoluteSelectedFile);

        if (mount is null)
        {
            return CurrentSnapshot;
        }

        ExternalAssetCatalog? catalog = null;
        Exception? failure = null;

        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken))
        {
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
                return CurrentSnapshot;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return _snapshot;
            }

            if (failure is not null || catalog is null)
            {
                var exception = failure ?? new InvalidDataException(
                    $"Animation document '{mount.RootVirtualKey}' was not found in its mount.");
                PublishLocked(FailedSnapshot(generation, exception, ExternalDiagnosticSourceRole.Animation));
                return _snapshot;
            }

            _animationCatalog = catalog;

            if (_pairing is { Document: ExternalAnimationSourceDocument.SeparateDocument } pairing &&
                !catalog.Animations.Any(a => a.Key == pairing.SelectedKey))
            {
                _pairing = null;
            }

            PublishLocked(_snapshot with
            {
                AnimationSourceDisplayName = catalog.SourceDisplayName,
                AnimationPairing = _pairing,
            });
            return _snapshot;
        }
    }

    /// <summary>
    /// Selects an animation by stable descriptor key: from the model document or from the
    /// separately mounted animation document. Null clears the selection. Throws
    /// InvalidDataException for unknown keys.
    /// </summary>
    public ExternalModelSnapshot SelectAnimation(string? selectedKey)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (selectedKey is null)
            {
                _pairing = null;
                _previewOptions = _previewOptions with { SelectedAnimationKey = null, AnimationSampleTime = 0 };
                PublishLocked(_snapshot with
                {
                    AnimationPairing = null,
                    PreviewOptions = _previewOptions,
                });
                return _snapshot;
            }

            var descriptor = FindDescriptor(selectedKey);
            if (descriptor is null)
            {
                throw new InvalidDataException($"Animation key '{selectedKey}' is not available.");
            }

            var document = _modelMount is not null && _snapshot.Catalog is { } modelCatalog &&
                modelCatalog.Animations.Any(a => a.Key == selectedKey)
                ? ExternalAnimationSourceDocument.SameDocument
                : ExternalAnimationSourceDocument.SeparateDocument;

            var sourceName = document == ExternalAnimationSourceDocument.SameDocument
                ? _snapshot.SourceDisplayName
                : _animationCatalog?.SourceDisplayName;

            _pairing = new ExternalAnimationPairing(document, sourceName, descriptor);
            _previewOptions = _previewOptions with
            {
                SelectedAnimationKey = selectedKey,
                AnimationSampleTime = 0,
            };
            _previewOptions.Validate(_importOptions, (float)descriptor.Duration.TotalSeconds);

            PublishLocked(_snapshot with
            {
                AnimationPairing = _pairing,
                PreviewOptions = _previewOptions,
            });
            return _snapshot;
        }
    }

    /// <summary>
    /// Applies new importer options and reruns inspection: mounts are re-created because
    /// unit scale is baked into the format selection. Animation pairing keys survive the
    /// reload; preview-only values are untouched.
    /// </summary>
    public async Task<ExternalModelSnapshot> SetImportOptionsAsync(
        ExternalModelImportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            _importOptions = options;

            if (_modelFilePath is null)
            {
                PublishLocked(_snapshot with { ImportOptions = options });
                return _snapshot;
            }
        }

        var snapshot = await LoadModelAsync(_modelFilePath!, cancellationToken).ConfigureAwait(false);

        if (_animationFilePath is not null &&
            !snapshot.FailedOrEmpty())
        {
            snapshot = await LoadAnimationAsync(_animationFilePath, cancellationToken).ConfigureAwait(false);
        }

        return snapshot;
    }

    /// <summary>
    /// Applies preview-only corrections; publishes a new snapshot without reopening or
    /// modifying source data. Invalid values throw before anything is applied.
    /// </summary>
    public ExternalModelSnapshot SetPreviewOptions(ExternalModelPreviewOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        lock (_gate)
        {
            ThrowIfDisposed();

            options.Validate(_importOptions, CurrentDurationLocked());

            _previewOptions = options;
            PublishLocked(_snapshot with { PreviewOptions = options });
            return _snapshot;
        }
    }

    /// <summary>
    /// Restores canonical C4 import scale and default preview values, clears the
    /// animation selection, and reruns inspection. Source files are never modified.
    /// </summary>
    public Task<ExternalModelSnapshot> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            _importOptions = ExternalModelImportOptions.Default;
            _previewOptions = ExternalModelPreviewOptions.Default;
            _pairing = null;

            PublishLocked(_snapshot with
            {
                ImportOptions = _importOptions,
                PreviewOptions = _previewOptions,
                AnimationPairing = null,
            });
        }

        return SetImportOptionsAsync(_importOptions, cancellationToken);
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
                _animationMount,
                _artifactLease,
                _artifactStore,
            ];

            _operationCts = null;
            _modelMount = null;
            _animationMount = null;
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

            _modelFilePath = absoluteSelectedFile;
            _modelMount?.Dispose();
            _modelMount = null;
            _animationMount?.Dispose();
            _animationMount = null;
            _animationCatalog = null;
            _artifactLease?.Dispose();
            _artifactLease = null;
            _pairing = null;

            PublishLocked(_snapshot with
            {
                Generation = generation,
                State = ExternalModelSessionState.Inspecting,
                SourceDisplayName = null,
                ImportOptions = _importOptions,
                Catalog = null,
                Diagnostics = [],
                Eligibility = PreviewEligibility.NotEvaluated,
                PreviewArtifact = null,
                AnimationSourceDisplayName = null,
                AnimationPairing = null,
            });

            IExternalSourceMount? mount = null;
            try
            {
                mount = _modelMount = _mountFactory.Create(absoluteSelectedFile, _importOptions);
                PublishLocked(_snapshot with
                {
                    Generation = generation,
                    SourceDisplayName = mount.RootVirtualKey,
                });
            }
            catch (Exception exception)
            {
                PublishLocked(FailedSnapshot(generation, exception, ExternalDiagnosticSourceRole.Model));
                return (generation, null, CancellationToken.None);
            }

            return (generation, mount, _operationCts.Token);
        }
    }

    private (int Generation, IExternalSourceMount? Mount, CancellationToken Token) BeginAnimationLoad(
        string absoluteSelectedFile)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            _operationCts?.Cancel();
            _operationCts?.Dispose();

            var generation = ++_generation;
            _operationCts = new CancellationTokenSource();

            _animationFilePath = absoluteSelectedFile;
            _animationMount?.Dispose();
            _animationMount = null;
            _animationCatalog = null;

            if (_pairing is { Document: ExternalAnimationSourceDocument.SeparateDocument })
            {
                _pairing = null;
                PublishLocked(_snapshot with
                {
                    AnimationSourceDisplayName = null,
                    AnimationPairing = null,
                });
            }

            IExternalSourceMount? mount = null;
            try
            {
                mount = _animationMount = _mountFactory.Create(absoluteSelectedFile, _importOptions);
            }
            catch (Exception exception)
            {
                PublishLocked(FailedSnapshot(generation, exception, ExternalDiagnosticSourceRole.Animation));
                return (generation, null, CancellationToken.None);
            }

            return (generation, mount, _operationCts.Token);
        }
    }

    private async Task<ExternalModelSnapshot> InspectModelAsync(
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
                PublishLocked(FailedSnapshot(generation, failure, ExternalDiagnosticSourceRole.Model, stage: ExternalDiagnosticStage.Parse));
                return _snapshot;
            }

            if (catalog is null)
            {
                var exception = new InvalidDataException(
                    $"Source document '{mount.RootVirtualKey}' was not found in its mount.");
                PublishLocked(FailedSnapshot(generation, exception, ExternalDiagnosticSourceRole.Model));
                return _snapshot;
            }

            var diagnostics = CatalogDiagnostics(catalog);
            PublishLocked(_snapshot with
            {
                Generation = generation,
                State = ExternalModelSessionState.Ready,
                Catalog = catalog,
                Diagnostics = diagnostics,
                Eligibility = PreviewEligibility.Evaluate(diagnostics),
            });
            return _snapshot;
        }
    }

    private ExternalAnimationDescriptor? FindDescriptor(string selectedKey)
    {
        var descriptor = _snapshot.Catalog?.Animations.FirstOrDefault(a => a.Key == selectedKey);
        descriptor ??= _animationCatalog?.Animations.FirstOrDefault(a => a.Key == selectedKey);
        return descriptor;
    }

    private float? CurrentDurationLocked()
    {
        return _pairing is { } pairing
            ? (float?)pairing.Descriptor.Duration.TotalSeconds
            : null;
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

    private ExternalModelSnapshot FailedSnapshot(
        int generation,
        Exception exception,
        ExternalDiagnosticSourceRole source,
        ExternalDiagnosticStage stage = ExternalDiagnosticStage.Selection)
    {
        var diagnostics = new[]
        {
            ExternalModelDiagnosticMapper.Map(stage, source, null, exception),
        };

        return _snapshot with
        {
            Generation = generation,
            State = ExternalModelSessionState.Failed,
            Catalog = null,
            Diagnostics = ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics),
            Eligibility = PreviewEligibility.Evaluate(diagnostics),
            PreviewArtifact = null,
        };
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

internal static class ExternalModelSnapshotExtensions
{
    public static bool FailedOrEmpty(this ExternalModelSnapshot snapshot)
    {
        return snapshot.State is ExternalModelSessionState.Failed or ExternalModelSessionState.Empty;
    }
}
