# Task Card `WI-H2a-03`: Add engine-owned interchange contracts

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-03` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-02` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Implement the reviewed definition, adapter-result, typed identity, provenance, and diagnostic contracts.

## Context to read (and only this)

- H2a deep dive: reviewed contracts and architecture.
- `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs`, `AssetImportContext.cs`: owned import-value patterns.
- `src/Panopticon.Simulation/Content/AssetHash.cs`: runtime identity constraints.

## Steps

1. Add the approved contracts under `Content/Importing/Units/`; use one type per file.
2. Cover body, movement, combat, abilities, economy/upgrades, and visual references with explicit omitted-value semantics.
3. Add defensive ownership and identity tests; no parsing or runtime registration.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/` contract files.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitImportContractTests.cs`.

## Acceptance criteria

- [ ] Source-qualified typed IDs retain case/normalization rules and provenance.
- [ ] External collection mutation cannot alter imported values; omitted and explicit zero remain distinct.
- [ ] Contracts contain neither source-parser nodes nor live runtime instances; all reviewed content families have a representation.
- [ ] Standard build/test/format validation and brace-newline check pass.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Paths are provisional until card 02 reconciles them. No new persistent format, opaque source payloads, or unsupported mechanics hidden as successful content.

## On completion

Set Status to Done or Blocked, update H2a-3, reconcile dependent cards, and append validation to `.exchange`.
