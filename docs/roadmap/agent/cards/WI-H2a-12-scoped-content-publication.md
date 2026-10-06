# Task Card `WI-H2a-12`: Publish validated content scopes

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-12` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2a-10`, `WI-H2a-11` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Publish complete validated runtime content without partial registration or built-in collisions.

## Context to read (and only this)

- H2a deep dive: registration, override, rollback, and unload contracts.
- `src/Panopticon.Simulation/Content/AssetStore.cs`, `ScopedAssetCollection.cs`, `IScopedAssetStorage.cs`, `LazyAssetReference.cs`.
- Cards 07..11's detached materializers.

## Steps

1. Stage the required definition closure, validate identities/hashes, and construct detached runtime dependencies.
2. Register through the reviewed publication boundary only after staging succeeds; roll back exactly the owned import if publication fails.
3. Test injected mid-construction/mid-publication failures, explicit overrides, collision rejection, and unload with cached references.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitContentPublisher.cs`.
- Reviewed scoped storage registration seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitContentPublicationTests.cs`.

## Acceptance criteria

- [ ] Failures leave no imported object/weapon/armor/locomotor entries and do not alter built-in entries.
- [ ] Same local names from distinct sources coexist; legacy names remain compatible.
- [ ] Unload follows the reviewed live-object/reference lifetime policy without disposing built-in dependencies.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not pop unrelated global scopes or retrofit arbitrary concurrent hot reload. Reconcile exact native edits before Ready.

## On completion

Update Status and H2a-12, reconcile cards 13/16, and append validation to `.exchange`.
