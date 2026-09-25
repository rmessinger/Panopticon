# Task Card `WI-A1-12`: Fix the sleepy-update stall at GLA hole destruction

| Field | Value |
|---|---|
| Card ID | `WI-A1-12` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): engine defect surfaced by deterministic headless runs |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | none |
| Diagrams | none (bug fix) |

## Goal

Diagnose and fix the deterministic engine stall at logic frame 10435 of the
AlpineAssaultUSAvGLA headless scenario (both windowless-loop and `SimulationHost` stepping),
right after a `GLAHoleCommandCenter` is destroyed: the logic thread spins forever inside
`SleepyUpdateList.RebalanceChild` (`src/Panopticon.Simulation/Logic/GameLogic.cs`). Long headless
matches cannot pass this state, which blocks the A1 host on full skirmish scenarios.

## Context to read (and only this)

- `src/Panopticon.Simulation/Logic/GameLogic.cs`: `SleepyUpdateList` (Rebalance/RebalanceChild/
  RebalanceParent/IsLowerPriority) and `GameLogic.Update`'s wake loop.
- `src/Panopticon.Simulation/Logic/Object/Die/OCLUpdate.cs` or the GLA hole spawn path
  (`GLAHoleCommandCenter` destruction spawns `GLACommandCenter` via OCL): the state right
  before the stall.
- Repro: TestHarness headless, `Scenario: AlpineAssaultUSAvGLA`; the stall reproduces on
  pristine `main` as well (pre-existing, not A1-introduced).

## Steps

1. Reproduce under a debugger or with periodic stack dumps; capture the stuck module pair
   (which two `UpdateModule`s the rebalance oscillates between).
2. Determine the root cause candidates: priority-tie handling in `IsLowerPriority` (`>` only,
   no tiebreak, so equal priorities can swap back and forth), or a module repeatedly setting
   its own wake frame into the past from inside `Update`.
3. Fix with a stable tiebreak (e.g. compare `IndexInLogic` on priority ties) or the specific
   module defect found; keep the heap invariants (`Validate` passes).
4. Verify: the AlpineAssaultUSAvGLA headless run passes frame 10435 and reaches a decided
   outcome + runout; `SeededRunTests` and the P7 gates still pass.

## Files likely to change

- `src/Panopticon.Simulation/Logic/GameLogic.cs` (or the specific module found at fault)

## Acceptance criteria

- [ ] The repro scenario completes headless (decided outcome + runout exit).
- [ ] `dotnet test src --filter Category!=Interactive` passes with no new failures.
- [ ] The P7 regression suite passes unchanged.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
```

## Out of scope / guardrails

- Do not rewrite the sleepy-update structure; fix the stall's cause.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
