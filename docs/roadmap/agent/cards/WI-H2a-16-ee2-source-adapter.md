# Task Card `WI-H2a-16`: Adapt EE2 to shared unit content

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-16` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-12`, `WI-H2a-15` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Emit shared interchange for the selected real EE2 ground-combat unit and its dependency closure.

## Context to read (and only this)

- H2a deep dive: selected source unit, conversions, source-to-capability mapping, and visual states.
- Card 15's reader and card 12's shared publisher.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifModelImporter.cs`, `NifAnimationImporter.cs`.
- Selected real DDF files and combat/visual dependency records named by card 02, read from the operator install only.

## Steps

1. Adapt UnitType properties/abilities and inherited dependencies into shared engine-owned records.
2. Bind UnitModel states and Track C model/animation keys; declare explicit health/combat/movement units and conversions.
3. Test inherited health, attack damage/range/reload, armor mapping, movement, and states against authored source values.
4. Route resolution, validation, materialization, and publication entirely through the shared layer.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/Ee2/` adapter files.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2UnitAdapterTests.cs`.
- H2a evidence for source/runtime values and declared limitations.

## Acceptance criteria

- [ ] EE2 emits the same interchange consumed by the Generals reference adapter's shared pipeline.
- [ ] No direct EE2-specific `ObjectDefinition` construction or borrowed Generals stats are used.
- [ ] Conversion and explicit capability mapping are inspectable; M3 balancing is not baked into the adapter.
- [ ] Required missing/unsupported dependencies fail; standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Card 02 supplies the exact explicit real-install adapter test command before Ready.

## Out of scope / guardrails

No full M2 roster, aircraft rearm, or new mechanics. Direct-spawn eligibility must be explicit and must not imply unimplemented EE2 production support.

## On completion

Update Status and H2a-16, reconcile card 17, and append source-value evidence to `.exchange`.
