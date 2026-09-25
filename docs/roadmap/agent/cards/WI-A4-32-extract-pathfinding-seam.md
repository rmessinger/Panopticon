# Task Card `WI-A4-32`: Extract the pathfinding/routing seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-32` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable pathfinding seam |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-31`; B4 must define the routing contract and populate its diagrams |
| Diagrams | B4 architecture diagrams, pending the B4 design |

## Goal

Place the current A* navigation implementation behind B4's routing contract and select it through
one factory/configuration path.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-experiment-seams.md`: pathfinding extraction.
- The completed B4 design and diagrams.
- `src/Panopticon.Simulation/Navigation/Navigation.cs`.
- Pathfinding callers named by the implementation plan.
- Existing navigation and path-query tests.

## Steps

1. Add the B4-designed interface and selection identifier.
2. Adapt current `Navigation` as the reference A* implementation.
3. Inject it through the one documented engine construction path.
4. Move debug-only rendering outside the routing contract if required by A3.
5. Add factory-only construction and reference-equivalence tests.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/`
- Narrow engine construction and caller files named by the B4 design
- Navigation tests

## Acceptance criteria

- [ ] Callers depend on the routing interface.
- [ ] Current A* behavior and path results remain unchanged.
- [ ] One factory owns identifier-to-implementation mapping.
- [ ] The contract supports the B4 and B6 implementations without RTS-only casts.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Navigation|FullyQualifiedName~Pathfinding"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No hierarchical, flow-field, asynchronous, or cross-shard routing implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update the pathfinding row in `seams.md`.
- Append a result line to `.exchange`.
