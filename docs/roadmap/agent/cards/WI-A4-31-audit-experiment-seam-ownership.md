# Task Card `WI-A4-31`: Audit remaining experiment-seam ownership

| Field | Value |
|---|---|
| Card ID | `WI-A4-31` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable experiment seams |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [Seam landscape](../../seams.md#landscape-overview) |

## Goal

Ground the extraction points and ownership boundaries for pathfinding, partitioning, fidelity,
entity storage, and model import without preempting their B/C-track implementation designs.

## Context to read (and only this)

- `docs/roadmap/seams.md`: canonical experiment seam table.
- `docs/roadmap/track-b-scale.md`: B3, B4, B6, and B7 ownership.
- `docs/roadmap/track-c-external-assets.md`: C1 ownership.
- `src/OpenSage.Game/Navigation/`, `Logic/GameLogic.cs`, `Logic/PartitionCellManager.cs`.
- `src/OpenSage.Game/Content/Loaders/` and `Content/OnDemandAssetLoadStrategy.cs`.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md` and `b7-variable-fidelity.md`.

## Steps

1. Identify current construction sites and concrete callers for each seam.
2. Record the reference implementation and the owning phase for optimized alternatives.
3. Define the minimum A4 extraction boundary needed for runtime selection.
4. List every unresolved contract that must be settled by the owning B/C design.
5. Create `docs/roadmap/implementation/track-a-a4-experiment-seams.md` and index it.

## Files likely to change

- `docs/roadmap/implementation/track-a-a4-experiment-seams.md` (new)
- `docs/roadmap/implementation/README.md`
- `docs/roadmap/seams.md`

## Acceptance criteria

- [ ] All five seams have cited construction and caller sites.
- [ ] A4 extraction work is separated from later implementations.
- [ ] Each unresolved contract names the phase that must resolve it.
- [ ] No source file changes.

## Validation commands

```sh
git status --short -- 'src/*'
```

## Out of scope / guardrails

Do not invent B3/B4/B6/B7/C1 algorithms or duplicate the map/object-format seam owned by H2.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
