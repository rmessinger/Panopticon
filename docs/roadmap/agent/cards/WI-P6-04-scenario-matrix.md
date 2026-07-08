# Task Card: `WI-P6-04` — P6 scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P6-04` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md) — scenario matrix, see [deep dive §P6](../../deep-dives/economy-production-combat.md#p6-combat-and-command-verification) and [§Scenario Matrix](../../deep-dives/economy-production-combat.md#scenario-matrix) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P6-01`, `WI-P6-02`, `WI-P6-03` |
| Diagrams | [P6 Architecture Diagrams](../../deep-dives/economy-production-combat.md#architecture-diagrams) |

## Goal

Replace the P6 scenario-matrix stub with a CI-running 15-cell matrix sweeping order type by faction, asserting each command type produces the correct observable effect for that faction.

## Context to read (and only this)

- `docs/roadmap/deep-dives/economy-production-combat.md:113-144` — P6 matrix axes, constraints, and per-cell assertions.
- `docs/roadmap/implementation-checklist.md:293-297` — checklist ACs for the P6 scenario matrix.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:96-152` — matrix expansion model.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23` — matrix runner.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:165-188` — current skipped P6 matrix stubs.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs:374-425` — existing matrix expansion/report test style.
- `docs/roadmap/agent/cards/WI-P6-01-command-coverage-matrix.md` — confirmed command/order coverage and gaps.
- `docs/roadmap/agent/cards/WI-P6-02-combat-coverage.md` — verified combat assertions to reuse for attack cells.
- `docs/roadmap/agent/cards/WI-P6-03-death-cleanup.md` — death cleanup assertions to reuse when combat cells kill targets.

## Steps

1. Update `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs` or split P6 into `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6ScenarioMatrixTests.cs`.
2. Remove the skipped P6 matrix stubs and replace them with non-skipped `[Fact]` tests that run in CI.
3. Define a `ScenarioMatrix` with axes:
   - `orderType` ∈ `MoveTo`, `AttackObject`, `BuildObject`, `CreateUnit`, `SpecialPower`
   - `faction` ∈ `USA`, `GLA`, `China`
4. Assert expansion yields exactly 15 cells and no filters remove cells.
5. For each cell, issue the order for that faction's reference unit and assert the observable effect: unit moves, target health decreases, building is constructed or queued, unit spawns or production queues, or ability effect is observed.
6. Fail loudly on unimplemented handlers or no-op special powers. Each gap must be a failed cell/report entry naming `orderType` and `faction`, not a silent skip.
7. Preserve any helper logic from P6-01 through P6-03; do not weaken their assertions to make matrix cells pass.
8. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify it is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6ScenarioMatrixTests.cs` (new, if splitting from the existing file)

## Acceptance criteria

- [ ] The P6 `ScenarioMatrix` has axes `orderType` × `faction` with the five order families and three factions from the deep dive.
- [ ] The matrix expands to exactly 15 cells.
- [ ] Each cell asserts the observable effect for that faction's reference unit.
- [ ] Unimplemented handlers and no-op special-power cells fail loudly with explicit `orderType`/`faction` gap names.
- [ ] Tests run in CI as non-skipped `[Fact]` tests; no gate is satisfied only by `[GameFact]` or an Interactive-only test.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6_ScenarioMatrixTests|FullyQualifiedName~P6ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not create a card for "Fill gaps surfaced by P7" during this work; P7 will populate concrete follow-ups later.
- Do not implement P6b unit-contract validation here.
- Do not skip matrix cells for missing handlers; report them as explicit failures/gaps.
- Do not rely on local game installations for the required gate.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P6 scenario matrix item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with total/passed/failed cells and named gaps.
- Confirm the P6 matrix test file was created or updated and runs in CI.
- If `WI-P6-01`, `WI-P6-02`, and `WI-P6-03` are also `Done`, update the Track P P6 row status.
- Follow-up note: the P7 "Fill gaps surfaced by P7" checklist item intentionally remains TBD until P7 reports concrete defects; create one follow-up card per defect then, not now.

## Result

<!-- append after completing -->

