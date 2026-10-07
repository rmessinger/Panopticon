using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.IO;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class ExternalModelViewModelTests : IDisposable
{
    private readonly string _root;

    public ExternalModelViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "panopticon-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "tank.gltf"), "{}");
        Directory.CreateDirectory(Path.Combine(_root, "anim"));
        File.WriteAllText(Path.Combine(_root, "anim", "walk.gltf"), "{}");
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    private static ExternalAssetCatalog CreateCatalog(
        params ExternalAnimationDescriptor[] animations)
    {
        return new ExternalAssetCatalog(
            "tank.gltf",
            ExternalAssetInspection.GltfFormatId,
            1, 2, 1, 3, 3, 1, 0, 0, 0,
            new(
                System.Numerics.Vector3.Zero,
                System.Numerics.Vector3.One,
                new System.Numerics.Vector3(0.5f),
                0.8660254f),
            [],
            [],
            animations,
            []);
    }

    private sealed class FakeInspector(Func<string, ExternalAssetCatalog?> inspect) : IExternalAssetInspector
    {
        public ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context)
        {
            return inspect(assetKey);
        }
    }

    private sealed class FakeMount : IExternalSourceMount
    {
        public FakeMount(string rootVirtualKey, ExternalAssetCatalog catalog)
        {
            RootVirtualKey = rootVirtualKey;
            FormatSelection = new ExternalFormatSelection(
                ExternalAssetInspection.GltfFormatId,
                GltfImportOptions.Default,
                new FakeInspector(_ => catalog));
            ImportContext = new AssetImportContext(new DiskFileSystem(Path.GetTempPath()), null);
        }

        public string RootVirtualKey { get; }

        public ExternalFormatSelection FormatSelection { get; }

        public AssetImportContext ImportContext { get; }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingMountFactory : IExternalAssetSourceMountFactory
    {
        private readonly Func<string, ExternalAssetCatalog> _catalogForFile;

        public RecordingMountFactory(Func<string, ExternalAssetCatalog> catalogForFile)
        {
            _catalogForFile = catalogForFile;
        }

        public List<string> CreatedFiles { get; } = [];

        public int CreateCalls => CreatedFiles.Count;

        public IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options)
        {
            CreatedFiles.Add(absoluteSelectedFile);
            return new FakeMount(Path.GetFileName(absoluteSelectedFile), _catalogForFile(absoluteSelectedFile));
        }
    }

    private sealed class FakeFileSelector : IExternalModelFileSelector
    {
        public Queue<string?> ModelResponses { get; } = new();

        public Queue<string?> AnimationResponses { get; } = new();

        public Task<string?> SelectModelFileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ModelResponses.Count > 0 ? ModelResponses.Dequeue() : null);
        }

        public Task<string?> SelectAnimationFileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(AnimationResponses.Count > 0 ? AnimationResponses.Dequeue() : null);
        }
    }

    private sealed class FakePreviewClient : IExternalModelPreviewClient
    {
        private readonly Func<ExternalModelRequest, Task<ExternalModelPreviewResult>> _execute;

        public FakePreviewClient(Func<ExternalModelRequest, Task<ExternalModelPreviewResult>> execute)
        {
            _execute = execute;
        }

        public List<ExternalModelRequest> Requests { get; } = [];

        public List<IExternalModelArtifactLease> Leases { get; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ExternalModelPreviewResult> ExecuteAsync(
            ExternalModelRequest request,
            IExternalModelArtifactLease lease,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Leases.Add(lease);
            LastCancellationToken = cancellationToken;
            return _execute(request);
        }
    }


    private static (ExternalModelViewModel ViewModel, RecordingMountFactory Factory, FakePreviewClient Client) Create(
        ExternalAssetCatalog? catalog = null,
        FakeFileSelector? selector = null,
        FakePreviewClient? client = null)
    {
        var factory = new RecordingMountFactory(_ => catalog ?? CreateCatalog());
        var store = new ExternalModelArtifactStore();
        var effectiveClient = client ?? new FakePreviewClient(_ => Task.FromResult(new ExternalModelPreviewResult(true, [])));
        var session = new ExternalModelSession(factory, store);
        var viewModel = new ExternalModelViewModel(
            session,
            store,
            selector ?? new FakeFileSelector(),
            new SynchronousMainThreadDispatcher(),
            effectiveClient);
        return (viewModel, factory, effectiveClient);
    }

    private async Task<ExternalModelViewModel> LoadedViewModelAsync(
        ExternalAssetCatalog? catalog = null,
        FakePreviewClient? client = null)
    {
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var (viewModel, _, _) = Create(catalog, selector, client);
        await viewModel.ChooseModelCommand.ExecuteAsync();
        Assert.Equal("Ready", viewModel.StateText);
        return viewModel;
    }

    [Fact]
    public async Task ChooseModel_InspectsAndPublishesSummary()
    {
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var (viewModel, factory, _) = Create(CreateCatalog(), selector);

        Assert.False(viewModel.RenderCommand.CanExecute(null));
        Assert.Empty(viewModel.SummaryLines);

        await viewModel.ChooseModelCommand.ExecuteAsync();

        Assert.Single(factory.CreatedFiles);
        Assert.Equal("Ready", viewModel.StateText);
        Assert.Equal("tank.gltf", viewModel.SourceDisplayName);
        Assert.Equal(9, viewModel.SummaryLines.Count);
        Assert.Contains(viewModel.SummaryLines, line => line.Contains("Nodes: 2"));
    }

    [Fact]
    public async Task SelectorCancellation_ChangesNothing()
    {
        var selector = new FakeFileSelector();
        var (viewModel, factory, _) = Create(CreateCatalog(), selector);

        await viewModel.ChooseModelCommand.ExecuteAsync();

        Assert.Empty(factory.CreatedFiles);
        Assert.Equal("Empty", viewModel.StateText);
    }

    [Fact]
    public async Task Warnings_StayVisible_AndRenderStaysEnabled()
    {
        var catalog = CreateCatalog();
        var warningCatalog = new ExternalAssetCatalog(
            catalog.SourceDisplayName,
            catalog.FormatId,
            catalog.SceneCount, catalog.NodeCount, catalog.PrimitiveCount,
            catalog.VertexCount, catalog.IndexCount, catalog.MaterialCount, catalog.ImageCount,
            catalog.SkinCount, catalog.JointCount,
            catalog.Bounds,
            catalog.RequiredFeatureIds,
            catalog.OptionalFeatureIds,
            catalog.Animations,
            [new ExternalAssetInspectionDiagnostic(
                "GLTF0001",
                ExternalInspectionDiagnosticSeverity.Warning,
                "optional extension ignored")]);

        var viewModel = await LoadedViewModelAsync(warningCatalog);

        Assert.True(viewModel.Eligibility.CanRender);
        Assert.True(viewModel.RenderCommand.CanExecute(null));
        var warning = Assert.Single(viewModel.Diagnostics);
        Assert.Equal(ExternalDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task Render_DisabledUntilEligibleAndIdle()
    {
        var viewModel = await LoadedViewModelAsync(CreateCatalog());

        Assert.True(viewModel.RenderCommand.CanExecute(null));
        Assert.True(viewModel.RenderEnabled);

        viewModel.NextImportOptions = new ExternalModelImportOptions(4f, null, null);
        await viewModel.ApplyImportOptionsCommand.ExecuteAsync();

        Assert.True(viewModel.RenderCommand.CanExecute(null));
        Assert.Equal(4f, viewModel.ImportOptions.UnitsPerMeter);
    }

    [Fact]
    public async Task FailedInspection_DisablesRender_AndShowsDiagnostics()
    {
        var factory = new RecordingMountFactory(_ =>
            throw new InvalidDataException("malformed document"));
        var store = new ExternalModelArtifactStore();
        var session = new ExternalModelSession(factory, store);
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var viewModel = new ExternalModelViewModel(
            session, store, selector, new SynchronousMainThreadDispatcher(),
            new FakePreviewClient(_ => Task.FromResult(new ExternalModelPreviewResult(true, []))));

        await viewModel.ChooseModelCommand.ExecuteAsync();

        Assert.Equal("Failed", viewModel.StateText);
        Assert.False(viewModel.RenderCommand.CanExecute(null));
        Assert.Contains(viewModel.Diagnostics, d => d.Code == "EXT002");
    }

    [Fact]
    public async Task ImportOptions_ReloadInspection_PreviewOptionsDoNot()
    {
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var (viewModel, factory, _) = Create(CreateCatalog(), selector);
        await viewModel.ChooseModelCommand.ExecuteAsync();
        Assert.Equal(1, factory.CreateCalls);

        viewModel.NextPreviewOptions = ExternalModelPreviewOptions.Default with { ModelYaw = 1.2f };
        await viewModel.ApplyPreviewOptionsCommand.ExecuteAsync();
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(1.2f, viewModel.PreviewOptions.ModelYaw);

        viewModel.NextImportOptions = new ExternalModelImportOptions(2.5f, null, null);
        await viewModel.ApplyImportOptionsCommand.ExecuteAsync();
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(2.5f, viewModel.ImportOptions.UnitsPerMeter);
    }

    [Fact]
    public async Task Render_BuildsRequestFromSessionState_AndAdoptsArtifact()
    {
        var captured = default(ExternalModelRequest);
        var client = new FakePreviewClient(request =>
        {
            captured = request;
            return Task.FromResult(new ExternalModelPreviewResult(true, []));
        });
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var (viewModel, _, recordingClient) = Create(CreateCatalog(), selector, client);

        await viewModel.ChooseModelCommand.ExecuteAsync();
        await viewModel.RenderCommand.ExecuteAsync();

        Assert.NotNull(captured);
        Assert.Equal(viewModel.Snapshot.Generation, captured!.Generation);
        Assert.Equal(_root, Path.GetDirectoryName(Path.GetFullPath(captured.ModelSource.AbsoluteRoot + Path.DirectorySeparatorChar)));
        Assert.Equal("tank.gltf", captured.ModelSource.VirtualKey);
        Assert.NotNull(captured.OutputImagePath);
        Assert.NotNull(captured.OutputReportPath);
        Assert.Equal(
            Path.GetDirectoryName(captured.OutputImagePath),
            Path.GetDirectoryName(captured.OutputReportPath));
        Assert.NotNull(viewModel.LatestPreviewImagePath);
        Assert.Equal(captured.OutputImagePath, viewModel.LatestPreviewImagePath);
        Assert.NotNull(viewModel.Snapshot.PreviewArtifact);
        Assert.Single(recordingClient.Leases);
    }

    [Fact]
    public async Task RenderFailure_SurfacesDiagnostics_AndDisposesLease()
    {
        var client = new FakePreviewClient(_ => Task.FromResult(new ExternalModelPreviewResult(
            false,
            [new ExternalModelDiagnostic(
                TestHarnessPreviewClient.NonZeroExitCode,
                ExternalDiagnosticSeverity.Error,
                ExternalDiagnosticStage.Render,
                ExternalDiagnosticSourceRole.Model,
                null,
                "process exited with code 5")])));
        var viewModel = await LoadedViewModelAsync(CreateCatalog(), client);

        await viewModel.RenderCommand.ExecuteAsync();

        Assert.Contains(viewModel.Diagnostics, d => d.Code == TestHarnessPreviewClient.NonZeroExitCode);
        Assert.Null(viewModel.LatestPreviewImagePath);
    }

    [Fact]
    public async Task RenderCancellation_SwallowsAsControlFlow()
    {
        var client = new FakePreviewClient(_ => throw new OperationCanceledException());
        var viewModel = await LoadedViewModelAsync(CreateCatalog(), client);

        await viewModel.RenderCommand.ExecuteAsync();

        Assert.Null(viewModel.LatestPreviewImagePath);
        Assert.DoesNotContain(viewModel.Diagnostics, d => d.Code == "EXT999");
        Assert.True(viewModel.RenderCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectAnimation_UsesStableKeys()
    {
        var key = "tank.gltf#animation=0";
        var viewModel = await LoadedViewModelAsync(
            CreateCatalog(new ExternalAnimationDescriptor(key, "drive", TimeSpan.FromSeconds(2), 1)));

        viewModel.SelectAnimation(key);

        Assert.NotNull(viewModel.AnimationPairing);
        Assert.Equal(key, viewModel.AnimationPairing!.SelectedKey);
        Assert.Contains(viewModel.Animations, a => a.Key == key);
    }

    [Fact]
    public async Task AnimationSourceSelection_ListsSeparateDocumentClips()
    {
        var animKey = "walk.gltf#animation=0";
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        selector.AnimationResponses.Enqueue(Path.Combine(_root, "anim", "walk.gltf"));
        var modelCatalog = CreateCatalog();
        var animCatalog = new ExternalAssetCatalog(
            "walk.gltf",
            ExternalAssetInspection.GltfFormatId,
            0, 1, 0, 0, 0, 0, 0, 0, 0,
            new(System.Numerics.Vector3.Zero, System.Numerics.Vector3.One, new System.Numerics.Vector3(0.5f), 0.866f),
            [],
            [],
            [new ExternalAnimationDescriptor(animKey, "walk", TimeSpan.FromSeconds(1), 2)],
            []);
        var factory = new RecordingMountFactory(file =>
            Path.GetFileName(file) == "walk.gltf" ? animCatalog : modelCatalog);
        var store = new ExternalModelArtifactStore();
        var session = new ExternalModelSession(factory, store);
        var viewModel = new ExternalModelViewModel(
            session, store, selector, new SynchronousMainThreadDispatcher(),
            new FakePreviewClient(_ => Task.FromResult(new ExternalModelPreviewResult(true, []))));

        await viewModel.ChooseModelCommand.ExecuteAsync();
        await viewModel.ChooseAnimationCommand.ExecuteAsync();

        Assert.Equal("walk.gltf", viewModel.AnimationSourceDisplayName);
        Assert.Contains(viewModel.Animations, a => a.Key == animKey);

        viewModel.SelectAnimation(animKey);
        Assert.Equal(ExternalAnimationSourceDocument.SeparateDocument, viewModel.AnimationPairing!.Document);
    }

    [Fact]
    public async Task Reset_RestoresDefaults()
    {
        var viewModel = await LoadedViewModelAsync(CreateCatalog());

        viewModel.NextImportOptions = new ExternalModelImportOptions(9f, null, null);
        await viewModel.ApplyImportOptionsCommand.ExecuteAsync();
        Assert.Equal(9f, viewModel.ImportOptions.UnitsPerMeter);

        await viewModel.ResetCommand.ExecuteAsync();

        Assert.Equal(ExternalModelImportOptions.Default.UnitsPerMeter, viewModel.ImportOptions.UnitsPerMeter);
        Assert.Equal(ExternalModelPreviewOptions.Default, viewModel.PreviewOptions);
        Assert.Equal("Ready", viewModel.StateText);
    }

    [Fact]
    public async Task CancelCommand_CancelsActiveRender()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakePreviewClient(async _ =>
        {
            await gate.Task;
            return new ExternalModelPreviewResult(true, []);
        });
        var viewModel = await LoadedViewModelAsync(CreateCatalog(), client);

        var renderTask = viewModel.RenderCommand.ExecuteAsync();
        ((System.Windows.Input.ICommand)viewModel.CancelCommand).Execute(null);
        gate.TrySetResult(true);
        await renderTask;

        Assert.True(client.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Disposal_Unsubscribes_AndDisposesSessionOnce()
    {
        var selector = new FakeFileSelector();
        selector.ModelResponses.Enqueue(Path.Combine(_root, "tank.gltf"));
        var (viewModel, _, _) = Create(CreateCatalog(), selector);
        await viewModel.ChooseModelCommand.ExecuteAsync();
        Assert.Equal("Ready", viewModel.StateText);

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Equal("Disposed", viewModel.StateText);
    }
}
