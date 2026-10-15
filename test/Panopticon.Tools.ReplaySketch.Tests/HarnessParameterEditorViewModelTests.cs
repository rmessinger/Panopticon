using System.Collections.Generic;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessParameterEditorViewModelTests
{
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
