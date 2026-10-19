# Task Card `WI-A5-10`: Validate declarative scenarios before execution

| Field | Value |
|---|---|
| Card ID | `WI-A5-10` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 scenario authoring |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03`, `WI-A5-06` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Validate the complete versioned scenario before host construction and report every error with its
artifact field path.

## Context to read (and only this)

- Scenario schema from the A5 implementation plan.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ReplayScenario.cs`, `PlayerSlotConfig.cs`.
- `Services/TerrainValidator.cs`, `ScenarioProfileLoader.cs`, `MapMetadataService.cs`.
- Scenario tests added by `WI-A5-03`.

## Steps

1. Add the validator and structured diagnostic type.
2. Validate schema version, seed, map, players, definitions, placements, parameters, stop
   conditions, probes, and recording options.
3. Delegate domain-specific topology/probe checks when A4-1 is present.
4. Ensure invalid scenarios construct no host.
5. Add one valid fixture and a focused failure fixture per field group.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- `test/Panopticon.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [x] Every invalid fixture reports a JSON-style field path.
  - `ScenarioValidatorTests.FieldGroupFailures_ReportStablePathsInOrder` asserts exact paths for
    ten diagnostics across header/players/placements/parameters/stop conditions/metric series.
- [x] All independent errors are returned in stable order.
  - Same test asserts the full ordered path list.
- [x] Valid scenarios produce no diagnostics.
  - `ValidScenario_HasNoDiagnostics`; `ProceduralScenarioTests.ExpandedScenario_PassesStructuralValidation`.
- [x] Execution cannot start after validation failure.
  - `HarnessRunner.RunAndAssert` runs `ScenarioValidator.Validate` after map load and before
    `Platform.Start`/host construction; invalid scenarios throw before any Game is created.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ScenarioValidator"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No preview rendering or automatic correction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
