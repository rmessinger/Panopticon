# Task Card `WI-H2a-07`: Materialize body and movement

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-07` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-06` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Construct native body, geometry, and movement definitions from validated engine-owned data.

## Context to read (and only this)

- H2a deep dive: audited construction call sites and body/movement mappings.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs`.
- `src/Panopticon.Simulation/Logic/Object/Body/BodyModule.cs` and exact native data types named by card 02.
- Card 06's validated definition result.

## Steps

1. Add a bounded native construction seam where parser-only setters currently prevent construction.
2. Materialize approved body/geometry/locomotor and movement module data into detached runtime definitions.
3. Test current/max health, geometry, speed, terrain capability, conversion values, and invalid-number rejection.

## Files likely to change

- Proposed `Content/Importing/Units/UnitRuntimeMaterializer.cs` and body/movement mapper files in `src/Panopticon.Simulation/`.
- Native definition construction seams identified by card 02.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitBodyMovementMaterializerTests.cs`.

## Acceptance criteria

- [ ] Health and movement values come from the validated definition and explicit conversions.
- [ ] Existing native modules remain the execution path; no INI text round-trip is used.
- [ ] Detached construction has no `AssetStore` side effects; standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No publication, new physics/locomotion mechanics, or balance changes. Exact native files must be named before Ready.

## On completion

Update Status and H2a-7, reconcile card 08, and append validation to `.exchange`.
