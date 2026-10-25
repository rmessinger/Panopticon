# Track N: Multi-Unit Pathfinding Performance

> Part of the [Large-Scale RTS Roadmap](README.md). This track cuts the cost of path requests
> issued by multi-unit orders: army-AI squads, player selections, and scripted group moves.
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
| N5. Scale verification | Re-run N0 cases and the M4 army-AI probe with N1 to N4 enabled. | Every authored path budget is met or the miss is recorded with its residual cause; M4 invariants and engagement checks pass. | Not started | N2, N3, N4 |

## Phase resources

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| N0-N5 | [Multi-unit pathfinding](deep-dives/n-multi-unit-pathfinding.md) | [§8](deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) | `WI-N0-01..02`, `WI-N1-01..03`, `WI-N2-01..02`, `WI-N3-01`, `WI-N4-01..02`, `WI-N5-01` ([plan](implementation/track-n-pathfinding-performance.md)) |

## Cross-track notes

- B4 owns partition scaling and the async queue; N reuses `PathRequestQueue` and does not reopen
  the hierarchical gate except through N3's own measurement.
- E1 owns locomotor surface layers and the `Pathfinder` stubs. N keeps one passability layer;
  N1's region labels become per-layer when E1 lands.
- A4-32 extracts the pathfinding seam after N: it wraps N's `Navigation` surface rather than the
  pre-N one.
- M5 does not depend on N. N's results lower the tick cost M5's live view measures.
