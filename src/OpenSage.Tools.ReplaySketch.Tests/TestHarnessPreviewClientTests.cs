#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class TestHarnessPreviewClientTests : IDisposable
{
    private readonly string _root;
    private readonly string _artifactDir;

    public TestHarnessPreviewClientTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "opensage-client-" + Guid.NewGuid().ToString("N"));
        _artifactDir = Path.Combine(_root, "artifacts");
        Directory.CreateDirectory(_artifactDir);
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
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

    private sealed class FakeProcess : IExternalModelProcess
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _stderr;

        public FakeProcess(string stderr = "", int exitCode = 0)
        {
            _stderr = stderr;
            _exit.SetResult(exitCode);
        }

        public bool Disposed { get; private set; }

        public int KillTreeCallCount { get; private set; }

        public Task<int> ExitTask => _exit.Task;

        public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_stderr);
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            return _exit.Task.WaitAsync(cancellationToken);
        }

        public void KillTree()
        {
            KillTreeCallCount++;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class FakeProcessFactory(Func<string, string, IExternalModelProcess> start)
        : IExternalModelProcessFactory
    {
        public IExternalModelProcess? LastProcess { get; private set; }

        public string? LastExecutablePath { get; private set; }

        public string? LastArguments { get; private set; }

        public int StartCalls { get; private set; }

        public IExternalModelProcess Start(string executablePath, string arguments)
        {
            StartCalls++;
            LastExecutablePath = executablePath;
            LastArguments = arguments;
            LastProcess = start(executablePath, arguments);
            return LastProcess;
        }
    }

    private ExternalModelRequest CreateRequest(int generation = 5, string? image = "preview.png")
    {
        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = generation,
            ModelSource = new ExternalModelSourceRequest(_root, "tank.gltf", ExternalAssetInspection.GltfFormatId),
            ImportOptions = ExternalModelImportOptions.Default,
            PreviewOptions = ExternalModelPreviewOptions.Default,
            Viewport = new ExternalModelViewport(32, 32),
            OutputImagePath = image is null ? null : Path.Combine(_artifactDir, image),
            OutputReportPath = Path.Combine(_artifactDir, "report.json"),
        };
    }

    private static void WriteReport(ExternalModelRequest request, int? generation = null)
    {
        var json = generation is { } g
            ? $$"""{ "schemaVersion": 1, "requestGeneration": {{g}} }"""
            : """{ "schemaVersion": 1 }""";
        File.WriteAllText(request.OutputReportPath, json);
    }

    private static TestHarnessPreviewClient CreateClient(
        Func<string?> locate,
        IExternalModelProcessFactory factory)
    {
        return new TestHarnessPreviewClient(locate, factory);
    }

    private static string ExePath => "/fake/OpenSage.Tools.TestHarness";

    [Fact]
    public async Task Success_ValidatesExitReportGenerationAndPng()
    {
        File.WriteAllText(Path.Combine(_root, "tank.gltf"), "{}");
        var request = CreateRequest();
        WriteReport(request, generation: request.Generation);
        File.WriteAllBytes(request.OutputImagePath!, [1, 2, 3]);

        var factory = new FakeProcessFactory((_, _) => new FakeProcess());
        var client = CreateClient(() => ExePath, factory);
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ExePath, factory.LastExecutablePath);
        Assert.Contains("--external-model-request", factory.LastArguments, StringComparison.Ordinal);
        Assert.True(((FakeProcess)factory.LastProcess!).Disposed);
        Assert.False(File.Exists(Path.Combine(_artifactDir, $"request-{lease.ArtifactId}.json")));
        Assert.True(File.Exists(request.OutputReportPath));
        Assert.True(File.Exists(request.OutputImagePath!));
        Assert.False(lease.IsDisposed);
    }

    [Fact]
    public async Task MissingExecutable_ReturnsExt200()
    {
        var client = CreateClient(() => null, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(CreateRequest(), lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.MissingExecutableCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task StartFailure_ReturnsExt201()
    {
        var factory = new FakeProcessFactory((_, _) =>
            throw new InvalidOperationException("no exec format"));
        var client = CreateClient(() => ExePath, factory);
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(CreateRequest(), lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.StartFailedCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task NonZeroExit_ReturnsExt210_WithBoundedStderrDetail()
    {
        var request = CreateRequest();
        var factory = new FakeProcessFactory((_, _) => new FakeProcess("boom details", exitCode: 5));
        var client = CreateClient(() => ExePath, factory);
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        var diagnostic = result.Diagnostics[0];
        Assert.Equal(TestHarnessPreviewClient.NonZeroExitCode, diagnostic.Code);
        Assert.Contains("boom details", diagnostic.ExceptionDetail?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("boom details", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingReport_ReturnsExt211()
    {
        var request = CreateRequest();
        var client = CreateClient(() => ExePath, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.MissingReportCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task MalformedReport_ReturnsExt212()
    {
        var request = CreateRequest();
        File.WriteAllText(request.OutputReportPath, "{ not json");
        var client = CreateClient(() => ExePath, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.MalformedReportCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task GenerationMismatch_ReturnsExt213()
    {
        var request = CreateRequest(generation: 7);
        WriteReport(request, generation: 3);
        File.WriteAllBytes(request.OutputImagePath!, [1]);

        var client = CreateClient(() => ExePath, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.GenerationMismatchCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task MissingRequestedPng_ReturnsExt214()
    {
        var request = CreateRequest();
        WriteReport(request, generation: request.Generation);

        var client = CreateClient(() => ExePath, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TestHarnessPreviewClient.MissingPngCode, result.Diagnostics[0].Code);
    }

    [Fact]
    public async Task Cancellation_KillsOnlyOwnedInstanceAndAwaitsExit()
    {
        var request = CreateRequest();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new GatedFakeProcess(gate);
        var factory = new FakeProcessFactory((_, _) => process);
        var client = CreateClient(() => ExePath, factory);
        using var lease = new FakeLease();
        using var cts = new CancellationTokenSource();

        var execution = client.ExecuteAsync(request, lease, cts.Token);
        cts.Cancel();
        gate.TrySetResult(true);
        await Assert.ThrowsAsync<TaskCanceledException>(() => execution);

        Assert.Equal(1, process.KillTreeCallCount);
        Assert.True(process.Disposed);
    }

    private sealed class GatedFakeProcess(TaskCompletionSource<bool> exitGate) : IExternalModelProcess
    {
        public int KillTreeCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("");
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await exitGate.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public void KillTree()
        {
            KillTreeCallCount++;
            exitGate.TrySetResult(true);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    [Fact]
    public async Task ReportOnlyRequest_DoesNotRequirePng()
    {
        var request = CreateRequest(image: null);
        WriteReport(request, generation: request.Generation);

        var client = CreateClient(() => ExePath, new FakeProcessFactory((_, _) => new FakeProcess()));
        using var lease = new FakeLease();

        var result = await client.ExecuteAsync(request, lease, CancellationToken.None);

        Assert.True(result.Succeeded);
    }
}
