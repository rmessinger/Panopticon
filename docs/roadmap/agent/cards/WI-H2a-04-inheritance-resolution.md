# Task Card `WI-H2a-04`: Resolve imported inheritance

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-04` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-03` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Resolve parent definitions and field/list overrides without mutating source records.

## Context to read (and only this)

- H2a deep dive: reviewed merge/default rules and diagnostics.
- Card 03's landed contracts; `ObjectDefinition.cs:18-56` for legacy default/child/reskin behavior.

## Steps

1. Implement the reviewed inheritance resolver, including named ability/module merge semantics.
2. Retain source attribution for inherited and overridden fields.
3. Test omitted/zero/empty overrides, multiple levels, missing parents, cycles, and source isolation.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitInheritanceResolver.cs`.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitInheritanceResolverTests.cs`.

## Acceptance criteria

- [ ] Reviewed scalar and list rules match Generals and EE2 adapter needs without assuming identical source semantics.
- [ ] Missing parents and cycles identify the complete offending source chain.
- [ ] Resolving a child does not mutate its parent or unrelated records.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No dependency publication or source parsing. Reconcile provisional filenames before Ready.

## On completion

Update Status and H2a-4, reconcile card 05, and append validation to `.exchange`.
