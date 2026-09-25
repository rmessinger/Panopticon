# Task Card `WI-A4-33`: Extract the entity-storage seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-33` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable entity-storage seam |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-31`; B3 must define storage invariants and populate its diagrams |
| Diagrams | B3 architecture diagrams, pending the B3 design |

## Goal

Put `GameLogic` entity storage behind B3's collection contract with the current list-backed behavior
as the reference implementation.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-experiment-seams.md`: storage invariants.
- The completed B3 design and diagrams.
- `src/Panopticon.Simulation/Logic/GameLogic.cs`.
- `src/Panopticon.Simulation/Logic/IGameObjectCollection.cs`.
- Object creation, destruction, persistence, and iteration tests named by the B3 design.

## Steps

1. Add the B3-designed storage interface.
2. Move the current list/index behavior into the reference implementation.
3. Route lookup, stable iteration, add, remove, and persistence enumeration through the interface.
4. Preserve object-ID holes and mutation timing exactly.
5. Add source and behavior tests for interface-only use.

## Files likely to change

- `src/Panopticon.Simulation/Logic/GameLogic.cs`
- New storage files under `src/Panopticon.Simulation/Logic/`
- Focused logic and persistence tests

## Acceptance criteria

- [ ] Object lookup and iteration behavior are unchanged.
- [ ] Save ordering remains unchanged.
- [ ] `GameLogic` callers do not depend on the list implementation.
- [ ] One construction point selects storage.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GameLogic|FullyQualifiedName~EntityStorage"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No SoA migration, parallel update, or allocation optimization.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update the entity-storage row in `seams.md`.
- Append a result line to `.exchange`.
