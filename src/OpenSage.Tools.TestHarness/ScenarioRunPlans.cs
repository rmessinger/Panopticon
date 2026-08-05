using System.Collections.Generic;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// A fully-constructed replay scenario ready to run: the exported <c>.rep</c> file, the sibling
/// engine-action-results path, and any actions deferred until a runtime trigger fires. Built by
/// <see cref="ScenarioBuilder.BuildReplayRun"/> and consumed by <see cref="ReplayScenarioRunner"/>.
/// </summary>
public sealed record ReplayRunPlan(
    string ReplayPath,
    string ResultsPath,
    IReadOnlyList<DeferredAction> DeferredActions);

/// <summary>
/// A fully-constructed Unit Validation Ground run: the scenario definition, the resolved unit
/// spawn plan, and the roster player settings. Built by
/// <see cref="ScenarioBuilder.BuildValidationGroundRun"/> and consumed by
/// <see cref="ValidationGroundRunner"/>.
/// </summary>
public sealed record ValidationGroundRunPlan(
    ValidationGroundScenario Scenario,
    SpawnPlan SpawnPlan,
    Logic.PlayerSetting[] PlayerSettings);
