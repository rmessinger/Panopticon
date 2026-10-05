# Task Card `WI-H2a-13`: Route Generals through the import seam

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-13` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2a-12` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Make existing Generals INI loading the reference source adapter for shared unit-content importing.

## Context to read (and only this)

- H2a deep dive: audited Generals call sites and compatibility contract.
- `src/Panopticon.Simulation/Content/ContentManager.cs`, `SubsystemLoader.cs`.
- `src/Panopticon.Simulation/Data/Ini/IniParser.cs`.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs`: defaults/child/reskin/module parsing.
- Card 12's publisher and the exact dependency parser paths named by card 02.

## Steps

1. Adapt INI unit/dependency parsing into the reviewed engine-owned contracts, keeping existing parser semantics.
2. Route normal unit loading through shared resolution/materialization/publication, preserving legacy names and load scopes.
3. Cover defaults, child/reskin records, map overrides, module tags, and native dependency loading with focused tests.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/Generals/` adapter files.
- Audited INI/loading call sites and native definition construction seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/GeneralsUnitAdapterTests.cs`.

## Acceptance criteria

- [ ] Normal Generals unit loading uses the shared seam, not a permanent independent fallback path.
- [ ] Native module coverage, legacy identifiers, defaults, overrides, and lazy-reference behavior remain intact.
- [ ] Synthetic inheritance/module/override tests pass; standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No rewrite of unrelated INI subsystems. If audit reveals a large migration, split this Draft card into staged always-shippable refactors before Ready. Never preserve unsupported native data by hiding parser objects in interchange.

## On completion

Update Status and H2a-13, reconcile card 14, and append validation to `.exchange`.
