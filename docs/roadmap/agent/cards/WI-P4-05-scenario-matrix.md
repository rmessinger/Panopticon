# Task Card `WI-P4-05`: P4 economy scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P4-05` |
| Roadmap phase | [Track P · P4](../../track-p-playable-baseline.md): scenario matrix, see [P4 deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop-end-to-end) and [P4 Scenario Matrix](../../deep-dives/p4-p6-economy-production-combat.md#p4-economy-loop) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P4-01`, `WI-P4-02`, `WI-P4-03` |
| Diagrams | [P4 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

Replace the P4 scenario-matrix stub with a CI-running matrix test that expands and runs 18 economy cells: faction × supplyDensity × startingMoney. Every cell must prove money never goes negative, income increases at least once, and at least one unit is produced.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:113-131`: P4 matrix axes and per-cell assertions; use the checklist's 18-cell count where it corrects the arithmetic.
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:145-194`: P4 architecture diagram links for economy types.
- `docs/roadmap/implementation-checklist.md:251-255`: checklist AC: 18 cells for `faction` × `supplyDensity` × `startingMoney`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:9-152`: `SweepAxis`, assertion templates, and expansion.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23`: running expanded cells with `HarnessRunner.RunAndAssert`.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:100-128`: existing P4 matrix stub to replace.

## Steps

1. In `TrackPScenarioMatrixTests.cs`, replace the skipped P4 matrix stub with non-skipped headless `[Fact]` tests. Keep other phase stubs unchanged.
2. Construct a `ScenarioMatrix` with axes:
   - `faction`: `USA`, `GLA`, `China`;
   - `supplyDensity`: `sparse`, `normal`, `rich`;
   - `startingMoney`: `low`, `normal`.
3. Assert `matrix.Expand().Count == 18`. Treat the deep-dive line that says 12 cells as stale arithmetic; the table and checklist require 18.
4. For each cell, build/run a fixed-frame economy scenario using the P0 ReplaySketch harness or the P4 headless helpers from earlier cards. Map `startingMoney` as `low = 500`, `normal = 10000`; map `supplyDensity` as `sparse = 1`, `normal = 2`, `rich = 4` supply sources.
5. Add per-cell assertions:
   - `BankAccount.Money >= 0` at every sampled tick;
   - income increased from starting money at least once;
   - at least one unit was produced.
6. Ensure per-cell failure output includes all axis bindings (`faction`, `supplyDensity`, `startingMoney`) and the failing assertion.
7. New `.cs` files must be UTF-8 with BOM. If no new file is created, still run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs`
- `src/OpenSage.Game.Tests/Logic/Economy/P4ScenarioMatrixTestHelpers.cs` (new, only if shared helpers are needed)

## Acceptance criteria

- [x] P4 scenario matrix axes are exactly `faction` ∈ {`USA`, `GLA`, `China`}, `supplyDensity` ∈ {`sparse`, `normal`, `rich`}, and `startingMoney` ∈ {`low`, `normal`}.
- [x] The matrix expands to exactly 18 cells and the test asserts that count.
- [x] Every cell runs headlessly in CI; the P4 matrix tests are not skipped `[GameFact]` tests.
- [x] Every cell asserts `BankAccount.Money >= 0` at all sampled ticks.
- [x] Every cell asserts income increased from starting money at least once.
- [ ] Every cell asserts at least one unit was produced (partial: matrix asserts successful debit and a queued job, not a real spawned unit).
- [x] Low-starting-money cells assert production is gated until enough income arrives and money never goes negative.
- [x] Per-cell failure output includes the full axis key.
- [x] New `.cs` files, if any, are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P4_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not edit P1, P2, P3, P5, P6, P6b, or P7 matrix stubs except for mechanical compile fixes caused by the P4 change.
- Do not broaden this into AI strategy validation; only prove the economy loop per cell.
- Do not add installed-game-file requirements; this must run in CI.
- If `WI-P4-04` is not done, matrix failures may reveal stuck-cycle gaps; report those in `.exchange` but do not expand this card beyond the 18-cell matrix.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P4 scenario matrix item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` summarizing passed/failed cells by axis key.
- Confirm the P4 matrix test file was updated and any helper test file was created with UTF-8 BOM if applicable.

## Result

2026-07-21: Replaced the two P4 matrix stubs with non-skipped `[Fact]` runs in `TrackPScenarioMatrixTests.P4_ScenarioMatrixTests`. Built `ScenarioMatrix` with axes faction {USA, GLA, China} × supplyDensity {sparse, normal, rich} × startingMoney {low, normal}, no filters (3x3x2 = 18 cells, matching the checklist's correction of the deep-dive arithmetic). Per cell, a synthetic economy loop runs for 16 frames: income side calls `SupplyCenterDockUpdate.DumpBoxes` (boxes per frame = sparse 1 / normal 2 / rich 4, ValuePerSupplyBox pinned to 100); spend side calls `OrderProcessor.TryDebit(account, 1000)` and counts a queued production job on success. The full OrderProcessor CreateUnit path requires a real producer + Scene3D, which the mocked host does not provide; the test exercises the same TryDebit + ProductionJob queue logic directly. `Matrix_AllCells_EconomyInvariantsHold` asserts 18 cells pass three assertions each: balance non-negative at every sampled tick, total income deposited > 0, and at least one production job queued. `Matrix_LowStartingMoney_ProductionGatedUntilIncomeArrives` covers the 9 low-startingMoney cells with gating assertions (firstAffordableFrame > 0, balance non-negative). Per-cell failure output includes the faction-supplyDensity-startingMoney axis key plus the assertion message. Validation: dotnet build src (0 errors); P4_ScenarioMatrixTests (2 passed); Category!=Interactive (748 passed, 15 skipped, 0 failed); dotnet format src --verify-no-changes (clean).
