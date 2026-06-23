# Task Card: `P0-15` — Wire P0 acceptance test stubs to real implementations

| Field | Value |
|---|---|
| Card ID | `P0-15` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `P0-07`, `P0-08`, `P0-09`, `P0-10` |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#9-architecture-diagrams) |

## Goal

Update the remaining `NotImplementedException` stubs in the two P0 acceptance test files so they
call the real implementations that agents P0-07 through P0-13 built. No new production code; only
test wiring and two blocked-stub annotations.

## Context to read (and only this)

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the stubs to update
  (P0_3 OrderTypeMapping, P0_4 AssertionLayer, P0_5 RunAndAssertEntryPoint sections).
- `src/OpenSage.Tools.ReplaySketch.Tests/P0AcceptanceTests.cs` — two remaining stubs:
  `P0_2b_PlayerOwnerTargetingTests` and `P0_6_ProceduralAuthoringTests`.
- `src/OpenSage.Tools.ReplaySketch.Tests/MultiPlayerAttributionTests.cs` — real P0-2b tests
  already written by P0-10 (copy the core assertion logic).
- `src/OpenSage.Tools.ReplaySketch.Tests/` — look for the procedural authoring scale test file
  written by P0-09 to copy the 10k-order assertion logic into P0_6.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` — `RunAndAssert` signature.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` — wiring reference.
- `src/OpenSage.Tools.ReplaySketch.Tests/OrderTypeCoverageTests.cs` — `RunInteractiveScenario`
  helper to reuse for the P0_3 / P0_5 tests.

## Steps

### P0HarnessAcceptanceTests.cs — OpenSage.Game.Tests

1. **P0_3_OrderTypeMappingTests** (MoveOrder, AttackOrder, ConstructOrder, ProduceOrder,
   AbilityOrder):
   These require a live headless run. Tag all five `[Fact]` with
   `[Trait("Category", "Interactive")]`. Replace `throw new NotImplementedException(...)` with a
   call to `HarnessRunner.RunAndAssert` via a helper that mirrors `OrderTypeCoverageTests.RunInteractiveScenario`
   (get installation, build temp dir, call runner, catch SDL/FileNotFound, assert `AllPassed`).
   Use the same scenario shapes already in `OrderTypeCoverageTests.cs` (MoveTo, ForceAttackGround,
   BuildObject, CreateUnit, SpecialPower). The stubs can simply delegate to the equivalent
   `OrderTypeCoverageTests` scenarios rather than duplicating the setup.

   Note: the two stubs in `P0_3_RuntimeObjectReferenceTests` (`ImplicitHandle_AttackObject` and
   `QuerySelector_TargetsEnemyStructure`) depend on P0-12 (runtime handle resolution) which is
   blocked. Add `[Trait("Category", "BlockedOnP0-12")]` and keep the `NotImplementedException`
   with a comment explaining the dependency.

2. **P0_4_AssertionLayerTests**:
   - `PassingStatePredicate_HeadlessRun_ReportsPass`: Tag Interactive. Build a `ReplayScenario`
     with a `StateAssertion(player[0].money >= 0)`. Run via `HarnessRunner.RunAndAssert`.
     Assert `report.AllPassed`.
   - `FailingStatePredicate_HeadlessRun_ReportsWithFrameAndExpectedActual`: Tag Interactive.
     Add a `StateAssertion(player[0].money > 999999)` (deliberately failing). Run via
     `HarnessRunner.RunAndAssert`. Assert `!report.AllPassed` and that the failing result has
     a non-null `FailFrame` and non-null `ActualValue`.
   - `OutcomeAssertion_MatchDecided_ReportsPass`: Depends on P3 (`GameOutcome`). Add
     `[Trait("Category", "BlockedOnP3")]`. Keep `NotImplementedException` with comment.
   - `InvariantAssertion_MoneyNeverNegative_PassesThroughRun`: Tag Interactive. Add an
     `InvariantAssertion(player[0].money >= 0)` to a normal scenario. Run via
     `HarnessRunner.RunAndAssert`. Assert `report.AllPassed`.
   - `InvariantAssertion_Violated_AbortsAndReportsOffendingFrame`: Depends on P0-16 (early-abort
     not yet implemented). Add `[Trait("Category", "BlockedOnP0-16")]`. Keep
     `NotImplementedException` with comment.
   - `CodeDefinedPredicateEscapeHatch_Registered_EvaluatesCorrectly`: Depends on P0-17
     (escape hatch not yet implemented). Add `[Trait("Category", "BlockedOnP0-17")]`. Keep
     `NotImplementedException` with comment.

3. **P0_5_RunAndAssertEntryPointTests**:
   - `EntryPoint_PassingScenario_ExitsZeroWithJsonReport`: Tag Interactive. Use
     `HarnessRunner.RunAndAssert` with an empty assertion set. Assert `report.AllPassed == true`
     and `report.ToJson()` contains `"allPassed":true`.
   - `EntryPoint_FailingAssertion_ExitsNonZeroWithJsonReport`: Tag Interactive. Add a failing
     `StateAssertion`. Assert `!report.AllPassed` and report JSON contains the failing assertion id.
   - `EntryPoint_MultiCommandScenario_AuthorsAndAssertsEndToEnd`: Tag Interactive. Author a
     two-command scenario (one `BuildObject` then one `MoveTo` with `AfterActionTiming`). Assert
     `report.AllPassed` (basic non-negative money assertion).

### P0AcceptanceTests.cs — OpenSage.Tools.ReplaySketch.Tests

4. **P0_2b_PlayerOwnerTargetingTests.Scenario_ThreePlayers_CommandsAttributedToCorrectPlayerIndex**:
   Remove `throw new NotImplementedException`. Copy the assertion logic from
   `MultiPlayerAttributionTests.ThreePlayerScenario_RoutesCommandsToCorrectPlayerIndex` — build
   a 3-player scenario, export, read back orders, assert `PlayerIndex` values are `{2, 3, 4}`.

5. **P0_6_ProceduralAuthoringTests.ProceduralScenario_TenThousandOrders_ExportsAndLoadsWithinBudget**:
   Remove `throw new NotImplementedException`. Copy the assertion logic from the procedural scale
   test written by P0-09 — generate 10,000 `MoveTo` `ActionEntry` values, export, assert export
   succeeds and elapsed time < 5 seconds.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`
- `src/OpenSage.Tools.ReplaySketch.Tests/P0AcceptanceTests.cs`
- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj` — may need a project reference to
  `OpenSage.Tools.ReplaySketch.Core` if not already present.

## Acceptance criteria

- [ ] No `throw new NotImplementedException` remains in `P0HarnessAcceptanceTests.cs` except for
      the three stubs explicitly annotated as blocked (`BlockedOnP0-12`, `BlockedOnP3`,
      `BlockedOnP0-16`, `BlockedOnP0-17`).
- [ ] No `throw new NotImplementedException` remains in `P0AcceptanceTests.cs` (all stubs filled).
- [ ] `dotnet test src --filter "FullyQualifiedName~P0_4 OR FullyQualifiedName~P0_5 OR FullyQualifiedName~P0_2b OR FullyQualifiedName~P0_6"` has zero NotImplementedException failures.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet test src --filter "FullyQualifiedName~P0Harness"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement the blocked stubs (P0-12, P0-16, P0-17, P3 dependencies) — annotate and leave.
- Do not change production code in this card; test wiring only.
- If `OpenSage.Game.Tests` already references `ReplaySketch.Core` transitively, no `.csproj`
  change is needed — verify before adding the reference.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
