# Task Card `WI-H2a-08`: Materialize weapons and armor

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-08` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-07` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Construct native weapon and armor dependencies without source-specific runtime behavior.

## Context to read (and only this)

- H2a deep dive: audited combat mappings and construction seams.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponTemplate.cs`.
- `src/Panopticon.Simulation/Content/AssetStore.cs`: weapon/armor collections.
- Card 07's landed detached materializer and card 02's exact armor/projectile data paths.

## Steps

1. Materialize approved damage, range, reload, targeting, weapon modes, projectile references, and armor data.
2. Bind references through the resolved closure, retaining authored conversions rather than points/balance defaults.
3. Add exact-value and damage-application tests for direct/projectile attacks and supported targeting combinations.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitCombatMaterializer.cs`.
- Reviewed native combat definition construction seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitCombatMaterializerTests.cs`.

## Acceptance criteria

- [ ] Source range/reload/damage and armor semantics survive approved conversion.
- [ ] Invalid projectile/armor references and unsupported weapon modes block materialization.
- [ ] Native weapon execution consumes the constructed data; standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No M3 counter-class balancing or new weapon mechanics. Reconcile exact paths before Ready.

## On completion

Update Status and H2a-8, reconcile cards 09/11, and append validation to `.exchange`.
