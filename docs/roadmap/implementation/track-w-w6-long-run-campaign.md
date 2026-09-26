# Track W · W6: Long-run campaign

> Audience: human overview above the divider; agent-context detail below it.

Implements the W6 row of [Track W](../track-w-western-front.md). Design source of truth: [W6 long-run campaign](../deep-dives/w6-long-run-campaign.md), including its architecture
diagrams.

## Scope

Run the July-November campaign with one-minute operational ticks, domain-state replay, and save/load validation.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| W6-1 | `WI-W6-01` | Campaign file. |
| W6-2 | `WI-W6-02` | Historical OOB investigation. |
| W6-3 | `WI-W6-03` | Long-run clock plan. |
| W6-4 | `WI-W6-04` | Domain snapshot save/load. |
| W6-5 | `WI-W6-05` | Replay indexing. |
| W6-6 | `WI-W6-06` | Long-run metrics. |
| W6-7 | `WI-W6-07` | Approximation error report. |
| W6-8 | `WI-W6-08` | Evidence report. |

---

## Agent context

Code hooks: see the deep dive appendix.

## Dependency notes

Draft cards must remain Draft until their prerequisites are Done and the named planned types are
reconciled with source.
