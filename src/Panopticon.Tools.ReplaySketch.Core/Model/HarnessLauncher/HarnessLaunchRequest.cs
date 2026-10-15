using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessLaunchRequest(string ScenarioId, IReadOnlyDictionary<string, JsonNode?> Overrides, bool KeepConfig = false)
{
    public string ToConfigJson(HarnessCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var values = new JsonObject();
        foreach (var parameter in catalog.Parameters) values[parameter.Name] = parameter.Default?.DeepClone();
        foreach (var (name, value) in Overrides)
        {
            if (!catalog.Parameters.Any(p => p.Name == name)) throw new ArgumentException($"Unknown harness parameter '{name}'.", nameof(Overrides));
            values[name] = value?.DeepClone();
        }

        values["Scenario"] = ScenarioId;
        return values.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
