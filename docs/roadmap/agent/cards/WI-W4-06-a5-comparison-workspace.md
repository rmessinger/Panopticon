# Task Card `WI-W4-06`: Open W4 run sets in A5

| Field | Value |
|---|---|
| Card ID | `WI-W4-06` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W4-04`, `WI-A5-17`, `WI-A5-18` |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W4-04`, `WI-A5-17`, and `WI-A5-18` are Done.

Let A5 load, filter, compare, and scrub W4 matrix run sets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §3.
- `docs/roadmap/deep-dives/a5-visualization.md` §4-§5.

## Steps

1. Add W4 run-set loader support to the comparison workspace.
2. Filter by matrix axes and seed.
3. Open selected seed artifacts in the domain-state renderer.
4. Add UI or core tests for filtering and selection.

## Files likely to change

- A5 comparison workspace files
- Tests

## Acceptance criteria

- [ ] A W4 run set opens without rerunning simulations.
- [ ] Filtering by axis and seed selects the expected artifacts.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W4Comparison"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new charting beyond existing A5 comparison primitives.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
