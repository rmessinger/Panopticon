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
| [`WI-M4-03`](../agent/cards/WI-M4-03-army-hierarchy-on-probe.md) | Ground-only eligibility, per-squad selection actuation, probe army-AI mode, assignment invariants, scale + Interactive tests | Done |
| [`WI-M4-04`](../agent/cards/WI-M4-04-squad-group-orders.md) | One `MoveTo` per squad, multi-unit `MoveTo` routed through `GroupMovement.MoveSquad`, path-request bound tests | Done |
| [`WI-M4-05`](../agent/cards/WI-M4-05-air-commander.md) | Air squads, strike/patrol/escort missions, re-tasking rearmed parked jets, no scripted jet reissue in army-AI mode | Done |
| [`WI-M4-06`](../agent/cards/WI-M4-06-ai-budget-and-stall-report.md) | Budget and stall thresholds authored, full-scale army-AI run, verdict, phase closeout | Ready |

## WI-M4-03: what landed

- `SquadHierarchy.EligibleUnits` excludes `ObjectKinds.AIRCRAFT`; ground squads stay
  ground-only until the air hierarchy (WI-M4-05).
- Actuation lives in `HierarchicalAIDecisionPolicy.DecideNextOrders`: soldier decisions are
  collected per squad, structurally equal orders (the reference commander's squad-uniform
  goals) collapse into one `SetSelection` of the decided members plus the single order, and
  per-member decisions fall back to one `SetSelection` per member. Every order now reaches
  exactly the units that decided it, including headless where the selection starts empty.
- Probe army-AI mode: `MassBattleCapacityProbeArmyAi` (default off). On: both side players get
  `AIDecisionPolicyType.Hierarchical` and keep their AI enabled while every other player is
  disabled; the scripted general attack and its refresh issue orders to jets only (jets keep
  the M1 sortie loop until WI-M4-05).
- Squad invariant: `MassBattleSquadInvariantSampler` samples both sides after every engagement
  frame (a superset of the decision-tick requirement; sampling starts after each side's first
  decision tick since the pre-AI hierarchy is empty by design). The engagement report gains a
  `squadInvariant` block: per side, ground squads, members, unassigned eligible ground units,
  commanderless non-empty squads, and the maxima over the run. Non-zero maxima are engagement
  failures.
- Tests: install-free `SquadHierarchyScaleTests` (1000-unit assignment, mass-casualty
  promotion, aircraft exclusion) and the rewritten `SoldierOrderActuationTests` (uniform
  collapse and per-member fallback); scaled Interactive
  `ScaledProbe_ArmyAiRunsHierarchyWithoutScriptedGroundOrders` runs the army-AI probe on the
  38+2 roster and asserts the invariant plus the standard engagement checks (contact, losses,
  sorties, rearm).
- Documented updates to A4-3 neural tests: the uniform collapse changes one squad's order list
  from N orders to a `SetSelection` plus one order, so count assertions moved from 3 to 2
  (`HierarchicalNeuralInferenceTests`); rank call counts are unchanged.

## WI-M4-04: what landed

- Multi-unit `MoveTo` and `AttackMove`, and the movement half of `AttackObject` and
  `ForceAttackObject`, route through `OrderProcessor.MoveUnits`: selected units in the same
  squad move via one `GroupMovement.MoveSquad` call (leader path plus offsets); squad-less and
  single-unit selections keep the exact per-unit `SetTargetPoint` path, so player orders and
  replays are unchanged. Weapons still target per unit in the attack cases, and move sounds are
  untouched.
- `GroupMovement.BlockedFollowerCount` counts blocked followers that fall back to their own
  `CalculatePath`; the squad path-request bound is squads plus blocked followers.
- The squad invariant sampler tracks bursts: a decision tick's orders land through the network
  buffer on the next frame, so each burst window spans the decision frame plus the one after it.
  The report's `squadInvariant` block gains decision ticks observed, max path requests per
  decision tick, squads and members at the worst tick, and max blocked followers per decision
  tick.
- Tests: install-free `GroupMoveOrderTests` (an 8-unit squadded selection costs one shared
  leader path and every member receives the target; a squad-less selection keeps per-unit
  orders) and the scaled Interactive probe test asserts max path requests per decision tick is
  at most squads plus blocked followers. The first Interactive run failed with 29 requests
  against 10 squads; the excess was uncounted blocked followers at contact, which motivated the
  counter and the bound's final form.

## WI-M4-05: what landed

- Aircraft form air squads in a second `SquadHierarchy` (`SquadDomain.Air`: attack-capable
  aircraft only) beside the ground hierarchy; ground eligibility keeps excluding aircraft. The
  air hierarchy is not persisted; it rebuilds on load from live objects, unlike the persisted
  ground hierarchy.
- The air rank is a seam like the others: `IAirCommanderDecisionPolicy` receives the air
  squads, the friendly ground squads' summaries, and the ground directive, and returns one
  `AirSquadMission` (strike, patrol, or escort) per squad. The reference commander rotates
  mission windows deterministically (three decision ticks each, strike, patrol, escort); strike
  attacks the general's directive target when one exists, else the nearest enemy attack unit;
  patrol alternates legs between the first and last friendly ground squad centroids; escort
  shadows the first friendly squad's centroid.
- `HierarchicalAIDecisionPolicy` calls the air commander after the ground loop. Missions are
  counted every decision tick (per mission type and per aircraft), but a squad receives its
  order only when every member is parked and holding no target points; mid-sortie squads keep
  flying until they land and are re-tasked then. Orders ride the same actuation path as ground
  orders: one selection per squad plus one attack or move order.
- Re-task gate: parked state, not clip state. The card anticipated gating on a refilled clip,
  but the engine refills clips only through mid-combat reload (`ReloadingWeaponState`); nothing
  refills a parked jet's clip at an airfield, so a clip gate would ground jets after their
  first sortie forever (observed in the first scaled run: one launch per jet, then parked). The
  implemented gate matches the M1 scripted loop, which relaunched jets on parked state alone.
  Parked clip refill is recorded as a candidate engine follow-up; no rearm mechanic was added
  (guardrail).
- In army-AI mode the probe is now fully AI-owned: the scripted `IssueGeneralAttack` and
  `RefreshOrders` are both skipped, so jets fly, return, and re-sortie under the air commander.
  The plain probe keeps its scripted behavior unchanged. The report gains an air-mission block
  (assignments per mission type per side, jets with two or more missions) and per-jet
  `missionsAssigned` beside launches and recoveries.
- Tests: install-free `AirCommanderTests` (air squads form separately from ground squads; the
  mission rotation is deterministic across ticks; a parked squad receives its mission while a
  mid-sortie squad is skipped but still counted; a spent clip does not block re-tasking) and
  the scaled Interactive army-AI test asserts all three mission types occur, squad invariants
  hold, and jets with multiple missions exist.
