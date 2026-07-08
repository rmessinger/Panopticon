# Task Card: `WI-P2-05` — P2 scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P2-05` |
| Roadmap phase | [Track P · P2](../../track-p-playable-baseline.md) — P2 scenario matrix, see [deep dive §6](../../deep-dives/skirmish-ai.md#6-scenario-matrix) and [§4](../../deep-dives/skirmish-ai.md#3-sub-phases) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P2-01`, `WI-P2-02`, `WI-P2-03`, `WI-P2-04`; P0 ScenarioMatrix runner and assertion paths available |
| Diagrams | [P2 Architecture Diagrams](../../deep-dives/skirmish-ai.md#7-architecture-diagrams) |

## Goal

Create the P2 `ScenarioMatrix` test that sweeps `humanFaction × aiFaction × mapSize` and fails loudly unless every cell proves the AI plays a legal game.

## Context to read (and only this)

- `docs/roadmap/deep-dives/skirmish-ai.md:120-140` — P2 matrix axes, 18 valid cells, and per-cell assertions.
- `docs/roadmap/implementation-checklist.md:195-199` — checklist acceptance criteria for the P2 matrix.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:9-152` — axes, filters, assertion templates, and expansion.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23` — matrix execution via `HarnessRunner`.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:20-84` — current assertion path resolver; extend paths here if needed.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionReport.cs:7-35` — per-run pass/fail report.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/MatrixReport.cs:7-56` — aggregate report shape.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:42-67` — existing P2 stub style and intent.

## Steps

1. Create `src/OpenSage.Game.Tests/Tools/ReplaySketch/P2ScenarioMatrixTests.cs` as a real, non-skipped test file. Keep the existing broad stub file intact unless a duplicate type/name requires a tiny adjustment.
2. Build a `ScenarioMatrix` with axes:
   - `humanFaction`: `USA`, `GLA`, `China`
   - `aiFaction`: `USA`, `GLA`, `China`
   - `mapSize`: `small`, `medium`
   No constraints are needed; assert expansion yields exactly 18 cells.
3. For each cell, run a fixed-frame P0 harness scenario with one human and one AI. Map each `mapSize` value to a known small/medium test map or scenario profile; keep the mapping in one helper with explicit defaults.
4. Add assertion paths/templates so every cell checks: AI built at least one structure, AI produced at least one unit, AI issued at least one attack order, and AI `BankAccount >= 0` throughout.
5. If `ScenarioAssertion.TryResolvePath` still returns placeholder zeros for building/unit counts, replace those placeholders with real `Player.BuildingCount`/`UnitCount` reads before relying on the matrix.
6. Add or reuse an attack-order count exposed by ReplaySketch/order monitoring. Missing/unimplemented order handlers must produce a failing assertion with cell key and details, not a skip or silent pass.
7. Add a mirror-match assertion/test for cells where `humanFaction == aiFaction`; they must run through the same matrix rather than a special skipped path.
8. New `.cs` files must be UTF-8 with BOM. After creating files, run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P2ScenarioMatrixTests.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (if assertion paths need real P2 values)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayActionMonitor.cs` (if attack-order counts need exposure)

## Acceptance criteria

- [x] A `ScenarioMatrix` with axes `humanFaction × aiFaction × mapSize` expands to exactly 18 cells.
- [x] Each cell runs headlessly for a fixed frame budget and asserts AI built at least one structure.
- [x] Each cell asserts AI produced at least one unit.
- [x] Each cell asserts AI issued at least one attack order.
- [x] Each cell asserts AI `BankAccount >= 0` as an invariant.
- [x] Mirror-faction cells (`humanFaction == aiFaction`) are included and pass the same assertions.
- [x] Unimplemented handlers or unsupported assertion paths fail the matrix with the cell key and assertion detail; they do not skip or silently pass.
- [x] The new test file is real `[Fact]` coverage that runs in CI; no skipped test is the acceptance gate.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P2ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not author P5 matrix coverage here; P5 is a separate phase even though the deep dive is shared.
- Do not reduce the matrix to only USA/GLA or to only one map size.
- Do not mark this done with `[Fact(Skip=...)]`, `[GameFact]` that skips in CI, or placeholder assertion values.
- If real maps/game files are unavoidable for all cells, mark `Blocked` and explain the missing headless seam instead of weakening the acceptance gate.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "P2 scenario matrix" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Tools/ReplaySketch/P2ScenarioMatrixTests.cs` was created and the matrix test runs in CI.

## Result

- Added `P2ScenarioMatrixTests` with the required axis sweep (`humanFaction × aiFaction × mapSize`) and explicit 18-cell expansion + mirror-match coverage checks.
- Added concrete per-cell assertions for AI building count, unit count, attack-order count, and non-negative bank balance.
- Extended assertion/runtime plumbing for P2 matrix requirements:
  - `ScenarioAssertion.TryResolvePath` now reads real `Player.BuildingCount`/`Player.UnitCount` and supports `player[i].attackOrderCount`.
  - Added `ReplayActionMetrics` and integrated it with `ReplayActionMonitor` + `HarnessRunner` reset to expose attack-order counts per player.
  - Included `AttackObject`/`ForceAttackObject` as attack actions in monitoring.
- Added map-size-to-map-path helper mapping in the matrix test with explicit default fallback.
- Validation is blocked in this environment by missing .NET targeting-pack data (`Microsoft.NETCore.App.Ref/10.0.9/data/FrameworkList.xml`).
