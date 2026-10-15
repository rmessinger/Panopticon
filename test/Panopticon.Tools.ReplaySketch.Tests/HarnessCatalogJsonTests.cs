using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class HarnessCatalogJsonTests
{
    [Fact]
    public void PublishedCatalog_RoundTripsWithStringEnumsAndDefaults()
    {
        var catalog = HarnessScenarioCatalog.Build();
        var json = HarnessCatalogJson.Serialize(catalog);
        var restored = HarnessCatalogJson.Deserialize(json);
        Assert.Equal(json, HarnessCatalogJson.Serialize(restored));
        Assert.Contains("\"Headless\"", json);
        Assert.Equal(Enum.GetNames<BuiltInScenario>().Order(), restored.Scenarios.Select(s => s.Id).Order());
        Assert.All(restored.Scenarios, s => Assert.True(s.ExitCodes.ContainsKey(0)));
        var defaults = JsonSerializer.SerializeToNode(new TestHarnessConfig(), Program.JsonOptions)!;
        Assert.Equal(typeof(TestHarnessConfig).GetProperties().Length, restored.Parameters.Count);
        Assert.All(restored.Parameters, p =>
        {
            Assert.True(JsonNode.DeepEquals(defaults[p.Name], p.Default));
            Assert.All(p.AppliesTo, id => Assert.Contains(id, Enum.GetNames<BuiltInScenario>()));
        });
    }

    [Fact]
    public void UnknownSchema_IsRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => HarnessCatalogJson.Deserialize("{\"schemaVersion\":2}"));
        Assert.Contains("expected 1", error.Message);
    }

    [Fact]
    public void MissingArrays_AreRejected() => Assert.Throws<JsonException>(() => HarnessCatalogJson.Deserialize("{\"schemaVersion\":1}"));
}
