# Task Card `WI-B7-01`: Add the fidelity model and registry

| Field | Value |
|---|---|
| Card ID | `WI-B7-01` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B7 §9](../../deep-dives/b7-variable-fidelity.md#9-architecture-diagrams) |

## Goal

Add `SimulationFidelityLevel`, `FidelityState`, and `FidelityRegistry` in `Panopticon.Simulation`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md`: owning B7 design.
- `src/Panopticon.Simulation/Logic/GameLogic.cs`: live object enumeration and creation.
- `src/Panopticon.Simulation/IGame.cs:149-153`: object destruction event.

## Steps

1. Add the enum and immutable state record.
2. Add a registry keyed by `ObjectId` with current level, requested level, dwell counters, reason, and last transition frame.
3. Subscribe or expose removal for destroyed object ids.
4. Add unit tests for set, query, hysteresis state, and removal.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Registry stores per-object current and requested levels.
- [ ] Registry tracks hysteresis state.
- [ ] Destroyed object ids are removed or ignored deterministically.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No temporal LOD, simplified physics, aggregation, or visualization.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
