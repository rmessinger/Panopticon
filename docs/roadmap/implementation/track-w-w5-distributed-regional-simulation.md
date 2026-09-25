# Track W · W5: Distributed regional simulation

> Audience: human overview above the divider; agent-context detail below it.

Implements the W5 row of [Track W](../track-w-western-front.md). Design source of truth: [W5 distributed regional simulation](../deep-dives/w5-distributed-regional-simulation.md), including its architecture
diagrams.

## Scope

Apply B6 to Western Front first, using frontage strips and in-process JSON boundary exchange.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| W5-1 | `WI-W5-01` | Front-strip partition. |
| W5-2 | `WI-W5-02` | Shard slice state. |
| W5-3 | `WI-W5-03` | In-process JSON transport. |
| W5-4 | `WI-W5-04` | Lockstep coordinator. |
| W5-5 | `WI-W5-05` | Cross-boundary handoff. |
| W5-6 | `WI-W5-06` | Boundary effects. |
| W5-7 | `WI-W5-07` | Sharded versus reference evidence. |
| W5-8 | `WI-W5-08` | A5 shard visualization. |

---

## Agent context

Code hooks: see the deep dive appendix.

## Dependency notes

Draft cards must remain Draft until their prerequisites are Done and the named planned types are
reconciled with source.
