# Task Card `WI-A5-23`: Add procedural scenario generators

| Field | Value |
|---|---|
| Card ID | `WI-A5-23` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 authoring at scale |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Generate large initial populations and command timelines declaratively from bounded generator
specifications and a seed.

## Context to read (and only this)

- Generator contract in the A5 implementation plan.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ReplayScenario.cs`.
- `Model/SpawnPlan.cs`, `CommandSpec.cs`, and P0 procedural authoring tests.
- A5 scenario serialization from `WI-A5-03`.

## Steps

1. Add formation, grid, and seeded distribution population specifications.
2. Add timeline repetition/sequence generators that emit existing `CommandSpec`.
3. Stream expansion into the scenario writer where the design requires it.
4. Validate generator-specific counts, bounds, and overlap policy, then delegate expanded
   scenario validation to `WI-A5-10` when available.
5. Add same-seed and 10k-entity/10k-command tests.

## Files likely to change

- Scenario/generator models and services in ReplaySketch Core
- ReplaySketch tests

## Acceptance criteria

- [ ] Same configuration and seed produce identical artifacts.
- [ ] 10k entities and 10k commands require no per-item hand entry.
- [ ] Generated commands use the P0 model.
- [ ] Invalid generator bounds fail before partial output.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ProceduralScenario"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No viewport performance work or benchmark threshold.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
