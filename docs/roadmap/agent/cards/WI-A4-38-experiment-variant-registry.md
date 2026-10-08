# Task Card `WI-A4-38`: Add experiment definitions and variant registry

| Field | Value |
|---|---|
| Card ID | `WI-A4-38` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): experiment harness |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-37` |
| Diagrams | A4 harness diagrams created by `WI-A4-37` |

## Goal

Implement the typed experiment definition and sole identifier-to-implementation registry without
running benchmarks yet.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-experiment-harness.md`.
- A4 seam identifiers and factories already landed.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`: matrix conventions.
- B1 benchmark identifiers.

## Steps

1. Add the designed configuration and binding types.
2. Add the central registry and delegate to seam-owned factories.
3. Validate unknown, duplicate, incompatible, and missing bindings before execution.
4. Add JSON round-trip and registry-only construction tests.

## Files likely to change

- New experiment project or directory selected by `WI-A4-37`
- New focused tests

## Acceptance criteria

- [ ] Experiment JSON round-trips.
- [ ] Every implementation is constructed through the registry.
- [ ] Invalid bindings fail before a benchmark starts.
- [ ] Registry metadata includes stable implementation identity and version.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExperimentDefinition|FullyQualifiedName~VariantRegistry"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No benchmark execution or A5 UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-39`.
