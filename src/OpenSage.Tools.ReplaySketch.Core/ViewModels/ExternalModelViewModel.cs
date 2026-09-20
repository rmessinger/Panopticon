#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.ViewModels;

/// <summary>
/// Core view model for the external Model tab: exposes source summary, structural counts,
/// animation descriptors, sorted diagnostics, options, eligibility, and the current preview
/// artifact over one <see cref="ExternalModelSession"/>, and drives selection, reload,
/// options, reset, render, and cancel through awaitable commands. No MAUI, TestHarness, or
/// graphics dependency; every file, process, artifact, and dispatch service is injectable.
/// </summary>
public sealed class ExternalModelViewModel : IDisposable, INotifyPropertyChanged
{
    private readonly ExternalModelSession _session;
    private readonly IExternalModelArtifactStore _artifactStore;
    private readonly IExternalModelFileSelector _fileSelector;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly IExternalModelPreviewClient _previewClient;
    private readonly List<ExternalModelDiagnostic> _previewDiagnostics = [];

    private string? _modelFilePath;
    private string? _animationFilePath;
    private ExternalModelSnapshot _snapshot = ExternalModelSnapshot.CreateEmpty(0);
    private int _lastObservedGeneration;
    private bool _disposed;
    private string? _latestPreviewImagePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExternalModelViewModel(
        ExternalModelSession session,
        IExternalModelArtifactStore artifactStore,
        IExternalModelFileSelector fileSelector,
        IMainThreadDispatcher dispatcher,
        IExternalModelPreviewClient previewClient)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _fileSelector = fileSelector ?? throw new ArgumentNullException(nameof(fileSelector));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _previewClient = previewClient ?? throw new ArgumentNullException(nameof(previewClient));

        _session.SnapshotChanged += OnSessionSnapshotChanged;

        ChooseModelCommand = new AsyncCommand(ChooseModelAsync);
        ReloadCommand = new AsyncCommand(ReloadAsync, () => _snapshot.State == ExternalModelSessionState.Ready);
        ChooseAnimationCommand = new AsyncCommand(ChooseAnimationAsync, () => _snapshot.State == ExternalModelSessionState.Ready);
        ClearAnimationCommand = new AsyncCommand(ClearAnimationAsync, () => _snapshot.State == ExternalModelSessionState.Ready);
        ApplyImportOptionsCommand = new AsyncCommand(
            ct => ApplyImportOptionsAsync(NextImportOptions ?? ExternalModelImportOptions.Default, ct));
        ApplyPreviewOptionsCommand = new AsyncCommand(
            ct => ApplyPreviewOptionsAsync(NextPreviewOptions ?? ExternalModelPreviewOptions.Default, ct));
        ResetCommand = new AsyncCommand(ResetAsync, () => _modelFilePath is not null);
        RenderCommand = new AsyncCommand(RenderAsync, CanRender);
        CancelCommand = new AsyncCommand(CancelAsync);
    }

    public AsyncCommand ChooseModelCommand { get; }

    public AsyncCommand ReloadCommand { get; }

    public AsyncCommand ChooseAnimationCommand { get; }

    public AsyncCommand ClearAnimationCommand { get; }

    public AsyncCommand ApplyImportOptionsCommand { get; }

    public AsyncCommand ApplyPreviewOptionsCommand { get; }

    public AsyncCommand ResetCommand { get; }

    public AsyncCommand RenderCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public ExternalModelSnapshot Snapshot => _snapshot;

    /// <summary>Import options staged for the next apply command.</summary>
    public ExternalModelImportOptions? NextImportOptions { get; set; }

    /// <summary>Preview options staged for the next apply command.</summary>
    public ExternalModelPreviewOptions? NextPreviewOptions { get; set; }

    public string StateText => _snapshot.State.ToString();

    public string? SourceDisplayName => _snapshot.SourceDisplayName;

    public string? AnimationSourceDisplayName => _snapshot.AnimationSourceDisplayName;

    public ExternalAnimationPairing? AnimationPairing => _snapshot.AnimationPairing;

    public IReadOnlyList<string> SummaryLines { get; private set; } = [];

    public IReadOnlyList<ExternalAnimationDescriptor> Animations { get; private set; } = [];

    public IReadOnlyList<ExternalModelDiagnostic> Diagnostics { get; private set; } = [];

    public ExternalModelDiagnostic? SelectedDiagnostic { get; private set; }

    public PreviewEligibility Eligibility => _snapshot.Eligibility;

    public ExternalModelImportOptions ImportOptions => _snapshot.ImportOptions;

    public ExternalModelPreviewOptions PreviewOptions => _snapshot.PreviewOptions;

    public bool Busy =>
        ChooseModelCommand.IsExecuting ||
        ReloadCommand.IsExecuting ||
        RenderCommand.IsExecuting ||
        ResetCommand.IsExecuting ||
        ChooseAnimationCommand.IsExecuting ||
        ApplyImportOptionsCommand.IsExecuting;

    public bool RenderEnabled => CanRender();

    public string? LatestPreviewImagePath => _latestPreviewImagePath;

    public long? LatestPreviewByteLength => _snapshot.PreviewArtifact?.ByteLength;

    private bool CanRender()
    {
        return _snapshot.State == ExternalModelSessionState.Ready &&
            _snapshot.Eligibility.CanRender &&
            !Busy;
    }

    public void SelectDiagnostic(ExternalModelDiagnostic? diagnostic)
    {
        SelectedDiagnostic = diagnostic;
        OnPropertyChanged(nameof(SelectedDiagnostic));
    }

    private async Task ChooseModelAsync(CancellationToken cancellationToken)
    {
        var selected = await _fileSelector.SelectModelFileAsync(cancellationToken).ConfigureAwait(true);
        if (selected is null)
        {
            return;
        }

        _previewDiagnostics.Clear();
        _modelFilePath = selected;
        await _session.LoadModelAsync(selected, cancellationToken).ConfigureAwait(true);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _session.ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task ChooseAnimationAsync(CancellationToken cancellationToken)
    {
        var selected = await _fileSelector.SelectAnimationFileAsync(cancellationToken).ConfigureAwait(true);
        if (selected is null)
        {
            return;
        }

        _animationFilePath = selected;
        await _session.LoadAnimationAsync(selected, cancellationToken).ConfigureAwait(true);
    }

    private async Task ClearAnimationAsync(CancellationToken cancellationToken)
    {
        _animationFilePath = null;
        await Task.CompletedTask;
        _session.SelectAnimation(null);
    }

    public void SelectAnimation(string? selectedKey)
    {
        _session.SelectAnimation(selectedKey);
    }

    private async Task ApplyImportOptionsAsync(ExternalModelImportOptions options, CancellationToken cancellationToken)
    {
        await _session.SetImportOptionsAsync(options, cancellationToken).ConfigureAwait(true);
        NextImportOptions = null;
        OnPropertyChanged(nameof(NextImportOptions));
    }

    private async Task ApplyPreviewOptionsAsync(ExternalModelPreviewOptions options, CancellationToken cancellationToken)
    {
        _session.SetPreviewOptions(options);
        NextPreviewOptions = null;
        OnPropertyChanged(nameof(NextPreviewOptions));
        await Task.CompletedTask;
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        _previewDiagnostics.Clear();
        await _session.ResetToDefaultsAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task RenderAsync(CancellationToken cancellationToken)
    {
        if (_modelFilePath is null)
        {
            return;
        }

        var lease = _artifactStore.CreateLease();
        var request = BuildRequest(lease);
        ExternalModelPreviewResult result;

        try
        {
            result = await _previewClient.ExecuteAsync(request, lease, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            lease.Dispose();
            OnPropertyChanged(nameof(LatestPreviewImagePath));
            return;
        }
        catch (Exception exception)
        {
            lease.Dispose();
            _previewDiagnostics.Add(ExternalModelDiagnosticMapper.Map(
                ExternalDiagnosticStage.Render,
                ExternalDiagnosticSourceRole.Model,
                null,
                exception));
            RefreshDiagnostics();
            return;
        }

        if (!result.Succeeded)
        {
            lease.Dispose();
            _previewDiagnostics.Clear();
            _previewDiagnostics.AddRange(result.Diagnostics);
            RefreshDiagnostics();
            return;
        }

        var byteLength = request.OutputImagePath is { } imagePath && File.Exists(imagePath)
            ? new FileInfo(imagePath).Length
            : 0;

        if (!_session.AdoptPreviewArtifact(_snapshot.Generation, lease, byteLength))
        {
            return;
        }

        _latestPreviewImagePath = request.OutputImagePath;
        _previewDiagnostics.Clear();
        RefreshDiagnostics();
        OnPropertyChanged(nameof(LatestPreviewImagePath));
        OnPropertyChanged(nameof(LatestPreviewByteLength));
    }

    private ExternalModelRequest BuildRequest(IExternalModelArtifactLease lease)
    {
        var preview = _snapshot.PreviewOptions;
        var animationSource = _animationFilePath is { } animationPath
            ? new ExternalModelSourceRequest(
                Path.GetDirectoryName(Path.GetFullPath(animationPath))!,
                Path.GetFileName(animationPath),
                ExternalAssetInspection.GltfFormatId)
            : null;

        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = _snapshot.Generation,
            ModelSource = new ExternalModelSourceRequest(
                Path.GetDirectoryName(Path.GetFullPath(_modelFilePath!))!,
                Path.GetFileName(_modelFilePath!),
                ExternalAssetInspection.GltfFormatId),
            AnimationSource = animationSource,
            ImportOptions = _snapshot.ImportOptions,
            PreviewOptions = preview,
            Viewport = new ExternalModelViewport(512, 512),
            OutputImagePath = _artifactStore is ExternalModelArtifactStore store
                ? store.GetCapturePath(lease)
                : null,
            OutputReportPath = _artifactStore is ExternalModelArtifactStore concrete
                ? concrete.GetReportPath(lease)
                : throw new InvalidOperationException(
                    "The artifact store must be the production store to derive output paths."),
        };
    }

    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        foreach (var command in new[]
                 {
                     ChooseModelCommand,
                     ReloadCommand,
                     ChooseAnimationCommand,
                     ResetCommand,
                     RenderCommand,
                 })
        {
            command.Cancel();
        }

        await Task.CompletedTask;
    }

    private void OnSessionSnapshotChanged(object? sender, ExternalModelSnapshot snapshot)
    {
        _ = _dispatcher.InvokeOnMainThreadAsync(() =>
        {
            if (snapshot.Generation < _lastObservedGeneration)
            {
                return;
            }

            _lastObservedGeneration = snapshot.Generation;
            _snapshot = snapshot;
            RefreshProjections();
        });
    }

    private void RefreshProjections()
    {
        var catalog = _snapshot.Catalog;
        SummaryLines = catalog is null
            ? []
            :
            [
                $"Scenes: {catalog.SceneCount}",
                $"Nodes: {catalog.NodeCount}",
                $"Primitives: {catalog.PrimitiveCount}",
                $"Vertices: {catalog.VertexCount}",
                $"Indices: {catalog.IndexCount}",
                $"Materials: {catalog.MaterialCount}",
                $"Images: {catalog.ImageCount}",
                $"Skins: {catalog.SkinCount} (joints: {catalog.JointCount})",
                $"Animations: {catalog.Animations.Count}",
            ];

        Animations = _session.GetAvailableAnimations().ToArray();

        RefreshDiagnostics();

        OnPropertyChanged(null);
        RenderCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        ChooseAnimationCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
    }

    private void RefreshDiagnostics()
    {
        Diagnostics = ExternalModelDiagnosticMapper.SortAndCollapse(
            _snapshot.Diagnostics.Concat(_previewDiagnostics));
        SelectedDiagnostic = Diagnostics.FirstOrDefault();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAsync(CancellationToken.None).GetAwaiter().GetResult();
        _session.Dispose();
        _session.SnapshotChanged -= OnSessionSnapshotChanged;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
