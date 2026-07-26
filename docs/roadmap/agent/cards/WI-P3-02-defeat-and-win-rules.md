# Task Card `WI-P3-02`: Defeat and win rules

| Field | Value |
|---|---|
| Card ID | `WI-P3-02` |
| Roadmap phase | [Track P · P3](../../track-p-playable-baseline.md): sub-phase P3-2, see [deep dive §1.2](../../deep-dives/p3-victory-defeat.md#12-defeat-detection) and [§4](../../deep-dives/p3-victory-defeat.md#3-sub-phases) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P3-01`, `WI-P3-03`, and `WI-P1-07` ownership tracking (`Player.OwnedObjects`, `BuildingCount`, `UnitCount`) |
| Diagrams | [P3 Architecture Diagrams](../../deep-dives/p3-victory-defeat.md#7-architecture-diagrams) |

## Goal

Implement the default configurable defeat rule and win rule: a player is defeated when they lack qualifying command/production structures and rebuild means, and a side wins when all non-allies are defeated.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:48-72`: default Generals defeat rule, win rule, ownership dependency, and configurable-rule seam.
- `docs/roadmap/deep-dives/p3-victory-defeat.md:118-124`: P3-1/P3-2/P3-3 sequencing.
- `docs/roadmap/implementation-checklist.md:205-208`: defeat-rule AC.
- `docs/roadmap/implementation-checklist.md:212-214`: all-non-allies-defeated win AC.
- `src/OpenSage.Game/Logic/Player.cs:126-134`: P1 ownership/relationship fields consumed by this card.
- `src/OpenSage.Game/Logic/Player.cs:350-380`: existing production-prerequisite scan; do not copy this pattern for victory scans.
- `src/OpenSage.Game/Logic/Player.cs:785-804` and `src/OpenSage.Game/Logic/Player.cs:863-884`: allies/enemies and relationship lookup.
- `src/OpenSage.Game/Logic/Player.cs:807-839`: maintained owned-object set and counts from `WI-P1-07`.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:291-297` and `src/OpenSage.Game/Logic/Object/GameObject.cs:586-631`: owner, structure flag, and kind checks.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:788-798`: object-level production capability check.
- `src/OpenSage.Game/Logic/Object/ObjectKinds.cs:148-218` and `src/OpenSage.Game/Logic/Object/ObjectKinds.cs:379-383`: relevant structure/command/rebuild kind flags.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70`: headless test base; tests must run in CI.

## Steps

1. Add a small configurable victory/defeat rule seam used by `VictorySystem`; keep the default rule selected by default.
2. Implement the default defeat predicate using maintained `Player.OwnedObjects`, `BuildingCount`, and `UnitCount`. Do not scan all game objects every check.
3. Treat the default Generals rule as: no qualifying command/production structures and no rebuild means (no dozer/worker equivalent and insufficient resources). Keep exact object classification narrow and data-driven/configurable; if a flag is ambiguous, document the gap and keep the seam.
4. Implement win determination using P1 relationships/teams: when all players not allied to player X are defeated, set `GameOutcome.Status = Decided`, `Reason = Eliminated`, and the correct `WinningPlayer`/`WinningTeam` fields.
5. React to the incremental lifecycle notification from `WI-P3-03`; do not add a per-frame full-object scan.
6. Add headless `[Fact]` tests in `src/OpenSage.Game.Tests/Logic/Victory/VictoryRuleTests.cs` for player defeat within the check cadence and all-non-allies-defeated win determination.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/VictorySystem.cs`
- `src/OpenSage.Game/Logic/GameOutcome.cs`
- `src/OpenSage.Game/Logic/VictoryConditionRule.cs` (new, or equivalent rule seam)
- `src/OpenSage.Game.Tests/Logic/Victory/VictoryRuleTests.cs` (new)

## Acceptance criteria

- [x] A harness/headless scenario that removes a player's qualifying structures and rebuild means flags that player defeated within the configured check cadence.
- [ ] The default defeat rule is selectable via config/rule seam and remains the default (partial: rule seam is test-settable, not data-driven config).
- [x] Defeat checks consume `Player.OwnedObjects`/counts and incremental notifications, not a full-object scan.
- [x] When all non-allies of player X are defeated, `GameOutcome.Status = Decided` with X or X's team as winner and `Reason = Eliminated`.
- [x] Outcome assertions fail loudly if the expected winner/reason is wrong.
- [x] Headless `[Fact]` tests are not skipped and do not require game files.
- [x] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [x] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~VictoryRuleTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement `OrderType.EndGame`, sudden death, or time-limit score decisions here.
- Do not invent a deeper Generals rule than the deep dive states; keep ambiguous classification behind the configurable seam.
- Preserve all existing ownership-count behavior from `WI-P1-07`; do not replace it with scans.
- If `WI-P3-03` is not done, mark this card `Blocked` rather than implementing a polling workaround.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Player-defeat detection" item and the win-determination portion of "Win/lose determination + match-end flow" in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with the rule seam name and tests run.
- Confirm the required test file `src/OpenSage.Game.Tests/Logic/Victory/VictoryRuleTests.cs` was created and runs in CI.

## Result

- Added a configurable `VictoryConditionRule` seam with `DefaultVictoryConditionRule`, implemented queued defeat evaluation from maintained owned-object state plus cached rebuild costs, and resolved wins when every non-allied player on the opposing side is defeated.
- Added `VictoryRuleTests` covering defeat within cadence, preserved rebuild ability with a surviving builder plus cash, allied-side win resolution, and rule-seam substitution; full non-interactive validation passed.
