# Task Card `WI-A5-15`: Make experiment matrices declarative

| Field | Value |
|---|---|
| Card ID | `WI-A5-15` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-2 experiment matrix |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A4-38`, `WI-A5-03` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Serialize matrix axes, structured constraints, and typed bindings to scenario/domain/seam fields,
then reject unbound paths before execution.

## Context to read (and only this)

- Matrix contract in the A5 implementation plan.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`.
- A4 experiment definition and binding types.
- Existing scenario-matrix tests.

## Steps

1. Add a versioned matrix loader and writer.
2. Replace delegate-only constraints with the designed serializable expression model.
3. Apply typed bindings to map, seed, counts, domain parameters, seam selection, and assertions.
4. Validate unknown paths, incompatible values, and duplicate bindings before expansion.
5. Add JSON round-trip and binding tests.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`
- New loader/binding services
- Existing matrix tests

## Acceptance criteria

- [x] Matrix JSON round-trips without executable delegates.
  - `ConstraintFilter` gained serializable `ConstraintTerm`s (==/!= ANDed); JSON carries terms only,
    the in-code delegate constructor remains for programmatic matrices and terms take precedence.
- [x] Every supported binding target has a focused test.
  - Map (with `{value}` format), Seed (uint-validated), Parameter, Seam (recorded as cell
    `SeamSelections`), PlacementCount (Quantity on matching placements) all covered in
    `Bindings_ApplyTypedTargetsToConcreteScenarios`.
- [x] Invalid paths fail before any cell runs.
  - `ScenarioMatrix.Validate()` reports unknown axes, missing keys, duplicate bindings, non-uint
    seed/count values, and malformed filter terms; `ScenarioMatrixDocument.Load` runs it and the
    scenario structural validation before returning.
- [x] Existing cartesian expansion behavior is preserved.
  - `Expand_TwoAxes_ProducesCartesianProduct` and the delegate filter test pass unchanged;
    `MatrixReport_ToJson_EmitsCorrectShape` stays green.
- [x] Standard validation passes.
  - Filter `ScenarioMatrix` 12/12 (4 prior + 4 binding + 4 loader).

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
