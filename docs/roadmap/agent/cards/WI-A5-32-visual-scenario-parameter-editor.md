# Task Card `WI-A5-32`: Add visual scenario-parameter editing

| Field | Value |
|---|---|
| Card ID | `WI-A5-32` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 visual authoring |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03`, `WI-A5-10`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Edit domain parameters, seam selections, seed, stop conditions, probes, and recording options from
schema-driven controls without hard-coding RTS fields.

## Context to read (and only this)

- Parameter-editing contract in the A5 implementation plan.
- Versioned scenario model from `WI-A5-03`.
- Validator from `WI-A5-10`.
- Domain parameter/descriptor projection from `WI-A5-12`.
- Existing ReplaySketch Core/MAUI settings patterns.

## Steps

1. Add a Core form model from scenario and domain parameter descriptors.
2. Support the documented scalar, enum, list, and implementation-selection value types.
3. Validate before committing an edit.
4. Bind controls in the scenario-authoring surface.
5. Add round-trip, invalid-value, domain-switch, and screenshot tests.

## Files likely to change

- ReplaySketch Core authoring models
- Narrow ReplaySketch MAUI files
- ReplaySketch tests

## Acceptance criteria

- [ ] All versioned scenario parameter groups are editable.
- [ ] Domain switches replace domain-specific fields without stale values.
- [ ] Invalid edits do not mutate the scenario.
- [ ] Save/reload preserves edited values.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ScenarioParameterEditor"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-parameters.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No matrix-axis editing or topology drawing.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
