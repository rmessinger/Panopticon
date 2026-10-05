# Task Card `WI-H2a-06`: Validate capabilities and report failures

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-06` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-05` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Reject unsupported or malformed gameplay before runtime content publication.

## Context to read (and only this)

- H2a deep dive: capability matrix, numeric bounds, diagnostic stages and optional-content policy.
- Cards 03/05's landed contracts and dependency results.
- `src/Panopticon.Simulation/Content/Importing/ExternalAssetInspectionDiagnostic.cs`: existing diagnostic conventions.

## Steps

1. Validate the resolved closure against the reviewed runtime capabilities and numeric/enum constraints.
2. Distinguish unsupported gameplay from classified non-gameplay metadata and optional presentation.
3. Add tests for unsupported abilities/economies, NaN/infinity, invalid range/time/health, and warning-only metadata.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitImportValidator.cs`.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitImportValidatorTests.cs`.

## Acceptance criteria

- [ ] Blocking errors identify source, record, field, capability, and pipeline stage.
- [ ] Unknown gameplay never becomes a warning or a silent default; metadata warnings do not block valid content.
- [ ] No invalid closure is marked eligible for materialization/publication.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No new mechanic handlers or permissive catch-all fallback. Reconcile filenames before Ready.

## On completion

Update Status and H2a-6, reconcile materialization cards, and append validation to `.exchange`.
