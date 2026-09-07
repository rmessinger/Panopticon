# Task Card `WI-A5-15`: Make experiment matrices declarative

| Field | Value |
|---|---|
| Card ID | `WI-A5-15` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-2 experiment matrix |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-38`, `WI-A5-03` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Serialize matrix axes, structured constraints, and typed bindings to scenario/domain/seam fields,
then reject unbound paths before execution.

## Context to read (and only this)

- Matrix contract in the A5 implementation plan.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`.
- A4 experiment definition and binding types.
- Existing scenario-matrix tests.

## Steps

1. Add a versioned matrix loader and writer.
2. Replace delegate-only constraints with the designed serializable expression model.
3. Apply typed bindings to map, seed, counts, domain parameters, seam selection, and assertions.
4. Validate unknown paths, incompatible values, and duplicate bindings before expansion.
5. Add JSON round-trip and binding tests.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`
- New loader/binding services
- Existing matrix tests

## Acceptance criteria

- [ ] Matrix JSON round-trips without executable delegates.
- [ ] Every supported binding target has a focused test.
- [ ] Invalid paths fail before any cell runs.
- [ ] Existing cartesian expansion behavior is preserved.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ScenarioMatrix"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No run-bundle writing or matrix UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
