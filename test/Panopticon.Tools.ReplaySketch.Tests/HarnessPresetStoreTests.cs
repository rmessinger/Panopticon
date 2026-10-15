using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessPresetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void SaveThenLoad_RestoresOverrides()
    {
        var store = new HarnessPresetStore(_root);
        store.Save("one/two", new HarnessLaunchRequest("Run", new Dictionary<string, JsonNode?> { ["Count"] = JsonValue.Create(3) }));
        var result = store.Load("one_two");
        Assert.Equal("Run", result.ScenarioId);
        Assert.Equal(3, result.Overrides["Count"]!.GetValue<int>());
    }
}
