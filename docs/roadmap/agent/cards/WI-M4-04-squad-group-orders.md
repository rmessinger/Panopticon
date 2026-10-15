# Task Card `WI-M4-04`: Squad group orders served by B4

| Field | Value |
|---|---|
| Card ID | `WI-M4-04` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M4-03` |
| Diagrams | [M4 army hierarchy](../../deep-dives/mass-battle-1k.md#m4-army-hierarchy) |

## Goal

A squad's move orders issue one shared path request per squad through B4's `GroupMovement`, so path requests per tick scale with squads, not units, in the M1 scenario (M4-2).

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m4-army-ai.md` (actuation decision).
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`: `MoveSquad` (leader path, follower offsets, aircraft fly direct).
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` `MoveTo`/`AttackMove` case.
- `docs/roadmap/implementation/track-b-b4-spatial-scaling.md` sections for WI-B4-04 and WI-B4-05.
- `src/Panopticon.Simulation/Navigation/Navigation.cs` `PathRequestCount`.

## Steps

1. Batch soldier orders per squad in the hierarchy adapter: members sharing one destination become one selection plus one `MoveTo`; mixed goals fall back to per-unit orders.
2. Route a multi-unit `MoveTo` in `OrderProcessor` through `GroupMovement.MoveSquad`; a single-unit `MoveTo` keeps `SetTargetPoint`. Keep move sounds unchanged.
3. Count path requests per decision tick in the army-AI run and report squads issued, units moved, and path requests.
4. Add tests: an install-free `OrderProcessor` test that one 8-unit `MoveTo` costs one leader path plus blocked followers only; a scaled Interactive run asserting path requests per decision tick are at most squads plus blocked followers.

## Files likely to change

- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs` (or the batcher named in WI-M4-01)
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeReport.cs`
- `test/Panopticon.Simulation.Tests/Logic/Orders/GroupMoveOrderTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeScenarioTests.cs`

## Acceptance criteria

- [ ] Path requests per decision tick scale with squad count: the report shows requests at most squads plus blocked followers, not units.
- [ ] Player-issued multi-unit moves keep working (existing order and ReplaySketch tests pass).
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~GroupMove|FullyQualifiedName~GroupMovement|FullyQualifiedName~PathRequest"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~MassBattleCapacityProbe"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Async path requests stay optional: use `SubmitPathRequest` only if WI-M4-01 decided it, and then results must be timing-insensitive (B4 determinism position).
- Changing `OrderProcessor` multi-unit moves changes replay behavior for player orders; a golden-frame or `SeededRunTests` failure is a suspected deviation (stop and report).

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none expected.
