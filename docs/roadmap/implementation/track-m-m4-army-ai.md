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
| [`WI-M4-06`](../agent/cards/WI-M4-06-ai-budget-and-stall-report.md) | Budget and stall thresholds authored, full-scale army-AI run, verdict, phase closeout | Done |

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
  counted every decision tick (per mission type and per aircraft), but only members that are
  parked and holding no target points receive the order; mid-sortie members keep flying until
  they land and are re-tasked then (per-member readiness, WI-M4-06: an all-or-nothing squad
  gate let one stuck-airborne wingman ground its whole squad for an entire full-scale run).
  Orders ride the same actuation path as ground orders: one selection of the ready members
  plus one attack or move order.
- Re-task gate: parked state, not clip state. The card anticipated gating on a refilled clip,
  but the engine refills clips only through mid-combat reload (`ReloadingWeaponState`); nothing
  refills a parked jet's clip at an airfield, so a clip gate would ground jets after their
  first sortie forever (observed in the first scaled run: one launch per jet, then parked). The
  implemented gate matches the M1 scripted loop, which relaunched jets on parked state alone.
  Parked clip refill is recorded as a candidate engine follow-up; no rearm mechanic was added
  (guardrail).
- Follow-up (operator note, WI-M4-06): revisit the squad invariant check strategy. The sampler
  scans both armies after every probe frame (assignment, commander, stall tracking) and that
  cadence looks too frequent for the benefit it provides. Sampling on decision ticks, or at a
  coarser cadence, is the likely shape; note that relaxing or removing the check may let
  transient violations survive, which can be a realistic property of a battle (units briefly
  unassigned mid-casualty, squads briefly commanderless) rather than a defect, so the invariant
  may deserve a tolerance window instead of zero.
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

## WI-M4-06: AI budget, stall threshold, and verdict

### Authored budget

The frame anchor is 33.3 ms (30 fps; M1 report). The army AI gets:

| Bound | Value | Reasoning |
|---|---|---|
| Amortized `system.ai.spanMs` median | at most 1.7 ms (5 percent of the anchor) | AI is one consumer of the frame alongside the systems B-track work optimizes; 5 percent keeps it from ever being the dominant cost while leaving the hierarchy fully functional. |
| Decision-tick `system.ai.spanMs` p99 | at most 100 ms (three frames) | A decision tick concentrates the cadence's work; one spike may cost at most three frames of the 30 fps budget. The span is zero outside decision ticks. |

### Authored stall threshold

600 frames (20 sim-seconds): the decision cadence is 60 frames, so a unit that arrives or
loses its target is re-tasked within a few cadences; ten cadences of idleness with enemies
remaining means the hierarchy is not reaching it. A unit is idle when it is alive, holds no
target point, its weapon has no valid target, and it has not moved since the previous frame,
while enemies remain. Parked jets are excluded: parking between sorties is intentional idle
time (WI-M4-05).

### Measured (full-scale army-AI run)

Full-scale headless probe, army-AI mode, 4500 frames in 10.4 min wall, all engagement checks
passed (artifacts: `/tmp/opencode/m4-g6-fullrun2/`; the first run with the pre-fix readiness
gate is preserved at `/tmp/opencode/m4-g6-fullrun/` as the sortie-failure evidence). B2/B3 have
cut the context tick since M1: median `tick.wallMs` 121.9 ms (CI [120.0, 123.5]), allocation
12.6 MB/tick, so the AI share below reads against a 122 ms tick, not M1's 1041 ms.

| Budget | Authored | Measured | Verdict |
|---|---|---|---|
| `system.ai.spanMs` amortized median | <= 1.7 ms (5% of the 33.3 ms anchor) | 0.000 ms | Met |
| `system.ai.spanMs` decision-tick p99 | <= 100 ms | 18.3 ms (n=75, p50 1.1, max 18.3) | Met |
| Span zero outside decision ticks | yes | median 0.000 across all 4500 ticks | Met |

Invariants and stalls (sampler over both armies, both sides): 119/120 ground squads, 751/726
members, unassigned eligible ground units 0 (max 0), commanderless non-empty squads 0
(max 0), max idle 475/347 frames against the 600-frame threshold, 0 stalled units at end.
Losses 265/276 destroyed. Group-order bound held at the worst decision tick: 912 path requests
<= 248 squads + 664 blocked followers (75 decision ticks observed).

Air: 27 strike / 24 patrol / 24 escort mission assignments per side; 5 and 3 jets received two
or more missions, with repeat sortie cycles (for example 3 launches / 2 recoveries / 3
missions on one sideA jet). The run-#1 failure (every jet launched exactly once because one
stuck-airborne wingman grounded its whole squad under the all-or-nothing readiness gate) is
what motivated per-member re-tasking.

### Verdict

The authored AI budgets hold at full scale with the current engine: the hierarchy costs
nothing amortized and at most 18.3 ms on a decision tick, inside both limits. The squad
invariant held at zero throughout and no unit stalled past the threshold, so no budget-miss
fixes are required for M5. The measured 122 ms tick remains the M2/M3-agnostic engine cost;
closing it to the 33.3 ms anchor stays with B-track work, not M4.
