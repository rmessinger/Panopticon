# Task Card `WI-H2a-10`: Materialize production and upgrades

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-10` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-09` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Preserve existing production, cost, prerequisite, and upgrade semantics through shared materialization.

## Context to read (and only this)

- H2a deep dive: approved economy/upgrade cases and resource semantics.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs`: build fields and module data.
- Card 09's materializer and exact native production/upgrade data paths named by card 02.

## Steps

1. Materialize supported build costs/time, production links, prerequisites, and upgrade dependencies.
2. Preserve original resource values; apply only declared conversion policy supported by the runtime.
3. Test produce/apply-upgrade contracts and reject unsupported multi-resource or era mechanics without silent scalar conversion.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitProductionMaterializer.cs`.
- Reviewed native economy/upgrade construction seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitProductionMaterializerTests.cs`.

## Acceptance criteria

- [ ] Approved Generals production and upgrades execute with unchanged costs/time/prerequisites.
- [ ] Unsupported economy requirements are retained and diagnosed, not erased or implicitly balanced.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No Track D era mechanics or M3 points budget. The EE2 direct-spawn proof may declare economy unused but must not claim unsupported production works. Split oversized coverage at the design gate.

## On completion

Update Status and H2a-10, reconcile card 12, and append validation to `.exchange`.
