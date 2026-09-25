# Task Card `WI-A4-34`: Extract the partition-scheme seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-34` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable partition seam |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-10`, `WI-A4-31`; B6-1 contract must be implementation-ready |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Introduce B6's partition-scheme contract with spatial-grid and graph-topology reference
implementations selectable without changing host callers.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-experiment-seams.md`: partition boundary.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md`: B6-1 contract.
- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs` and `DataStructures/Quadtree.cs`.
- RTS and transport domain adapters from A4-1.

## Steps

1. Add the B6-designed interface and partition assignment value types.
2. Implement the existing spatial grid as the RTS reference.
3. Implement graph assignment using A4-1 topology keys.
4. Inject one scheme through domain/host configuration.
5. Test assignment, neighbours, halo candidates, and stable keys.

## Files likely to change

- New partitioning files under `src/Panopticon.Simulation/Simulation/`
- Narrow domain-adapter integration
- Focused partition tests

## Acceptance criteria

- [ ] Spatial and graph schemes satisfy the same contract.
- [ ] Existing collision and shroud behavior is not moved into the seam.
- [ ] Selection is configuration-driven.
- [ ] Stable keys match A4 recording buckets.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PartitionScheme"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No multi-process shards, load balancing, migration, or graph-cut optimization.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update the partition row in `seams.md`.
- Append a result line to `.exchange`.
