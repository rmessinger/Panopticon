# Track M · M4: Army AI implementation plan

Source of truth: [M4 checklist items](../implementation-checklist.md#m4-army-ai),
[mass battle charter](../deep-dives/mass-battle-1k.md) :
[M4 architecture diagrams §9](../deep-dives/mass-battle-1k.md#9-architecture-diagrams). Cards:
[`WI-M4-01..06`](../agent/cards/).

## Audit findings (WI-M4-01)

Code read at the audit: `AIPlayer.cs`, `Hierarchy/*.cs` (`HierarchicalAIDecisionPolicy`,
`SquadHierarchy`, `Squad`, the three rule-based policies), `OrderProcessor.cs`, `LogicStepper.cs`,
`Player.LogicTick`, and the M1 probe runner.

| Finding | Evidence | Consequence |
|---|---|---|
| Soldier orders act on the player's current selection, not their own unit | `OrderProcessor.cs` `MoveTo` loops `player.SelectedUnits` (line 98); `AttackObject`/`ForceAttackObject` loop `player.SelectedUnits` (line 350); `RuleBasedSoldierDecisionPolicy` emits `CreateAttackObject`/`CreateMoveOrder` with no preceding `SetSelection` | On a headless probe (empty selection) every soldier order is a silent no-op; the hierarchy never actuates |
| Selection exists headless | `Game.Selection` is a non-null `SelectionSystem` created in the `Game` constructor; the `SetSelection` order path resolves ids through it | Squad-scoped selection is a viable actuation bus with no engine prerequisites |
| Reference goals are squad-uniform | `RuleBasedCommanderDecisionPolicy.GoalFor` ignores member identity: every member of a squad receives the same goal | One order per squad is semantically exact for the reference policies; per-member heterogeneity is a future policy concern |
| Shape at 1000 units per side | `SquadHierarchy.MaxSquadSize = 8`, id-order auto-grouping: 125 ground squads, 125 commanders, 1 general per side | No extra squad-group layer and no decision staggering needed; both would add structure without removing measurable work |
| Synchronize cost is front-loaded | Initial bulk assignment scans open squads per new unit (at most 1000 x 125 member checks, once); steady state enumerates `OwnedObjects` once per decision tick (every 60 frames) | Keep `Synchronize` as-is; its per-sync allocations (`Where().ToList()` per squad) are recorded for B2, not fixed here |
| Aircraft join ground squads | `SquadHierarchy.EligibleUnits` filters on `CanAttack` and non-structure/non-harvester/non-dozer only; parked `AmericaJetAurora` units are eligible | Ground squads must exclude `ObjectKinds.AIRCRAFT` before the hierarchy runs on the probe |

## Decisions

1. Actuation form: per-squad `SetSelection` followed by the squad's order. `WI-M4-03` makes
   soldier orders reach their squad (correctness: one selection per squad, orders still
   per-member and idempotent while goals are squad-uniform). `WI-M4-04` collapses each squad's
   move orders to one `MoveTo` and routes it through `GroupMovement.MoveSquad` (efficiency and
   the path-request bound).
2. Squad size stays 8; no squad-group layer; no decision staggering. The general already scales
   to 125 direct reports (one dictionary loop per general tick).
3. Ground-only squads: `WI-M4-03` excludes `ObjectKinds.AIRCRAFT` from `EligibleUnits`;
   `WI-M4-05` introduces the separate air hierarchy (air squads, missions, re-tasking rearmed
   jets at the airfield).
4. Probe army-AI mode: a TestHarness knob (`MassBattleCapacityProbeArmyAi`, default off) keeps
   the scripted M1 behavior unchanged. In army-AI mode the probe leaves both players' AI enabled
   with `AIDecisionPolicyType.Hierarchical`, skips the scripted general attack and jet reissue,
   and adds a squad-assignment invariant block to the engagement report (ground squads,
   unassigned eligible units, commanderless squads, max members per squad).
5. AI cost budget authoring (`WI-M4-06`): the `ai` span from `WI-M4-02` is measured against the
   33.3 ms tick budget; the audit proposes a per-decision-tick budget of at most 5 percent of
   the tick (about 1.7 ms median) and a stall threshold authored from the invariant block;
   `WI-M4-06` records the final numbers.
6. Names: the span is `ai` (`system.ai.spanMs` statistics); the probe knob is
   `MassBattleCapacityProbeArmyAi`; the invariant block is `squadInvariant` in the engagement
   report; the group-order path is `GroupMovement.MoveSquad` behind multi-unit `MoveTo`.

## Plan

| Card | Deliverable | Status |
|---|---|---|
| [`WI-M4-01`](../agent/cards/WI-M4-01-army-hierarchy-scale-audit.md) | This audit and the decisions above | Done |
| [`WI-M4-02`](../agent/cards/WI-M4-02-ai-decision-cost-counter.md) | `ai` span: allocation-free accumulation of `OnDecisionTick` across all AI players, emitted by `LogicStepper` after `gameLogic`; install-free tests | Ready |
| [`WI-M4-03`](../agent/cards/WI-M4-03-army-hierarchy-on-probe.md) | Ground-only eligibility, per-squad selection actuation, probe army-AI mode, assignment invariants, scale + Interactive tests | Ready |
| [`WI-M4-04`](../agent/cards/WI-M4-04-squad-group-orders.md) | One `MoveTo` per squad, multi-unit `MoveTo` routed through `GroupMovement.MoveSquad`, path-request bound tests | Ready |
| [`WI-M4-05`](../agent/cards/WI-M4-05-air-commander.md) | Air squads, strike/patrol/escort missions, re-tasking rearmed parked jets, no scripted jet reissue in army-AI mode | Ready |
| [`WI-M4-06`](../agent/cards/WI-M4-06-ai-budget-and-stall-report.md) | Budget and stall thresholds authored, full-scale army-AI run, verdict, phase closeout | Ready |
