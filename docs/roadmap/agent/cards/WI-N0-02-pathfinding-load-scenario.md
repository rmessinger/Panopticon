# Task Card `WI-N0-02`: `PathfindingLoad` scenario and baseline report

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N0-02` |
| Roadmap phase | [N0](../../track-n-pathfinding-performance.md): Path cost baseline |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-N0-01` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Add a seeded TestHarness scenario that measures multi-unit order cost on the M0 map, run it and
the M4 army-AI probe, and record the baseline that every later Track N card compares against.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` sections 3 and 4 (N0).
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`, `MassBattlePlacement.cs`, `MassBattleDeploymentProfile.cs`, `mass-battle-deepdivide-capacity-probe.json`.
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `TestHarnessConfig.cs`, `Program.cs` dispatch.
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs`: `MoveUnits`.
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeScenarioTests.cs`.
- `docs/roadmap/implementation/track-m-m4-army-ai.md`: army-AI run command and report shape.

## Steps

1. Append `BuiltInScenario.PathfindingLoad` with a catalog descriptor and `[HarnessParameter]`
   knobs: `PathfindingLoadProfilePath`, `PathfindingLoadReportPath`, `PathfindingLoadSeed`
   (default 1), `PathfindingLoadSelectionSizes` (default 1, 10, 100, 1000).
2. `PathfindingLoadRunner`: headless game on the capacity-probe profile's map, one side only,
   enemy AI disabled. Per case, spawn the ground units through `MassBattlePlacement`, settle,
   then issue one `MoveTo` across the map through `OrderProcessor.MoveUnits`:
   squad-less selections of each size, the same sizes as army-AI squads, and one destination in
   an impassable pocket. Clear units between cases.
3. Report JSON per case: order-tick `path` span, requests by origin, nodes expanded, failed
   searches, mean and p99 search wall time, and the grid dimensions. Attach a B1 record.
4. Run the M4 army-AI probe (`MassBattleCapacityProbeArmyAi` on, 4500 frames, seed 1) with the
   `WI-N0-01` counters.
5. Record both in "WI-N0-02 baseline" in `docs/roadmap/implementation/track-n-pathfinding-performance.md`:
   tables, the dominant request origin, and a verdict per budget in deep dive section 3
   (confirm, or revise with the reason). Update the deep dive budgets table if revised.
6. Add an install-free test for case planning and report serialization, and an Interactive
   test that runs the 10-unit case.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `TestHarnessConfig.cs`, `Program.cs`
- `src/Panopticon.Tools.TestHarness/PathfindingLoadRunner.cs`, `PathfindingLoadReport.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/PathfindingLoadScenarioTests.cs` (new)
- `docs/roadmap/implementation/track-n-pathfinding-performance.md`, `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md`

## Acceptance criteria

- [ ] The scenario appears in `--list-scenarios` with all parameters described.
- [ ] Every case reports the metrics in step 3; the unreachable case reports its failed search.
- [ ] The M4 army-AI probe record includes the `path` span and per-origin statistics.
- [ ] The baseline section names the dominant origin and gives a verdict per budget.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~PathfindingLoad&Category!=Interactive"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario PathfindingLoad --headless
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Measurement only: no navigation or order-routing change.
- Do not change the capacity-probe profile or its defaults.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N1-01` to `Ready`.
- Tick N0-2 in the checklist, set N0 `Done` in the track doc, update the README progress row.
- Append a result line to `.exchange`.
