# Task Card `WI-H2a-09`: Materialize supported abilities

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-09` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-08` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Map the reviewed ability cases to existing native behavior modules and commands.

## Context to read (and only this)

- H2a deep dive: approved ability capability matrix and reference cases.
- Card 08's materializer and the exact native ability/module/command data paths named by card 02.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md`: ability contract.

## Steps

1. Add reviewed ability mappings and resolved command/special-power dependencies.
2. Test each selected ability through its native execution path, including failure and cooldown cases.
3. Add a required unsupported-ability fixture that fails before publication.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitAbilityMaterializer.cs`.
- Reviewed native module-data construction seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitAbilityMaterializerTests.cs`.

## Acceptance criteria

- [ ] Selected ability values and references survive materialization and native activation.
- [ ] Unknown or unsupported required abilities never disappear from accepted content.
- [ ] Existing Generals handler coverage is retained; standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No new game mechanics. Card 02 must split this card if audited handler coverage is not a small bounded change; reconcile exact paths before Ready.

## On completion

Update Status and H2a-9, reconcile card 10, and append validation to `.exchange`.
