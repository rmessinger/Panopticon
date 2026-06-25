# Task Card: `WI-P1-06` — Boot validation: human + AI skirmish boots headlessly

| Field | Value |
|---|---|
| Card ID | `WI-P1-06` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) (sub-phase P1-4, see [deep dive §7](../../deep-dives/skirmish-setup.md)) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P1-02`, `WI-P1-04`, `WI-P1-05` |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/skirmish-setup.md#8-architecture-diagrams) |

## Goal

Add a P0-harness-backed acceptance test that a human + 1 AI skirmish boots headlessly
with correct player relationships, spawn positions, and faction assignments. This is the
primary regression gate for P1: if relationship wiring, spawn unification, or slot fill
regress, this test surfaces it.

## Context to read (and only this)

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the existing
  P0 harness acceptance test class. Match its structure exactly: use `HarnessRunner`,
  load a map, run a fixed number of ticks, assert on the resulting `AssertionReport`.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` — `RunAndAssert(...)` entry point
  and `HarnessRunnerOptions`; note how the test sets up a `ScenarioScript` (commands + assertions).
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` — available assertion
  subtypes: `PropertyAssertion`, `OutcomeAssertion`, `InvariantAssertion`, `CodeAssertion`.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs` — register named
  predicates keyed by string; this is the preferred way to write complex assertions.
- `src/OpenSage.Game/Logic/PlayerManager.cs` — `Players` property (the list to query for
  relationship and ownership assertions).
- `src/OpenSage.Game/Logic/Player.cs:811-831` — `GetRelationship(Team? that)` (use this to
  assert ally/enemy status between a pair of players).

## Steps

1. Add a new test class `P1BootValidationTests` in
   `src/OpenSage.Game.Tests/Tools/ReplaySketch/P1BootValidationTests.cs`, namespace
   `OpenSage.Tests.Tools.ReplaySketch`.

2. Register the needed predicates in a static constructor or `[ModuleInitializer]` via
   `CodeAssertionRegistry`. The minimum set:
   - `"P1.HumanPlayerIsLocal"` — asserts the game has exactly one player marked as
     `LocalPlayer`, and it is not a neutral or AI player.
   - `"P1.RelationshipsAreSymmetric"` — for every ordered pair `(a, b)` of non-neutral players,
     asserts `a.GetRelationship(...) == b.GetRelationship(...)` (i.e. the relationship is
     reflexively symmetric).
   - `"P1.NoPlayerUnspawned"` — asserts every non-neutral player has a `GameObject` in the
     scene with a start position (i.e. `Scene3D.GameObjects` contains at least one object
     owned by each non-neutral player).

3. Write a `[Fact]` named `SkirmishBoot_HumanPlusOneAI_HasCorrectRelationshipsAndSpawns`.
   - Set up a `ScenarioScript` with a 1-human + 1-AI slot config using a stock Generals map.
   - Run for enough ticks that the game finishes starting (≥10 ticks after start).
   - Assert all three predicates above via `CodeAssertion` entries in the script.

4. The test must not require a game installation: mark it `[Fact]` (not `[GameFact]`).
   If the test needs a map that requires installed files, guard it the same way existing
   `P0HarnessAcceptanceTests` interactive tests are guarded (check `P0HarnessTestHelpers`
   for the skip pattern).

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P1BootValidationTests.cs` (new file).

## Acceptance criteria

- [ ] `dotnet test src --filter "FullyQualifiedName~P1BootValidationTests"` runs and passes
      (or skips cleanly on CI if an installed game is required).
- [ ] The three predicates above are registered and used.
- [ ] `dotnet test src --filter Category!=Interactive` does not regress.
- [ ] `dotnet format src --verify-no-changes` passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P1BootValidationTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not modify production code; if a predicate can't pass, mark `Blocked` with the
  specific failure and the card that must fix it.
- Do not add the scenario-matrix sweep here (that is a later card if needed). One
  deterministic 1v1 boot test is the acceptance gate for P1-4.

## On completion

- Set Status to `Done` (or `Blocked` + reason, naming the blocking card).
- Tick the P1-4 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
