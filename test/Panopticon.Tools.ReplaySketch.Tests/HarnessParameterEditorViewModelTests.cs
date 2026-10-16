using System.Collections.Generic;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessParameterEditorViewModelTests
{
    [Fact]
    public void LiveCatalog_DefaultsAreValid_AndProofIsForcedHeadless()
    {
        var catalog = Panopticon.Tools.TestHarness.HarnessScenarioCatalog.Build();
        foreach (var scenario in catalog.Scenarios)
        {
            var editor = new HarnessParameterEditorViewModel();
            editor.Load(catalog, scenario);
            Assert.False(editor.HasErrors);
            var request = editor.BuildRequest(scenario.Id);
            if (scenario.Modes == HarnessRunModes.Headless)
                Assert.True(request.Overrides["Headless"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void ApplyOverrides_ClearsUnrelatedEdits_AndPreservesEscapedStrings()
    {
        var scenario = new HarnessScenarioDescriptor("Run", HarnessScenarioCategory.Proof, "", HarnessRunModes.Headless, HarnessProfileInput.None, [], new Dictionary<int, string>());
        var catalog = new HarnessCatalog(1, [scenario], [new("Path", "Run", HarnessParameterKind.Path, [], null, "", []), new("Count", "Run", HarnessParameterKind.Int, [], JsonValue.Create(2), "", [])]);
        var editor = new HarnessParameterEditorViewModel();
        editor.Load(catalog, scenario);
        editor.Rows[0].Text = "9";
        var path = "C:\\folder\\\"quoted\"";
        editor.ApplyOverrides(new Dictionary<string, JsonNode?> { ["Path"] = JsonValue.Create(path) });
        var request = editor.BuildRequest("Run");
        Assert.Single(request.Overrides);
        Assert.Equal(path, request.Overrides["Path"]!.GetValue<string>());
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelledCommand_CanRunAgain()
    {
        var runs = 0;
        var command = new AsyncCommand(token => { token.ThrowIfCancellationRequested(); runs++; return System.Threading.Tasks.Task.CompletedTask; });
        await command.ExecuteAsync();
        command.Cancel();
        await command.ExecuteAsync();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void BuildRequest_IncludesOnlyOverrides()
    {
        var scenario = new HarnessScenarioDescriptor("UnitValidationGround", HarnessScenarioCategory.ValidationGround, "", HarnessRunModes.Headless, HarnessProfileInput.None, [], new Dictionary<int, string>());
        var catalog = new HarnessCatalog(1, [scenario], [new("Count", "Run", HarnessParameterKind.Int, [], JsonValue.Create(2), "", [])]);
        var editor = new HarnessParameterEditorViewModel();
        editor.Load(catalog, scenario);
        Assert.Empty(editor.BuildRequest(scenario.Id).Overrides);
        editor.Rows[0].Text = "3";
        var request = editor.BuildRequest(scenario.Id);
        Assert.Equal(3, request.Overrides["Count"]!.GetValue<int>());
    }

    [Fact]
    public void InvalidInteger_HasError()
    {
        var row = new HarnessParameterRow(new("Count", "Run", HarnessParameterKind.Int, [], JsonValue.Create(2), "", []));
        row.Text = "abc";
        Assert.NotNull(row.Error);
    }
}
