# Task Card `WI-N6-02`: `NavigationDeadlock` scenario and baseline

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N6-02` |
| Roadmap phase | [N6](../../track-n-pathfinding-performance.md): Deadlock detection baseline |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-N6-01` |
| Diagrams | [Track N deadlock diagrams](../../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) |

## Goal

Add a seeded `NavigationDeadlock` TestHarness scenario whose cases reproduce each deadlock mechanism, run them to settle or frame budget, and record the baseline.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-navigation-deadlocks.md` sections 3 and 4 (N6 case table).
- `src/Panopticon.Tools.TestHarness/RoutingChokeRunner.cs`, `RoutingChokeProgressMonitor.cs` (wall gap setup and termination).
- `src/Panopticon.Tools.TestHarness/PathfindingLoadRunner.cs`, `PathfindingLoadCasePlanner.cs` (case planner pattern).
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `TestHarnessConfig.cs`.

## Steps

1. Add `BuiltInScenario.NavigationDeadlock` with a catalog descriptor and `[HarnessParameter]` config (case filter, frame budget per case, seed, report path).
2. Add `NavigationDeadlockCasePlanner` with the six cases in the deep dive table; reuse the `RoutingChoke` wall-gap setup for `corridor-opposed-20` and `queue-behind-idle`.
3. Run each case until every unit is `Arrived` or `CrowdArrived` or the budget ends; sample with `NavigationStallSampler`.
4. Report per case: settled units, frames to settle, stall records by reason, wait-for cycles at end, `impassableRetracePoints`, and the `path` span.
5. Record the baseline in "Navigation deadlocks results"; confirm or revise the section 3 targets and per-case frame budgets.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/NavigationDeadlockRunner.cs`, `NavigationDeadlockCasePlanner.cs`, `NavigationDeadlockReport.cs` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `TestHarnessConfig.cs`, `Program.cs`
- `test/Panopticon.Simulation.Tests/` case planner tests next to the PathfindingLoad tests (new)

## Acceptance criteria

- [ ] Each case reproduces its mechanism at baseline, or the report states it does not, with numbers.
- [ ] The scenario appears in `--list-scenarios` with its parameters.
- [ ] Baseline and target revisions are recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~NavigationDeadlock|FullyQualifiedName~Navigation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No fixes; mechanisms are recorded as found.
- A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-N7-01`, `WI-N8-01`, and `WI-N9-01` to `Ready`.
- Tick N6-2 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
