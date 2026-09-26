# Track B · B6: Distributed simulation

> Audience: human overview above the divider; agent-context detail below it.

Implements the B6 row of [Track B](../track-b-scale.md). Design source of truth: [B6 distributed simulation](../deep-dives/b6-distributed-simulation.md), including its architecture
diagrams.

## Scope

Implement distribution through the A4 seam, starting with the Western Front domain. RTS/Bepu distribution remains gated on F4a.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| B6-1 | `WI-B6-01..03` | Partition contract, halo derivation, boundary message design. |
| B6-2 | `WI-B6-04..07` | Western Front in-process multi-shard implementation and evidence. |
| B6-6 | `WI-B6-08..10` | Compact binary codec, multi-process transport, and fault diagnostics. |

---

## Agent context

Code hooks: see the deep dive appendix.

## Dependency notes

Draft cards must remain Draft until their prerequisites are Done and the named planned types are
reconciled with source.
