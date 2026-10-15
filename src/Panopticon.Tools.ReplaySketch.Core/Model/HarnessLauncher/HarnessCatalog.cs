using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessCatalog(
    int SchemaVersion,
    IReadOnlyList<HarnessScenarioDescriptor> Scenarios,
    IReadOnlyList<HarnessParameterDescriptor> Parameters);
