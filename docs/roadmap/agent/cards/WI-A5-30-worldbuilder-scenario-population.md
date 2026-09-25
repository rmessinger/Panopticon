# Task Card `WI-A5-30`: Bind spatial scenario populations in WorldBuilder

| Field | Value |
|---|---|
| Card ID | `WI-A5-30` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 visual authoring |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-02`, `WI-A5-14` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Bind the shared initial-population controller into WorldBuilder for spatial domains and save edits
to the declarative scenario artifact.

## Context to read (and only this)

- Spatial-authoring ownership in the A5 implementation plan.
- Initial-population controller from `WI-A5-14`.
- `src/Panopticon.Tools.WorldBuilder.Core/PlaceObjectTool.cs`, `SelectTool.cs`, and `EditorMap.cs`.
- Relevant WorldBuilder MAUI view models and views.

## Steps

1. Add a scenario-authoring mode or companion document as designed by `WI-A5-06`.
2. Route select, add, move, delete, and ownership changes through the shared controller.
3. Keep terrain/map edits in the existing WorldBuilder model.
4. Save and reload the linked scenario artifact.
5. Add Core tests and targeted UI validation.

## Files likely to change

- `src/Panopticon.Tools.WorldBuilder.Core/`
- Narrow WorldBuilder MAUI files
- WorldBuilder tests

## Acceptance criteria

- [ ] Spatial population edits use the shared controller.
- [ ] Terrain/map and scenario-population ownership stay separate.
- [ ] Save/reload preserves both linked artifacts.
- [ ] No duplicate ReplaySketch spatial editor is introduced.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WorldBuilder|FullyQualifiedName~ScenarioPopulation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No large-population rendering or terrain-feature implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
