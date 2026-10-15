# Task Card `WI-P6-07`: Implement jet crash and final cleanup

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-P6-07` |
| Roadmap phase | [P6 corrective follow-up](../../track-p-playable-baseline.md#phase-resources) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S`, subject to WI-P6-06 scope reconciliation |
| Prerequisites | `WI-P6-06`, `WI-G6-03` |
| Diagrams | [Jet lifecycle design gate](../../plans/macos-battle-artifacts.md#jet-lifecycle-design-gate), pending WI-P6-06 |

## Goal

Dead jets leave the live combat/airfield lifecycle, crash when airborne, and finish
their authored death sequence with once-only object cleanup.

## Context to read (and only this)

- [Jet lifecycle contract](../../plans/macos-battle-artifacts.md#jet-lifecycle-design-gate), populated by WI-P6-06.
- [WI-P6-06](WI-P6-06-design-jet-crash-lifecycle.md): reconciled extension points and source-data semantics.
- `src/Panopticon.Simulation/Logic/Object/Behaviors/JetSlowDeathBehavior.cs`
- `src/Panopticon.Simulation/Logic/Object/Behaviors/SlowDeathBehavior.cs`
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`
- `test/Panopticon.Simulation.Tests/Logic/Object/Update/JetAIUpdateTests.cs`
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`

## Steps

1. Add the specialized runtime and override the jet module factory following the
   approved contract. Preserve non-jet slow-death behavior.
2. Implement airborne and grounded death paths, impact detection, authored delays,
   FX/OCL ordering, and final destruction through existing engine services.
3. Stop live attack/refuel/landing logic from competing with the dying jet. Preserve
   a renderable wreck until final removal, with once-only ownership/airfield cleanup.
4. Implement and test the approved checkpoint state and physics-backend integration.
5. Add unit/lifecycle tests and `capture-aurora-crash.json`. Assert physical removal
   from game logic separately from logical death, then inspect healthy, damaged,
   dying, impact, and post-removal captures. Run the windowed capacity probe.

## Files likely to change

Provisional list; WI-P6-06 must reconcile it before execution.

- `src/Panopticon.Simulation/Logic/Object/Behaviors/JetSlowDeathBehavior.cs`
- A new jet runtime module file beside the existing module-data file.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`, only for approved death handoff/cleanup hooks.
- Existing physics integration files identified by WI-P6-06, only for approved crash hooks.
- `test/Panopticon.Simulation.Tests/Logic/Object/Behaviors/JetSlowDeathBehaviorTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Logic/Object/Update/JetAIUpdateTests.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-aurora-crash.json` (new)

## Acceptance criteria

- [ ] Airborne lethal damage leads to impact and final removal within the approved bounded frame budget.
- [ ] Grounded lethal damage uses the authored ground-death path without entering an airborne crash.
- [ ] Logical-death notifications, airfield releases, final FX/OCLs, and destruction occur exactly once.
- [ ] Dead jets cannot keep attacking, land/refuel normally, or orbit indefinitely.
- [ ] Checkpoint continuation preserves the approved crash phase and remaining delays.
- [ ] Contract tests cover approved physics backends and preserve non-jet death behavior.
- [ ] Metal captures retain silver healthy jets and authored dark wrecks, with no lingering flying wreck after the crash budget.
- [ ] Standard validation and existing jet/aircraft contract tests pass.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario ScenarioCapture --scenario capture-aurora-crash.json
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattleCapacityProbe
```

Set `CNC_GENERALS_PATH` to the real installation. WI-P6-06 must add the exact focused
test filters and frame budgets. Preserve suspect determinism/golden deviations and
await the operator's determination; do not refresh baselines to mask them.

## Out of scope / guardrails

- No healthy-jet or wreck recoloring, particle-opacity changes, weapon balancing,
  or replacement of the general physics/death systems.
- WI-G6-03 must land before enabling additional jet death effects with negative authored opacity.
- Do not replace the sentinel destruction delay with an arbitrary timeout.
- Remain `Draft` until the dependency resolves the crash semantics and diagrams.

## On completion

- Set Status to `Done`, update the plan table and corrective checklist item.
- Append tests and capture locations to `.exchange`; retain the shipped P6 milestone.
