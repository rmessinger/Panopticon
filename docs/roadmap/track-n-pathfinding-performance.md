# Track N: Multi-Unit Pathfinding Performance, Deadlocks, and Layered Navigation

> Part of the [Large-Scale RTS Roadmap](README.md). This track cuts the cost of path requests
> issued by multi-unit orders (army-AI squads, player selections, scripted group moves), then makes
> those moves live: units that hold an order keep progressing or settle, and groups crossing a
> narrow passage queue through it in order. It then splits navigation into strategic, main-path,
> local-route, and steering layers.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the multi-unit pathfinding track.

## Track ordering rationale

B4 closed with a request-rate verdict (the flat async queue meets its deadline; no hierarchical
layer). It never measured what one multi-unit order costs. M4's full-scale run then showed the
order shape: 912 full-map searches on the worst decision tick, 664 of them from blocked
followers. N0 measures per-request and per-order cost first, so every later phase targets a
measured share. N1 makes each search cheaper without changing results. N2 and N4 then cut the
number of searches and move them off the logic thread; N3 is gated on what remains. N5 verifies
the budgets at scale. Evidence, budgets, and design live in the
[deep dive](deep-dives/n-multi-unit-pathfinding.md).

N5 left the M4 idle-stall check failing in both option modes and kept `AsyncOrderPaths` off until
it holds. N6 attributes stalls per unit and reproduces each deadlock mechanism in seeded cases.
N7 closes path liveness gaps, N8 resolves blocked units, and N9 spreads crowded destinations; all
three depend only on N6. N10 verifies the targets and feeds the default-flip decision. Design:
[deadlock deep dive](deep-dives/n-navigation-deadlocks.md).

N11 to N14 add proactive queueing at narrow passages, complementing N8's reactive resolution.
N11 detects passages along routes and baselines passage cases, N12 forms ad hoc queues with a
shared lane path and unit order, N13 handles opposing flows and squads, and N14 verifies against
N10. Design: [passage deep dive](deep-dives/n-confined-passage-queueing.md).

N15 to N20 split navigation into scoped layers. N15 gives each unit a layered plan and baselines
long-route, replan, and steering cases. N16 builds a sector-and-portal strategic graph (pure, so it
can start early), N17 plans the reachable main path inside a corridor of the next strategic legs,
N18 keeps squads and crowds together as navigation groups, and N19 adds reciprocal steering with N8
as fallback. N20 verifies every Track N target with the layers on. Design:
[layered navigation deep dive](deep-dives/n-layered-navigation.md).

## Phases

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps
> live in [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| N0. Path cost baseline | Measure path cost per tick, per request origin, and per multi-unit order, with a seeded scenario for player selections and army-AI squads. | B1 records carry a `path` span, request counts by origin, nodes expanded, and failed searches for selections of 1 to 1000 units and the M4 army-AI probe; the report names the dominant request origin and confirms or revises the authored budgets. | Done | B1, B4, M4 |
| N1. Search core efficiency | Make each grid search allocation-free, reject unreachable goals without flooding, and stop cloning the whole graph for worker snapshots. | A seeded corpus of start/end pairs returns identical waypoints before and after; per-search allocation is zero in steady state; unreachable requests cost a region lookup; snapshot cost scales with passability bytes, not node objects. | Done | N0 |
| N2. Shared group requests | Serve blocked followers with bounded local landings and route squad-less multi-unit selections through group movement behind an option. | Full searches per multi-unit order are bounded by groups plus region-crossing followers in the N0 scenario and the M4 probe; option-off behavior is unchanged. | Done | N1 |
| N3. Shared-destination fields | Compute one bounded goal field per shared destination when N2 leaves many full searches toward the same goal, gated on measurement. | The gate decision is recorded with numbers; if opened, paths from the field match search costs within tolerance and the residual path span meets budget. | Done (gate closed) | N2 |
| N4. Off-thread order paths | Compute order-driven paths on the async queue behind an option, with units holding until their path lands. | With the option on, the logic-thread `path` span on order ticks drops to submission cost; completions stay inside the frame bound; M4 stall and squad invariants hold. | Done | N1 |
| N5. Scale verification | Re-run N0 cases and the M4 army-AI probe with N1 to N4 enabled. | Every authored path budget is met or the miss is recorded with its residual cause; M4 invariants and engagement checks pass. | Done | N2, N3, N4 |
| N6. Deadlock detection baseline | Attribute every stalled unit to a reason and blocker, and reproduce each deadlock mechanism in a seeded `NavigationDeadlock` scenario. | Per-unit stall records and wait-for cycles are reported for the M4 probe and six cases (head-on pair, opposed corridor, crowded goal, idle blocker, async no-route, offset retrace); the M4 idle-stall failure is attributed. | Not started | N5 |
| N7. Path liveness | Settle units whose path fails, throws, or goes stale, and validate follower offset routes. | No unit holds `AwaitingPath` or an empty route after its order fails; `impassableRetracePoints` reads 0 with `ValidateOffsetRoutes` on. | Not started | N6 |
| N8. Blocked-unit resolution | Resolve blocked units by wait, friendly give-way, and bounded local repath behind an option. | Head-on and opposed-corridor cases settle with 0 wait-for cycles; N5 path budgets still met. | Not started | N6 |
| N9. Destination crowding | Give selections distinct goal slots and let units crowd-arrive behind an arrived friendly. | The crowded-goal case ends with 0 units holding routes. | Not started | N6 |
| N10. Deadlock verification | Re-run N6 cases and the M4 probe over 3 seeds with all options on, then each off in turn. | Every deadlock target has a verdict; the M4 stall check passes run over run or the miss is attributed; a default-flip recommendation is recorded. | Not started | N7, N8, N9 |
| N11. Passage detection and baseline | Find narrow passages along unit routes from grid cross-sections, and baseline seeded passage cases. | `PassageFinder` returns throat, width, and lane count for gaps and corridors and none for one-sided walls; five passage cases record option-off settle frames, mouth jams, and cycles. | Not started | N6 |
| N12. Passage queues | Form ad hoc queues of allied units crossing the same passage, with one shared lane path, deterministic order, hold before the mouth, and release behind an option. | `passage-column-40` and `passage-converge-2x20` settle with 0 mouth jams and 0 cycles; open-ground moves are unchanged with the option on. | Not started | N11 |
| N13. Opposing flow and group entry | Share a passage between opposing flows by lane split or bounded grants, and enter squads and clusters as contiguous blocks. | Single-lane and two-lane opposing cases settle with 0 cycles; `passage-squad-12` has 0 offset waypoints on impassable cells inside the passage. | Not started | N12 |
| N14. Passage verification | Re-run passage cases, N6 cases, and the M4 probe over 3 seeds with and without queueing and N8. | Every passage target has a verdict; the `passage` span meets its budget or the miss is attributed; a `PassageQueueing` default-flip recommendation is recorded. | Not started | N10, N13 |
| N15. Layered navigation contract and baseline | Give each unit a `NavigationPlan` whose layers own disjoint parts of its route, and baseline long-route, replan, and steering cases. | All route writes go through `NavigationPlan` with unchanged baselines; five layered cases and per-layer spans record an option-off baseline. | Not started | N10, X6 |
| N16. Strategic sector graph | Build a sector-and-portal graph over the grid with incremental sector rebuilds, and plan coarse portal routes on it. | Strategic routes exist exactly when regions are connected; abstract expansions meet the cost target; a structure placement rebuilds only touched sectors within budget. | Not started | N1 |
| N17. Corridor main path | Plan the reachable main path inside a corridor of the next strategic legs, extended ahead of the unit, behind `LayeredNavigation`. | Path quality and order-cost targets met on the N1 corpus and N0 cases; 0 seam gaps; passability changes replan only affected legs. | Not started | N15, N16 |
| N18. Group local router | Move squads, clusters, and crowds as persistent navigation groups that follow one spine with width-adaptive slots, behind `GroupRouting`. | Cohesion targets met; layered and passage squad cases settle with 0 waypoints in walls; groups hand off to passage queues and resume. | Not started | N14, N17 |
| N19. Unit steering | Steer moving ground units around neighbors and nearby obstacles with reciprocal velocity obstacles behind `UnitSteering`, with N8 as fallback. | Head-on and crossing cases pass with 0 give-way events and 0 gate stops; the `steer` span meets budget. | Not started | N10, N15 |
| N20. Layered navigation verification | Re-run every Track N case and the M4 probe over 3 seeds with layers on, then each layer off. | Every layered target has a verdict; N10 and N14 targets still hold; default-flip recommendations recorded for the three options. | Not started | N18, N19 |

## Phase resources

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| N0-N5 | [Multi-unit pathfinding](deep-dives/n-multi-unit-pathfinding.md) | [§8](deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) | `WI-N0-01..02`, `WI-N1-01..03`, `WI-N2-01..02`, `WI-N3-01`, `WI-N4-01..02`, `WI-N5-01` ([plan](implementation/track-n-pathfinding-performance.md)) |
| N6-N10 | [Navigation deadlocks](deep-dives/n-navigation-deadlocks.md) | [§8](deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams) | [`WI-N6-01`](agent/cards/WI-N6-01-movement-progress-and-stall-attribution.md), `WI-N6-02`, `WI-N7-01..02`, `WI-N8-01..02`, `WI-N9-01..02`, `WI-N10-01` ([plan](implementation/track-n-pathfinding-performance.md#navigation-deadlocks-n6-n10)) |
| N11-N14 | [Confined passage queueing](deep-dives/n-confined-passage-queueing.md) | [§8](deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams) | [`WI-N11-01`](agent/cards/WI-N11-01-passage-finder.md), `WI-N11-02`, `WI-N12-01..02`, `WI-N13-01..02`, `WI-N14-01` ([plan](implementation/track-n-pathfinding-performance.md#confined-passage-queueing-n11-n14)) |
| N15-N20 | [Layered navigation](deep-dives/n-layered-navigation.md) | [§9](deep-dives/n-layered-navigation.md#9-architecture-diagrams) | [`WI-N16-01`](agent/cards/WI-N16-01-sector-graph.md), [`WI-N19-01`](agent/cards/WI-N19-01-steering-core.md), `WI-N15-01..02`, `WI-N16-02`, `WI-N17-01..02`, `WI-N18-01..03`, `WI-N19-02`, `WI-N20-01` ([plan](implementation/track-n-pathfinding-performance.md#layered-navigation-n15-n20)) |

## Cross-track notes

- B4 owns partition scaling and the async queue; N reuses `PathRequestQueue`. B4's hierarchical
  gate measured request latency only; N16 adds a strategic layer for route scope and local
  replanning, and N20 measures its cost.
- E1 owns locomotor surface layers and the `Pathfinder` stubs. N keeps one passability layer;
  N1's region labels and N16's sector graph become per-layer when E1 lands.
- A4-32 extracts the pathfinding seam after N: it wraps N's `Navigation` surface rather than the
  pre-N one.
- X6 extracts `UnitNavigation` from `AIUpdate`; N15's `NavigationPlan` lives on it.
- A4's rank hierarchy chooses goals; N15 to N20 layers choose routes. Navigation groups are not
  `SquadHierarchy` squads.
- M5 does not depend on N. N's results lower the tick cost M5's live view measures, and N6 to N10
  remove stalled units from its battles.
- N6's corridor cases and N11's passage cases reuse the `RoutingChoke` wall-gap setup (M5
  addendum).
- E1 owns unit-size clearance in the grid; N11 measures passage width along routes and leaves
  the grid point-sized.
