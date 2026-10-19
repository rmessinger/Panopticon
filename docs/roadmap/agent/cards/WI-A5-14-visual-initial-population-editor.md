# Task Card `WI-A5-14`: Add the initial-population edit controller

| Field | Value |
|---|---|
| Card ID | `WI-A5-14` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 visual authoring |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Edit scenario initial entities through a UI-independent controller and serialize those edits back
to the declarative artifact.

## Context to read (and only this)

- Visual-authoring contract in the A5 implementation plan.
- Versioned scenario population types from `WI-A5-03`.
- Domain visual projection from `WI-A5-12`.
- The shared project location selected by `WI-A5-06` so WorldBuilder and non-spatial editors can
  consume the same controller without a circular project reference.

## Steps

1. Add a Core controller for select, add, move, delete, and owner change.
2. Validate edits through the domain adapter.
3. Save and reload through the scenario serializer.
4. Add controller tests for spatial and graph fixtures.

## Files likely to change

- Shared scenario-authoring project selected by `WI-A5-06`
- Corresponding headless test project

## Acceptance criteria

- [x] All five operations are headlessly tested.
  - `InitialPopulationEditorTests.AllFiveOperations_EditPlacementsHeadlessly` covers select, add,
    move, delete, and owner change.
- [x] Save/reload preserves edits exactly.
  - Round-trip through the scenario serializer asserts definition, owner, quantity, health
    pair, and normalized position survive reload.
- [x] Invalid placement reports a diagnostic without mutation.
  - Empty definition, out-of-range owner, and negative owner edits return diagnostics naming the
    field path and leave the placement list unchanged.
- [x] Standard validation passes.
  - Controller lives in `Panopticon.Tools.ReplaySketch.Core` (the shared location selected by
    WI-A5-06's grounding: the scenario artifact, validator, and serializer all live there, and
    WorldBuilder.Core can reference it without a cycle). Validation routes through
    `ScenarioValidator` on a candidate clone; graph-domain scenarios edit through the same
    controller (domain-agnostic placements; non-spatial topology is WI-A5-33).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~InitialPopulationEditor"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No UI binding, terrain editing, or procedural bulk generation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A5-30` and `WI-A5-33`.
