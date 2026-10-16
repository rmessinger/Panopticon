using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessProcessClientTests
{
    private static readonly HarnessCatalog Catalog = new(1,
        [new("Run", HarnessScenarioCategory.Proof, "Run", HarnessRunModes.Headless, HarnessProfileInput.None, [], new Dictionary<int, string> { [5] = "expectation not met" })],
        [new("Count", "Run", HarnessParameterKind.Int, [], JsonValue.Create(2), "Count", [])]);

    [Theory]
    [InlineData(0, "not json", "HRN103")]
    [InlineData(0, "{\"schemaVersion\":2}", "HRN103")]
    [InlineData(1, "", "HRN102")]
    public async Task CatalogFailures_AreTyped(int exit, string json, string code)
    {
        var process = new FakeProcess(json, exit);
        var result = await new HarnessCatalogClient(() => "harness", new Factory(process)).LoadAsync(default);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task CatalogSuccess_AndMissingExecutable()
    {
        var process = new FakeProcess(HarnessCatalogJson.Serialize(Catalog), 0);
        Assert.True((await new HarnessCatalogClient(() => "harness", new Factory(process)).LoadAsync(default)).Succeeded);
        Assert.Equal("HRN100", (await new HarnessCatalogClient(() => null, null).LoadAsync(default)).ErrorCode);
        Assert.Equal("HRN101", (await new HarnessCatalogClient(() => "harness", new Factory(null)).LoadAsync(default)).ErrorCode);
    }

    [Fact]
    public async Task Run_StreamsBeforeExit_AndCleansConfig()
    {
        var process = new FakeProcess("", 5, wait: true);
        var factory = new Factory(process);
        var lines = new List<string>();
        var task = new HarnessRunClient(() => "harness", factory).RunAsync(new("Run", new Dictionary<string, JsonNode?> { ["Count"] = JsonValue.Create(3) }), Catalog, new InlineProgress(lines.Add), default);
        await process.Streamed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted);
        Assert.Equal(new[] { "first", "[stderr] second" }, lines);
        var path = factory.Arguments!["--config ".Length..].Trim('"');
        Assert.Equal(3, JsonNode.Parse(await File.ReadAllTextAsync(path))!["Count"]!.GetValue<int>());
        process.Exited.TrySetResult();
        Assert.Equal("expectation not met", (await task).Meaning);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Cancellation_KillsRun()
    {
        var process = new FakeProcess("", 0, wait: true);
        using var cancellation = new CancellationTokenSource();
        var task = new HarnessRunClient(() => "harness", new Factory(process)).RunAsync(new("Run", new Dictionary<string, JsonNode?>()), Catalog, null, cancellation.Token);
        await process.Streamed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.True((await task).Cancelled);
        Assert.True(process.Killed);
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private sealed class Factory(FakeProcess? process) : IHarnessProcessFactory
    {
        public string? Arguments { get; private set; }
        public IHarnessProcess Start(string executablePath, string arguments)
        {
            Arguments = arguments;
            return process ?? throw new IOException("start failed");
        }
    }

    private sealed class FakeProcess(string json, int exit, bool wait = false) : IHarnessProcess
    {
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Streamed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Killed { get; private set; }
        public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken) => Task.FromResult(json);
        public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken) => Task.FromResult("error");
        public Task StreamOutputAsync(IProgress<string>? output, CancellationToken cancellationToken)
        {
            output?.Report("first");
            output?.Report("[stderr] second");
            Streamed.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (wait) await Exited.Task.WaitAsync(cancellationToken);
            return exit;
        }

        public void KillTree() { Killed = true; Exited.TrySetResult(); }
        public void Dispose() { }
    }
}
