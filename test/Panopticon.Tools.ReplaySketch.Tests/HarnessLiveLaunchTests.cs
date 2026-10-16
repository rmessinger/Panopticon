using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessLiveLaunchTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public async Task PbrMaterialProof_LaunchesFromPublishedCatalog()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var catalog = await new HarnessCatalogClient().LoadAsync(deadline.Token);
        Assert.True(catalog.Succeeded, catalog.Message);
        var scenario = catalog.Catalog!.Scenarios.Single(s => s.Id == "PbrMaterialProof");
        var editor = new HarnessParameterEditorViewModel();
        editor.Load(catalog.Catalog, scenario);
        var result = await new HarnessRunClient().RunAsync(editor.BuildRequest(scenario.Id), catalog.Catalog, null, deadline.Token);
        Assert.False(result.Cancelled);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("success", result.Meaning);
    }
}
