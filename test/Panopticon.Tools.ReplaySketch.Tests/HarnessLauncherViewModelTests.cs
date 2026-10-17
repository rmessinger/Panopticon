using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessLauncherViewModelTests
{
    [Fact]
    public async Task Filters_ClearHiddenSelection_AndErrorsSurface()
    {
        var client = new CatalogClient();
        var vm = new HarnessLauncherViewModel(client, new RunClient(), new Dispatcher());
        await vm.LoadCatalogCommand.ExecuteAsync();
        vm.SelectedScenario = vm.AllScenarios.Single(s => s.Id == "UnitValidationGround");
        vm.FilterText = "ee2";
        Assert.All(vm.VisibleScenarios, s => Assert.StartsWith("Ee2", s.Id));
        Assert.Null(vm.SelectedScenario);
        vm.ModeFilter = HarnessRunModes.Windowed;
        Assert.Empty(vm.VisibleScenarios);
        vm.FilterText = "";
        vm.CategoryFilter = HarnessScenarioCategory.ValidationGround;
        Assert.Single(vm.VisibleScenarios);
        client.Result = new(null, "HRN100", "missing");
        await vm.LoadCatalogCommand.ExecuteAsync();
        Assert.Empty(vm.VisibleScenarios);
        Assert.Contains("HRN100", vm.StatusMessage);
    }

    [Fact]
    public async Task LoadingPresetForSameScenario_ReplacesEditsAndReportsStaleKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HarnessPresetStore(root);
            store.Save("saved", new("UnitValidationGround", new Dictionary<string, JsonNode?> { ["Removed"] = JsonValue.Create(1) }));
            var vm = new HarnessLauncherViewModel(new CatalogClient(), new RunClient(), new Dispatcher(), store);
            await vm.LoadCatalogCommand.ExecuteAsync();
            vm.SelectedScenario = vm.AllScenarios.Single(s => s.Id == "UnitValidationGround");
            vm.Parameters.Rows.Single(r => r.Descriptor.Name == "ValidationGroundSeed").Text = "9";
            vm.LoadPreset("saved");
            Assert.Empty(vm.Parameters.BuildRequest("UnitValidationGround").Overrides);
            Assert.Contains("Removed", vm.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Scenarios_DefaultToNewestFirst_AndFiltersKeepThatOrder()
    {
        var vm = new HarnessLauncherViewModel(new CatalogClient(), new RunClient(), new Dispatcher());
        await vm.LoadCatalogCommand.ExecuteAsync();
        var expectedNewestFirst = Enum.GetValues<BuiltInScenario>().Reverse().Select(scenario => scenario.ToString());
        Assert.Equal(expectedNewestFirst, vm.AllScenarios.Select(s => s.Id));
        Assert.Equal(expectedNewestFirst, vm.VisibleScenarios.Select(s => s.Id));
        vm.FilterText = "proof";
        Assert.Equal(Enum.GetValues<BuiltInScenario>().Reverse().Select(scenario => scenario.ToString()).Where(id => id.Contains("proof", StringComparison.OrdinalIgnoreCase)),
            vm.VisibleScenarios.Select(s => s.Id));
    }

    [Fact]
    public async Task Scenarios_WithoutAddedOrder_KeepCategoryThenIdOrder()
    {
        var legacy = new[]
        {
            new HarnessScenarioDescriptor("OldProof", HarnessScenarioCategory.Proof, "p", HarnessRunModes.Headless, HarnessProfileInput.None, [], new Dictionary<int, string> { [0] = "success" }),
            new HarnessScenarioDescriptor("OldReplay", HarnessScenarioCategory.Replay, "r", HarnessRunModes.Headless, HarnessProfileInput.None, [], new Dictionary<int, string> { [0] = "success" }),
        };
        var client = new CatalogClient { Result = new(new HarnessCatalog(HarnessCatalogJson.CurrentSchemaVersion, legacy, []), null, null) };
        var vm = new HarnessLauncherViewModel(client, new RunClient(), new Dispatcher());
        await vm.LoadCatalogCommand.ExecuteAsync();
        Assert.Equal(new[] { "OldReplay", "OldProof" }, vm.AllScenarios.Select(s => s.Id));
    }

    private sealed class CatalogClient : IHarnessCatalogClient
    {
        public HarnessCatalogResult Result { get; set; } = new(HarnessScenarioCatalog.Build(), null, null);
        public Task<HarnessCatalogResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private sealed class RunClient : IHarnessRunClient
    {
        public Task<HarnessRunResult> RunAsync(HarnessLaunchRequest request, HarnessCatalog catalog, IProgress<string>? output, CancellationToken cancellationToken) => Task.FromResult(new HarnessRunResult(0, "success", false, null));
    }

    private sealed class Dispatcher : IMainThreadDispatcher
    {
        public Task InvokeOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
    }
}
