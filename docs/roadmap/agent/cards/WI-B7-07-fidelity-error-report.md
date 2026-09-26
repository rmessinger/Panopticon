# Task Card `WI-B7-07`: Report fidelity transitions and error

| Field | Value |
|---|---|
| Card ID | `WI-B7-07` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B7-05`; `WI-B7-06`; A2 report cards Done |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Create a fidelity report that combines level occupancy, transition dwell, and A2 error tables.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md` §6-§7.
- `docs/roadmap/deep-dives/a2-reproducibility.md`: A2 report conventions.

## Steps

1. Add report models for level occupancy, transition counts, dwell, and error metrics.
2. Generate report rows from recorded registry and aggregation events.
3. Add tests over a synthetic full-vs-mixed run pair.
4. Document how W2 can plug its 64-seed report into this contract.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/Reports/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Report includes level occupancy by frame range.
- [ ] Report includes transition reasons and dwell.
- [ ] Report includes A2 error metrics against a full-fidelity reference.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new fidelity-reduction technique.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
