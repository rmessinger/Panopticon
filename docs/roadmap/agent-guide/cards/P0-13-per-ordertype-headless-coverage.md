# Task Card: `P0-13` — Per-OrderType headless coverage (move / attack / construct / produce / ability)

| Field | Value |
|---|---|
| Card ID | `P0-13` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-3, see [deep dive §3.2/§3.5](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `P0-07` (assertion layer), `P0-08` (run-and-assert entry point), `P0-03` (faction-agnostic exporter) |

## Goal

Add one `[Trait("Category", "Interactive")]` integration test per gameplay-relevant `OrderType`
(move, attack/ground, construct, produce, ability) that authors a minimal scenario via
`HarnessRunner.RunAndAssert`, injects a `StateAssertion` verifying the expected
`OrderProcessor` effect, and asserts it passes — confirming the full
author → export → headless-run → assert chain works for each order family.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` (P0-08) — `RunAndAssert` signature.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` (P0-07) — how
  assertions are wired.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (P0-07) — `StateAssertion`
  fields (`Path`, `Op`, `Value`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs:83-91` — `CreateOpen(...)`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` — `PositionCommandArgument`,
  `IntegerCommandArgument`, `HandleCommandArgument` (P0-11).
- `src/OpenSage.Game/Logic/Orders/OrderType.cs` — canonical names: `MoveTo`, `ForceAttackGround`,
  `BuildObject`, `CreateUnit`, `SpecialPower`.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:42-566` — the authoritative list of handled
  order types; confirm each target type has a real handler (not the default "Unimplemented" log).
- `src/OpenSage.Tools.ReplaySketch.Core/Profiles/alpine-assault-usa-vs-gla.json` — USA faction
  definition names to reuse for construct/produce steps (barracks name, ranger name).
- `src/OpenSage.Game.Tests/` — existing Interactive test style (look for `[GameFact]` usage).

## Steps

1. Create `src/OpenSage.Tools.ReplaySketch.Tests/OrderTypeCoverageTests.cs` (or add to
   `OpenSage.Game.Tests` if `GameInstallation` is more accessible there — use whichever project
   already has Interactive test infrastructure and a `GameInstallation` fixture).

2. Add a shared helper `BuildSingleCommandScenario(CommandSpec command, PositionSpec position,
   TimingConfig timing, StateAssertion assertion)` that returns a `ReplayScenario` pre-loaded
   with: one USA player (FactionIndex 2), the `alpine-assault-usa-vs-gla` map path, the given
   command on that player, and the given assertion. Use the alpine map because its start waypoints
   are already exercised by existing tests.

3. Add the following five `[Trait("Category", "Interactive")]` tests, each using
   `HarnessRunner.RunAndAssert(scenario, installation, tempDir)` and asserting
   `report.AllPassed == true`:

   a. `MoveTo_ProducesExpectedPositionEffect`:
      - Command: `CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.3f, 0.3f)))`.
      - Timing: `FixedTiming(100)`.
      - Assertion: `player[0].unitCount >= 0` (the run completes without error; unit count
        is non-negative). The point is that no exception is thrown by `OrderProcessor`.

   b. `ForceAttackGround_ProducesExpectedEffect`:
      - Command: `CommandSpec.CreateOpen(OrderType.ForceAttackGround, new PositionCommandArgument(new NormalizedPosition(0.7f, 0.7f)))`.
      - Timing: `FixedTiming(100)`.
      - Assertion: `player[0].buildingCount >= 0` (run completes without error).

   c. `BuildObject_ProducesBuildingCount`:
      - Command: `CommandSpec.CreateOpen(OrderType.BuildObject, new IntegerCommandArgument((int)AssetHash.GetHash("AmericaBarracks")), new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)), new FloatCommandArgument(0f))`.
      - Timing: `FixedTiming(50)` (issued early; building may not complete within the run, but
        the order must be accepted without throwing).
      - Assertion: `player[0].buildingCount >= 0`.

   d. `CreateUnit_ProducesUnitCount`:
      - Prerequisite: player starts with a barracks (use `FixedTiming(0)` `BuildObject` for a
        barracks + `AfterActionTiming` for the `CreateUnit`, or just issue `CreateUnit` directly
        if the alpine map spawns one).
      - Command: `CommandSpec.CreateOpen(OrderType.CreateUnit, new IntegerCommandArgument((int)AssetHash.GetHash("AmericaRanger")))`.
      - Assertion: `player[0].unitCount >= 0`.

   e. `SpecialPower_DoesNotThrow`:
      - Command: `CommandSpec.CreateOpen(OrderType.SpecialPower, new IntegerCommandArgument(0), new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)))`.
      - Timing: `FixedTiming(200)`.
      - Assertion: `player[0].unitCount >= 0`. The key check is no unhandled exception from
        `OrderProcessor`; `SpecialPower` may be a no-op if no ability is selected.

4. If any order type is handled by the "Unimplemented order type" default branch in
   `OrderProcessor` (confirmed by checking `OrderProcessor.cs:553-560`), mark that test
   `[Trait("Category", "Interactive")]` and add a comment: `// Known gap: handler not
   implemented — test documents the gap.` Do not fail the card on this; surfacing the gap
   is the goal.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Tests/OrderTypeCoverageTests.cs` (new), or a new file in
  `src/OpenSage.Game.Tests/Tools/ReplaySketch/`.

## Acceptance criteria

- [ ] Five tests exist, one per OrderType family (move, attack/ground, construct, produce, ability).
- [ ] Each passing test asserts `report.AllPassed == true` and its `StateAssertion` holds.
- [ ] Any unimplemented-handler gap is documented in a test comment rather than causing a build
      failure.
- [ ] All five tests are tagged `[Trait("Category", "Interactive")]` so they are skipped in CI.
- [ ] Non-Interactive suite still passes: `dotnet test src --filter Category!=Interactive`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
# With game files installed:
dotnet test src --filter "FullyQualifiedName~OrderTypeCoverage"
```

## Out of scope / guardrails

- Do not implement missing `OrderProcessor` handlers — document gaps in comments only.
- Do not add non-Interactive variants of these tests (they require game files for map/asset loading).
- Do not author six-phase skirmish scenarios here; each test is the minimal one-command scenario
  needed to exercise the target `OrderType`.
- If `HarnessRunner` is not done (P0-08 blocked), mark this card `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason; list any documented handler gaps).
- Tick the "Map the gameplay-relevant `OrderType` set to exporter order emission" item in the
  [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing which handlers were confirmed working
  and which were gaps.
- If this is the last P0 card to land, update the P0 row in
  `docs/roadmap/track-p-playable-baseline.md` to `Done`.
