# Task Card `WI-B7-05`: Add aggregation error hooks

| Field | Value |
|---|---|
| Card ID | `WI-B7-05` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B7-04`; A2 report cards Done |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Add the error measurement hooks that compare aggregated and full-fidelity runs.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md` §6-§7.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §7: all-tactical reference comparison.

## Steps

1. Define metric names for member counts, positions, conserved quantities, and transition dwell.
2. Add report input models that A2 can consume.
3. Add tests for report row generation from synthetic aggregation transitions.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/Aggregation/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Aggregation transitions produce A2-compatible comparison rows.
- [ ] Full-fidelity reference ids and mixed-run ids are recorded.
- [ ] Metrics include conserved quantities and dwell.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No benchmark optimization.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
