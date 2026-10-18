# Task Card `WI-M5-03`: Army-eliminated outcome

| Field | Value |
|---|---|
| Card ID | `WI-M5-03` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | P3 (done) |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Let a battle without production structures reach a P3 outcome: a side is eliminated when it has
no living combat unit, and a frame limit decides stalled battles by unit count.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decision 3.
- `src/Panopticon.Simulation/Logic/VictoryConditionRule.cs`: `VictoryConditionRule`,
  `DefaultVictoryConditionRule`, `NeverDefeatedVictoryConditionRule`.
- `src/Panopticon.Simulation/Logic/VictorySystem.cs`: `ConditionRule`, `ApplyOptions`,
  `EvaluateFrameLimit`, `CurrentOutcome`.
- `src/Panopticon.Simulation/Logic/GameOutcome.cs`.
- `src/Panopticon.Simulation/Logic/VictoryOptions.cs` and `Game.VictoryOptions`.

## Steps

1. Add `ArmyEliminatedVictoryConditionRule`: a player is defeated when they own no living
   non-structure object that can attack. Structures (airfields) never keep a player alive;
   parked aircraft do.
2. Make the rule selectable from outside Simulation (a public factory or a `VictoryOptions`
   field), matching how `VictoryOptions` already exposes the frame limit.
3. Tests on the mocked game: elimination decides with `OutcomeReason.Eliminated` and the
   survivor as winner; mutual elimination in one check goes to `SuddenDeath`; the frame limit with
   `VictoryScoreMetric.UnitCount` decides as `ScoreDecision`; a side holding only an airfield is
   defeated.

## Files likely to change

- `src/Panopticon.Simulation/Logic/VictoryConditionRule.cs`
- `src/Panopticon.Simulation/Logic/VictoryOptions.cs`
- `src/Panopticon.Simulation/Logic/VictorySystem.cs`
- `test/Panopticon.Simulation.Tests/Logic/ArmyEliminatedVictoryConditionRuleTests.cs` (new)

## Acceptance criteria

- [ ] An army battle reaches `OutcomeStatus.Decided` with `Eliminated` or `ScoreDecision`.
- [ ] Default skirmish rules and existing P3 tests are unchanged.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~Victory|FullyQualifiedName~ArmyEliminated"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No change to `DefaultVictoryConditionRule` or the 30-frame check cadence.
- No new score metric.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
