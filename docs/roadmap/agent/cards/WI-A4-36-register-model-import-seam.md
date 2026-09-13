# Task Card `WI-A4-36`: Register the model-import seam

| Field | Value |
|---|---|
| Card ID | `WI-A4-36` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): stable model-import seam |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-31`, `WI-A4-38`; C1 must land importer contracts, W3D reference adapters, and diagrams |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Expose C1's model and animation importer identifiers through the common A4 implementation-selection
registry without duplicating importer contracts.

## Context to read (and only this)

- Completed C1 deep dive and importer implementation.
- `docs/roadmap/implementation/track-a-a4-experiment-seams.md`.
- `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs`.
- C1 importer factory and W3D adapter tests.
- A4 implementation registry from `WI-A4-38`.

## Steps

1. Add model and animation importer bindings to the A4 registry.
2. Delegate construction to the C1-owned factory.
3. Record selected importer identities in experiment configuration/results.
4. Add registry and factory-delegation tests.

## Files likely to change

- A4 experiment registry files
- Narrow C1 factory integration
- Focused tests

## Acceptance criteria

- [ ] A4 selects importers only through C1 contracts.
- [ ] W3D remains the reference default.
- [ ] No duplicate importer interface or format dispatch exists.
- [ ] Selected identities appear in experiment metadata.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelImporter|FullyQualifiedName~ExperimentRegistry"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No glTF implementation or asset-pipeline refactor outside C1's landed API.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update the model-import row in `seams.md`.
- Append a result line to `.exchange`.
