# Task Card `WI-M4-01`: Army hierarchy scale audit

| Field | Value |
|---|---|
| Card ID | `WI-M4-01` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none (A4-3, B4, M1 merged) |
| Diagrams | [M4 army hierarchy](../../deep-dives/mass-battle-1k.md#m4-army-hierarchy) |

## Goal

Record how the A4-3 hierarchy behaves when sized for 1000 units per side, and fix the sizing and actuation decisions WI-M4-03..05 implement: squad size, whether squad groups are needed between general and commanders, decision staggering, aircraft eligibility, and per-squad order actuation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 6 and section 9 "M4 army hierarchy".
- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md` sections 2.1-2.4.
- `src/Panopticon.Simulation/Logic/AI/AIPlayer.cs`: `DefaultDecisionCadenceFrames` (60), `OnDecisionTick`, `DispatchOrders`.
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs`, `SquadHierarchy.cs`, `Squad.cs`, `RuleBasedGeneralPolicy.cs`, `RuleBasedCommanderDecisionPolicy.cs`, `RuleBasedSoldierDecisionPolicy.cs`.
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs` `MoveTo`/`AttackMove` and `AttackObject` cases: they act on `player.SelectedUnits`.
- `src/Panopticon.Simulation/Navigation/GroupMovement.cs`: `MoveSquad` (no engine caller yet).
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs` lines 112-135 (player setup, `DisableAI`) and `IssueGeneralAttack`.

## Steps

1. Confirm by reading and one install-free test whether soldier orders reach the intended unit: `RuleBasedSoldierDecisionPolicy` returns `CreateMoveOrder`/`CreateAttackObject` with no preceding `CreateSetSelection`, and `OrderProcessor` applies them to the current selection. Record the finding.
2. Count the static shape at 1000 units: squads per side at `MaxSquadSize` 8 (about 125), directives per general tick, goals and orders per decision tick.
3. Review `SquadHierarchy.Synchronize` cost: `OpenSquad`/`SquadForTeam` sort all squads per newly assigned unit; estimate first-sync cost at 1000 units and state whether it must become linear.
4. Note that `EligibleUnits` admits aircraft (they `CanAttack`), so jets currently join ground squads.
5. Decide and record, with reasons: squad size (keep 8 or configurable), squad-group layer (yes or no), decision staggering across frames (all squads on one frame or spread), aircraft split into air squads, and the actuation form (per-squad `SetSelection` plus one order, served by `GroupMovement` for multi-unit `MoveTo`).
6. Write `docs/roadmap/implementation/track-m-m4-army-ai.md` with the findings, decisions, and card sequence; index it in `docs/roadmap/implementation/README.md`; reconcile names in WI-M4-02..06.

## Files likely to change

- `docs/roadmap/implementation/track-m-m4-army-ai.md` (new)
- `docs/roadmap/implementation/README.md`
- `docs/roadmap/agent/cards/WI-M4-02..06` (name reconciliation only)
- `test/Panopticon.Simulation.Tests/Logic/AI/Hierarchy/` (one install-free actuation test, if step 1 needs it)

## Acceptance criteria

- [ ] The actuation finding from step 1 is recorded with file and line evidence.
- [ ] Each decision in step 5 is stated with its reason and the card that implements it.
- [ ] The implementation plan lists WI-M4-01..06 in dependency order.

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~Hierarchy"
```

## Out of scope / guardrails

- No engine changes beyond an evidence test; implementation belongs to WI-M4-03..05.
- No neural ranks; the rule-based reference policies are M4's scope.

## On completion

- Set Status above to `Done`; set WI-M4-02..06 to `Ready` once reconciled and their prerequisites are met.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none expected.
