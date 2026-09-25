# Task Card `WI-P3-05`: Mutual elimination to sudden death

| Field | Value |
|---|---|
| Card ID | `WI-P3-05` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md): sub-phase P3-5, see [deep dive §1.5](../../deep-dives/p3-victory-defeat.md#15-sudden-death-and-time-limit-implementation-notes) and [§4](../../deep-dives/p3-victory-defeat.md#3-sub-phases) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P3-01`, `WI-P3-02`, `WI-P3-03`, and `WI-P3-04` |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/p3-victory-defeat.md#7-architecture-diagrams) |

## Goal

Detect simultaneous mutual elimination in one victory check window, transition to `OutcomeStatus.SuddenDeath` without stopping the match, then decide normally on the next elimination.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:64-67`: mutual-elimination design in defeat detection.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:90-103`: sudden-death flow and implementation notes.
- `docs/roadmap/implementation-checklist.md:221-224`: sudden-death AC.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:250-292`: logic frame advance used for check-window tests.
- `src/Panopticon.Simulation/Game.cs:899-927` and `src/Panopticon.Simulation/Game.cs:963-975`: logic-running behavior that must continue in sudden death.
- `src/Panopticon.Simulation/Logic/Player.cs:126-134` and `src/Panopticon.Simulation/Logic/Player.cs:785-804`: player state and ally/enemy relationships.
- `src/Panopticon.Simulation/Logic/Player.cs:807-839`: maintained owned-object counts consumed by the defeat rule.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs:24-70`: headless test base; tests must run in CI.

## Steps

1. Extend the `VictorySystem` check-window processing so it batches affected-player defeat evaluations before committing outcomes.
2. If every remaining active non-allied side would be eliminated in the same check window, set `GameOutcome.Status = SuddenDeath` and do not set `DecidedOnFrame` as a final result.
3. Keep play running in sudden death: do not stop `IsLogicRunning`, do not invoke the decided-match flow, and keep accepting subsequent lifecycle notifications.
4. On the first later elimination after sudden death, transition to `Status = Decided` with `Reason = Eliminated` and the correct winner/loser fields.
5. Make the default/sentinel behavior explicit in tests: `SuddenDeath` is transitional and distinct from both `InProgress` and `Decided`.
6. Add `test/Panopticon.Simulation.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs` with headless `[Fact]` tests for simultaneous last-structure loss and later decided resolution.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Simulation/Logic/VictorySystem.cs`
- `src/Panopticon.Simulation/Logic/GameOutcome.cs`
- `test/Panopticon.Simulation.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs` (new)

## Acceptance criteria

- [x] A scenario where all remaining active players' last qualifying structures are destroyed in the same check window transitions `GameOutcome.Status` to `SuddenDeath`.
- [x] The sudden-death transition does not terminate/stop the match; logic continues to tick.
- [x] Defeat flags/state are not permanently committed in a way that prevents continued play after sudden-death entry.
- [x] A subsequent elimination transitions to `GameOutcome.Status = Decided` with `Reason = Eliminated` and the correct winner.
- [x] Headless `[Fact]` tests are not skipped and do not require game files.
- [x] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SuddenDeathOutcomeTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement time-limit score decisions here.
- Do not stop the simulation on `SuddenDeath`; only `Decided` uses end-of-match flow.
- Do not add per-frame full-object scans; use the same affected-player/check-window queue as `WI-P3-02`/`WI-P3-03`.
- If simultaneous-team semantics are ambiguous beyond the deep dive, keep the narrow all-active-sides case and document a follow-up.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Mutual elimination → sudden-death handling" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the sudden-death tests run.
- Confirm the required test file `test/Panopticon.Simulation.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs` was created and runs in CI.

## Result

- Updated `VictorySystem` to batch defeat checks per cadence window, enter `OutcomeStatus.SuddenDeath` when every remaining active player would be eliminated together, and delay final defeat commitment until a later elimination resolves the match.
- Added `SuddenDeathOutcomeTests` covering the transitional sudden-death state, continued logic ticking, and later eliminated resolution; full non-interactive validation passed.
