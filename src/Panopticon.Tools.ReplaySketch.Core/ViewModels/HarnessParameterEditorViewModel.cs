using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

public sealed class HarnessParameterEditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<HarnessParameterRow> Rows { get; private set; } = [];
    public IReadOnlyList<IGrouping<string, HarnessParameterRow>> Groups => Rows.GroupBy(row => row.Descriptor.Group).ToArray();
    public bool HasErrors => Rows.Any(row => row.Error is not null);

    public void Load(HarnessCatalog catalog, HarnessScenarioDescriptor scenario, IReadOnlyDictionary<string, JsonNode?>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(scenario);
        var previous = overrides ?? Rows.Where(row => row.IsOverridden).ToDictionary(row => row.Descriptor.Name, row => ValueOf(row));
        Rows = catalog.Parameters
            .Where(parameter => parameter.Name != "Scenario" && (parameter.AppliesTo.Count == 0 || parameter.AppliesTo.Contains(scenario.Id, StringComparer.Ordinal)))
            .OrderBy(parameter => parameter.Group, StringComparer.Ordinal).ThenBy(parameter => parameter.Name, StringComparer.Ordinal)
            .Select(parameter => new HarnessParameterRow(parameter, scenario.Modes == HarnessRunModes.Headless && parameter.Name == "Headless"))
            .ToArray();
        foreach (var row in Rows)
            if (!row.IsLocked && previous.TryGetValue(row.Descriptor.Name, out var value)) row.Text = value?.ToJsonString().Trim('"') ?? string.Empty;
        foreach (var row in Rows) row.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public HarnessLaunchRequest BuildRequest(string scenarioId)
    {
        if (HasErrors) throw new InvalidOperationException("Correct parameter errors before running.");
        var values = Rows.Where(row => row.IsOverridden).ToDictionary(row => row.Descriptor.Name, ValueOf);
        return new HarnessLaunchRequest(scenarioId, values);
    }

    public void ApplyOverrides(IReadOnlyDictionary<string, JsonNode?> overrides)
    {
        foreach (var row in Rows)
            if (!row.IsLocked && overrides.TryGetValue(row.Descriptor.Name, out var value)) row.Text = value?.ToJsonString().Trim('"') ?? string.Empty;
    }

    private static JsonNode? ValueOf(HarnessParameterRow row) { row.TryGetValue(out var value); return value; }
}
