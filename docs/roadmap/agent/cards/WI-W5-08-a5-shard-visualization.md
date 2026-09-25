# Task Card `WI-W5-08`: Visualize W5 shards in A5

| Field | Value |
|---|---|
| Card ID | `WI-W5-08` |
| Roadmap phase | [Track W · W5](../../track-w-western-front.md): distributed regional simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W5-03`, `WI-A5-18` |
| Diagrams | [W5 §7](../../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W5-03` and `WI-A5-18` are Done.

Show shard strips, halo age, handoffs, and boundary messages in A5.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §5.
- `docs/roadmap/deep-dives/a5-visualization.md` §4.

## Steps

1. Add shard ownership and halo overlays to the W domain visualization.
2. Add a boundary-message timeline lane.
3. Show handoff source, target, and acknowledgement state.
4. Add core rendering tests for a recorded fixture.

## Files likely to change

- A5 visualization files
- Tests

## Acceptance criteria

- [ ] A5 displays strip ownership and halo age for selected frames.
- [ ] Boundary messages and handoffs are inspectable without rerun.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontShardVisualization"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No networking UI.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
