# Task Card `WI-B7-06`: Bridge fidelity states into A5 visualization

| Field | Value |
|---|---|
| Card ID | `WI-B7-06` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B7-03`; `WI-A5-19` Done |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Feed recorded fidelity categories to the A5 result visualizer without duplicating B7 policy types.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md` §6 and §9.
- `docs/roadmap/agent/cards/WI-A5-19-fidelity-overlay.md`: blocked A5 overlay card.

## Steps

1. Map registry categories to A5 categorical visual attributes.
2. Provide legend metadata from recorded states.
3. Add a synthetic recorded run with Full, Tactical, Operational, and Aggregate categories.
4. Add visualization tests required by A5.

## Files likely to change

- A5 domain visual projection files
- Fidelity projection test fixtures

## Acceptance criteria

- [ ] A5 receives stable category values and legend labels.
- [ ] Missing fidelity data disables the overlay without failure.
- [ ] A5 does not depend on B7 policy implementations.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not implement A5 overlay before `WI-A5-19` is unblocked.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
