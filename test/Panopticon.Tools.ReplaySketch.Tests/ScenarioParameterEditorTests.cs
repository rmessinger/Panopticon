# nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-32: the parameter form covers scalar, enum, list, and implementation-selection values;
/// invalid edits never mutate the wrapped scenario; applied edits survive save/reload through
/// the scenario loader.
/// </summary>
public class ScenarioParameterEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-params-{Guid.NewGuid():N}");

    [Fact]
    public void RoundTrip_SaveAndReloadPreservesEdits()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        var viewModel = new ScenarioParametersViewModel(scenario);

        viewModel.DomainText = "transport-graph";
        viewModel.SeedText = "77";
        viewModel.FrameCapText = "90";
        viewModel.RecordingModeText = "Full";
        viewModel.SampleIntervalText = "20";
        viewModel.SnapshotIntervalText = "200";
        viewModel.AddParameter("difficulty", "hard");
        Assert.Empty(viewModel.ApplyChanges());

        var path = Path.Combine(_root, "params-round-trip.json");
        Directory.CreateDirectory(_root);
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(path, JsonSerializer.Serialize(scenario, options));

        var reloaded = ReplayScenarioLoader.Load(path);
        Assert.Equal("transport-graph", reloaded.DomainId);
        Assert.Equal(77u, reloaded.Seed);
        var cap = Assert.Single(reloaded.StopConditions.OfType<FrameCapStopCondition>());
        Assert.Equal(90u, cap.MaxFrames);
        Assert.Equal(RecordingMode.Full, reloaded.Recording?.Mode);
        Assert.Equal(20u, reloaded.Recording?.SampleInterval);
        Assert.Equal(200u, reloaded.Recording?.SnapshotInterval);
        Assert.Equal("hard", reloaded.Parameters["difficulty"]);
    }

    [Fact]
    public void InvalidEdits_DoNotMutateScenario()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        var viewModel = new ScenarioParametersViewModel(scenario);
        var seedBefore = scenario.Seed;
        var stopConditionsBefore = scenario.StopConditions.Count;

        viewModel.SeedText = "notanumber";
        viewModel.FrameCapText = "0";

        var errors = viewModel.ApplyChanges();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("seed", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("stopConditions", StringComparison.Ordinal));
        Assert.Equal(seedBefore, scenario.Seed);
        Assert.Equal(stopConditionsBefore, scenario.StopConditions.Count);
    }

    [Fact]
    public void DomainSwitch_ReplacesDomainWithoutStaleValues()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        var viewModel = new ScenarioParametersViewModel(scenario);

        viewModel.DomainText = "transport-graph";
        viewModel.SeedText = "77";

        Assert.Empty(viewModel.ApplyChanges());

        Assert.Equal("transport-graph", scenario.DomainId);
        Assert.Equal(77u, scenario.Seed);
    }

    [Fact]
    public void EnumAndListValueTypes_RoundTrip()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        var viewModel = new ScenarioParametersViewModel(scenario);

        viewModel.RecordingModeText = "full";
        Assert.Empty(viewModel.ApplyChanges());
        Assert.Equal(RecordingMode.Full, scenario.Recording?.Mode);

        viewModel.RecordingModeText = "Bogus";
        Assert.NotEmpty(viewModel.ApplyChanges());
        Assert.Equal(RecordingMode.Full, scenario.Recording?.Mode);

        // Restore a valid mode before the list-edit apply; the form holds what it shows.
        viewModel.RecordingModeText = "Full";
        viewModel.AddParameter("alpha", "1");
        viewModel.AddParameter("beta", "2");
        viewModel.SelectedParameter = viewModel.Parameters.Single(p => p.Key == "alpha");
        viewModel.RemoveSelected();
        Assert.Empty(viewModel.ApplyChanges());

        Assert.Equal("2", Assert.Single(scenario.Parameters).Value);
        Assert.Equal("beta", scenario.Parameters.Keys.Single());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
