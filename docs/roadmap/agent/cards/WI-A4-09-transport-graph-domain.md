# Task Card `WI-A4-09`: Add the minimal transport/graph domain

| Field | Value |
|---|---|
| Card ID | `WI-A4-09` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-08` |
| Diagrams | A4 domain diagrams created by `WI-A4-06` |

## Goal

Add the intentionally minimal graph-domain adapter that proves the seam is topology-general without
creating a second entity runtime.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-simulation-domain-model.md`: graph validation contract.
- `src/OpenSage.Game/Simulation/`: domain interfaces and RTS adapter.
- `src/OpenSage.Game/Logic/Object/GameObject.cs` and module base types.
- Existing synthetic graph fixtures under `src/OpenSage.*Tests/`, if any.

## Steps

1. Implement graph nodes, edges, adjacency, and stable topology keys as designed.
2. Represent vehicles or flows with the shared `GameObject`/module runtime.
3. Implement edge-coupled interactions and the documented owner-authority rule.
4. Emit graph boundary and throughput metrics plus stable recording buckets.
5. Add a small terminating headless fixture that crosses one partition boundary.

## Files likely to change

- New files under `src/OpenSage.Game/Simulation/Transport/`
- Focused tests under `src/OpenSage.Game.Tests/Simulation/`

## Acceptance criteria

- [ ] The graph scenario uses `SimulationHost` and the shared entity runtime.
- [ ] It exercises adjacency, edge locality, owner authority, and boundary metrics.
- [ ] Stable graph keys appear in its T1 recording.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TransportGraphDomain"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No complete transport economy, routing product, editor, AI, or Track-P-grade match.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-10`.
