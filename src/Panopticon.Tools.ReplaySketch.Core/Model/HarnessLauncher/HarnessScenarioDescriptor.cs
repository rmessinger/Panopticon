using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

public sealed record HarnessScenarioDescriptor(
    string Id,
    HarnessScenarioCategory Category,
    string Summary,
    HarnessRunModes Modes,
    HarnessProfileInput ProfileInput,
    IReadOnlyList<string> EmbeddedProfiles,
    IReadOnlyDictionary<int, string> ExitCodes);
