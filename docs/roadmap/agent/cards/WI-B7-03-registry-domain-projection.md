# Task Card `WI-B7-03`: Project fidelity into domain state and metrics

| Field | Value |
|---|---|
| Card ID | `WI-B7-03` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B7-01`; `WI-B7-02`; A4 domain projection cards Done |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Expose registry fidelity categories to domain metrics and recorded state.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md`: owning B7 design.
- `src/Panopticon.Simulation/Logic/GameLogic.cs`: live object enumeration and creation.
- `src/Panopticon.Simulation/IGame.cs:149-153`: object destruction event.

## Steps

1. Add a small projection helper from registry state to domain metric/category values.
2. Add per-frame counts by fidelity level.
3. Add tests that recorded state reconstructs per-object categories and counts.
4. Keep presentation LOD separate from simulation fidelity.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Per-object fidelity category is recordable.
- [ ] Per-frame level counts are recordable.
- [ ] Presentation LOD types are not reused as simulation fidelity state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No A5 UI work.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
