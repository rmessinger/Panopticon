# Track B · B4: Spatial and pathfinding scaling

Source of truth: [deep-dives/b4-spatial-pathfinding-scaling.md](../deep-dives/b4-spatial-pathfinding-scaling.md) :
[Architecture diagrams](../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams).

Audience: human overview; agents load the cards.

| Card | Title | Checklist item |
|---|---|---|
| [`WI-B4-01`](../agent/cards/WI-B4-01-partition-counters.md) | Partition counters and hot-path audit | B4 |
| [`WI-B4-02`](../agent/cards/WI-B4-02-pair-once-partition-update.md) | Allocation-free, pair-once partition update | B4 |
| [`WI-B4-03`](../agent/cards/WI-B4-03-full-scale-verification.md) | Full-scale verification re-run | B4 |
| [`WI-B4-04`](../agent/cards/WI-B4-04-group-movement.md) | Group movement service | B4 |
| [`WI-B4-05`](../agent/cards/WI-B4-05-async-path-requests.md) | Asynchronous path requests | B4 |
| [`WI-B4-06`](../agent/cards/WI-B4-06-hierarchical-gate.md) | Hierarchical pathfinding gate | B4 |

One commit per card, one PR for the phase, left open for the operator. Cards land in order;
04..06 may interleave with the M4 preparation once 01..03 hold the partition budget.

Pending at authoring time: counter names as they land in the B1 record, measured medians
from the verification re-run, the squad probe numbers, and the hierarchical gate decision.
