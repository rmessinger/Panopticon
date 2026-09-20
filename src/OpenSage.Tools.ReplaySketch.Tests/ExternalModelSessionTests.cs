#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.IO;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class ExternalModelSessionTests : IDisposable
{
    private readonly string _tempRoot;

    public ExternalModelSessionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "opensage-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    private static ExternalAssetCatalog CreateCatalog(
        string sourceDisplayName = "model.gltf",
        IReadOnlyList<ExternalAssetInspectionDiagnostic>? diagnostics = null)
    {
        return new ExternalAssetCatalog(
            sourceDisplayName,
            ExternalAssetInspection.GltfFormatId,
            1, 1, 1, 3, 3, 1, 0, 0, 0,
            new ExternalModelBounds(
                Vector3.Zero,
                Vector3.One,
                new Vector3(0.5f, 0.5f, 0.5f),
                0.8660254f),
            [],
            [],
            [],
            diagnostics ?? []);
    }

    private sealed class FakeInspector : IExternalAssetInspector
    {
        private readonly Func<string, ExternalAssetCatalog?> _inspect;

        public FakeInspector(Func<string, ExternalAssetCatalog?> inspect)
        {
            _inspect = inspect;
        }

        public ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context)
        {
            return _inspect(assetKey);
        }
    }

    private sealed class FakeMount : IExternalSourceMount
    {
        public FakeMount(string rootVirtualKey, IExternalAssetInspector inspector)
        {
            RootVirtualKey = rootVirtualKey;
            FormatSelection = new ExternalFormatSelection(
                ExternalAssetInspection.GltfFormatId,
                GltfImportOptions.Default,
                inspector);
            ImportContext = new AssetImportContext(new DiskFileSystem(Path.GetTempPath()), null);
        }

        public string RootVirtualKey { get; }

        public ExternalFormatSelection FormatSelection { get; }

        public AssetImportContext ImportContext { get; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeMountFactory : IExternalAssetSourceMountFactory
    {
        private readonly Func<string, IExternalSourceMount> _create;

        public FakeMountFactory(Func<string, IExternalSourceMount> create)
        {
            _create = create;
        }

        public int CreateCalls { get; private set; }

        public IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options)
        {
            CreateCalls++;
            return _create(absoluteSelectedFile);
        }
    }

    private sealed class FakeLease : IExternalModelArtifactLease
    {
        public string ArtifactId { get; } = Guid.NewGuid().ToString("N");

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeArtifactStore : IExternalModelArtifactStore
    {
        public bool IsDisposed { get; private set; }

        public IExternalModelArtifactLease CreateLease()
        {
            return new FakeLease();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private static ExternalModelSession CreateSession(
        Func<string, IExternalSourceMount> createMount,
        List<ExternalModelSnapshot>? events = null)
    {
        var session = new ExternalModelSession(
            new FakeMountFactory(createMount),
            new FakeArtifactStore());

        if (events is not null)
        {
            session.SnapshotChanged += (_, snapshot) => events.Add(snapshot);
        }

        return session;
    }

    private static string ModelPath(string fileName)
    {
        return Path.Combine("/models", fileName);
    }

    [Fact]
    public async Task LoadModel_SuccessfulInspection_PublishesCompleteReadySnapshot()
    {
        var events = new List<ExternalModelSnapshot>();
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog())),
            events);

        var snapshot = await session.LoadModelAsync(ModelPath("tank.gltf"));

        Assert.Equal(ExternalModelSessionState.Ready, snapshot.State);
        Assert.Equal(1, snapshot.Generation);
        Assert.Equal("tank.gltf", snapshot.SourceDisplayName);
        Assert.NotNull(snapshot.Catalog);
        Assert.True(snapshot.Eligibility.CanRender);
        Assert.Null(snapshot.PreviewArtifact);

        Assert.Equal(
            [
                ExternalModelSessionState.Inspecting,
                ExternalModelSessionState.Inspecting,
                ExternalModelSessionState.Ready,
            ],
            events.Select(e => e.State));
        Assert.All(events, e => Assert.Equal(1, e.Generation));
    }

    [Fact]
    public async Task LoadModel_WithWarnings_StaysReadyAndEligible()
    {
        var catalog = CreateCatalog(diagnostics:
        [
            new ExternalAssetInspectionDiagnostic(
                "GLTF0001",
                ExternalInspectionDiagnosticSeverity.Warning,
                "Optional glTF extension 'KHR_texture_transform' is not interpreted."),
        ]);
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => catalog)));

        var snapshot = await session.LoadModelAsync(ModelPath("tank.gltf"));

        Assert.Equal(ExternalModelSessionState.Ready, snapshot.State);
        Assert.True(snapshot.Eligibility.CanRender);
        var warning = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ExternalDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("GLTF0001", warning.Code);
    }

    [Fact]
    public async Task LoadModel_UnsupportedFormat_FailsSelection()
    {
        using var session = CreateSession(_ =>
            throw new InvalidDataException(
                $"{ExternalFormatSelectionResult.UnsupportedFormatCode}: 'decoy.obj' is unsupported."));

        var snapshot = await session.LoadModelAsync(ModelPath("decoy.obj"));

        Assert.Equal(ExternalModelSessionState.Failed, snapshot.State);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal("EXT002", diagnostic.Code);
        Assert.Contains(ExternalFormatSelectionResult.UnsupportedFormatCode, diagnostic.Message);
    }

    [Fact]
    public async Task LoadModel_MissingRootDocument_FailsSelection()
    {
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => null)));

        var snapshot = await session.LoadModelAsync(ModelPath("absent.gltf"));

        Assert.Equal(ExternalModelSessionState.Failed, snapshot.State);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal("EXT002", diagnostic.Code);
    }

    [Fact]
    public async Task LoadModel_InspectionFailure_MapsToParseStage()
    {
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ =>
                throw new InvalidDataException("malformed glTF document"))));

        var snapshot = await session.LoadModelAsync(ModelPath("broken.gltf"));

        Assert.Equal(ExternalModelSessionState.Failed, snapshot.State);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal("GLTF010", diagnostic.Code);
        Assert.False(snapshot.Eligibility.CanRender);
        Assert.Contains("GLTF010", snapshot.Eligibility.BlockingDiagnosticCodes);
    }

    [Fact]
    public async Task LoadModel_UnexpectedFailure_IsInternalFailure()
    {
        InvalidOperationException? captured = null;
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ =>
                throw (captured = new InvalidOperationException("worker crashed")))));

        var snapshot = await session.LoadModelAsync(ModelPath("boom.gltf"));

        Assert.Equal(ExternalModelSessionState.Failed, snapshot.State);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal(ExternalModelDiagnosticMapper.InternalFailureCode, diagnostic.Code);
        Assert.Same(captured, diagnostic.ExceptionDetail);
    }

    [Fact]
    public async Task ReplaceModel_DiscardsStaleCompletion()
    {
        var gateForA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeMount? mountA = null;
        var events = new List<ExternalModelSnapshot>();

        using var session = CreateSession(file =>
        {
            var inspector = Path.GetFileName(file) == "a.gltf"
                ? new FakeInspector(_ =>
                {
                    gateForA.Task.Wait();
                    return CreateCatalog("a.gltf");
                })
                : new FakeInspector(_ => CreateCatalog("b.gltf"));
            var mount = new FakeMount(Path.GetFileName(file), inspector);
            if (Path.GetFileName(file) == "a.gltf")
            {
                mountA = mount;
            }

            return mount;
        }, events);

        var loadA = session.LoadModelAsync(ModelPath("a.gltf"));
        var loadB = await session.LoadModelAsync(ModelPath("b.gltf"));

        Assert.Equal(ExternalModelSessionState.Ready, loadB.State);
        Assert.Equal(2, loadB.Generation);
        Assert.Equal("b.gltf", loadB.SourceDisplayName);

        gateForA.SetResult(true);
        var staleSnapshotA = await loadA;

        Assert.Equal(2, staleSnapshotA.Generation);
        Assert.Equal("b.gltf", staleSnapshotA.SourceDisplayName);
        Assert.Equal(loadB, session.CurrentSnapshot);
        Assert.DoesNotContain(events, e => e.State == ExternalModelSessionState.Failed);
        Assert.DoesNotContain(events, e => e.Catalog?.SourceDisplayName == "a.gltf" && e.State == ExternalModelSessionState.Ready);
    }

    [Fact]
    public async Task Cancellation_ProducesNoDiagnostic()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ =>
            {
                gate.Task.Wait();
                throw new OperationCanceledException();
            })));

        using var cts = new CancellationTokenSource();
        var load = session.LoadModelAsync(ModelPath("tank.gltf"), cts.Token);
        cts.Cancel();
        gate.TrySetResult(true);

        var snapshot = await load;

        Assert.DoesNotContain(session.CurrentSnapshot.Diagnostics ?? [], _ => true);
        Assert.NotEqual(ExternalModelSessionState.Failed, session.CurrentSnapshot.State);
    }

    [Fact]
    public async Task ReplaceModel_DisposesPriorMountAndArtifactLease()
    {
        FakeMount? firstMount = null;
        using var session = CreateSession(file =>
        {
            var mount = new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog()));
            firstMount ??= mount;
            return mount;
        });

        await session.LoadModelAsync(ModelPath("a.gltf"));
        var firstLease = new FakeLease();
        Assert.True(session.AdoptPreviewArtifact(1, firstLease, 42));

        await session.LoadModelAsync(ModelPath("b.gltf"));

        Assert.True(firstMount!.IsDisposed);
        Assert.True(firstLease.IsDisposed);
        Assert.Null(session.CurrentSnapshot.PreviewArtifact);
    }

    [Fact]
    public async Task AdoptPreviewArtifact_StaleGeneration_DisposesLease()
    {
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog())));

        await session.LoadModelAsync(ModelPath("a.gltf"));
        await session.LoadModelAsync(ModelPath("b.gltf"));

        var staleLease = new FakeLease();
        Assert.False(session.AdoptPreviewArtifact(1, staleLease, 42));
        Assert.True(staleLease.IsDisposed);
        Assert.Null(session.CurrentSnapshot.PreviewArtifact);
    }

    [Fact]
    public async Task AdoptPreviewArtifact_CurrentGeneration_ReplacesPriorLease()
    {
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog())));

        var snapshot = await session.LoadModelAsync(ModelPath("a.gltf"));

        var firstLease = new FakeLease();
        Assert.True(session.AdoptPreviewArtifact(snapshot.Generation, firstLease, 42));
        Assert.Equal((firstLease.ArtifactId, 42L),
            (session.CurrentSnapshot.PreviewArtifact!.ArtifactId,
            session.CurrentSnapshot.PreviewArtifact.ByteLength));

        var secondLease = new FakeLease();
        Assert.True(session.AdoptPreviewArtifact(snapshot.Generation, secondLease, 7));
        Assert.True(firstLease.IsDisposed);
        Assert.Equal(secondLease.ArtifactId, session.CurrentSnapshot.PreviewArtifact!.ArtifactId);
    }

    [Fact]
    public async Task ReloadAsync_WithoutSource_Throws()
    {
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReloadAsync());
    }

    [Fact]
    public async Task ReloadAsync_RecreatesMountWithCurrentOptions()
    {
        FakeMount? firstMount = null;
        var factory = new FakeMountFactory(file =>
        {
            var mount = new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog()));
            firstMount ??= mount;
            return mount;
        });
        using var session = new ExternalModelSession(factory, new FakeArtifactStore());

        var first = await session.LoadModelAsync(ModelPath("tank.gltf"));
        var second = await session.ReloadAsync();

        Assert.Equal(2, second.Generation);
        Assert.Equal(ExternalModelSessionState.Ready, second.State);
        Assert.Equal(first.SourceDisplayName, second.SourceDisplayName);
        Assert.True(firstMount!.IsDisposed);
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(2, session.CurrentSnapshot.Generation);
    }

    [Fact]
    public async Task Dispose_ReleasesEverythingOnce_AndRejectsLaterOperations()
    {
        FakeMount? mount = null;
        using var session = CreateSession(file =>
        {
            mount = new FakeMount(Path.GetFileName(file), new FakeInspector(_ => CreateCatalog()));
            return mount;
        });

        var snapshot = await session.LoadModelAsync(ModelPath("tank.gltf"));
        var lease = new FakeLease();
        session.AdoptPreviewArtifact(snapshot.Generation, lease, 42);

        session.Dispose();
        session.Dispose();

        Assert.True(mount!.IsDisposed);
        Assert.True(lease.IsDisposed);
        Assert.Equal(ExternalModelSessionState.Disposed, session.CurrentSnapshot.State);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LoadModelAsync(ModelPath("x.gltf")));
        Assert.Throws<ObjectDisposedException>(() => session.AdoptPreviewArtifact(1, new FakeLease(), 1));
    }

    [Fact]
    public async Task Dispose_DuringInspection_DiscardsCompletion()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<ExternalModelSnapshot>();
        using var session = CreateSession(
            file => new FakeMount(Path.GetFileName(file), new FakeInspector(_ =>
            {
                gate.Task.Wait();
                return CreateCatalog();
            })),
            events);

        var load = session.LoadModelAsync(ModelPath("tank.gltf"));
        session.Dispose();
        gate.SetResult(true);

        await load;

        Assert.Equal(ExternalModelSessionState.Disposed, session.CurrentSnapshot.State);
        Assert.Equal(ExternalModelSessionState.Disposed, events[^1].State);
        Assert.DoesNotContain(events, e => e.State == ExternalModelSessionState.Ready);
    }
}
