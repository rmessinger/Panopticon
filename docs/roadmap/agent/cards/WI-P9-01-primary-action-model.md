# Task Card `WI-P9-01`: Primary-action model

| Field | Value |
|---|---|
| Card ID | `WI-P9-01` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-1 Primary-action model, see [deep dive §2.4](../../deep-dives/p9-unit-validation-ground.md#24-primary-action-per-unit) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Add a `PrimaryAction` model that resolves every unit in `UnitCatalog` to exactly one primary action,
with `MoveTo` as the universal fallback, enforced by a test that fails when a catalog entry is
unresolved.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.3 and §2.4: roster source and the
  action table this card implements.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/UnitCatalog.cs`: the 32 `UnitOption` entries and the
  `{Faction}{Archetype}{Name}` definition-name convention.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/UnitArchetypeCatalog.cs`: P6b archetype-to-reference-unit
  mapping with `ObjectKinds` tags; use it to derive archetype where a unit is covered by it.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/BuildingCatalog.cs`: structure definitions, for the
  fixed cheap structure a builder unit constructs.
- `src/Panopticon.Simulation/Logic/Orders/OrderType.cs`: the `OrderType` values referenced by each action
  kind (`MoveTo`, `AttackObject`, `BuildObject`, `GatherDumpSupplies`, `SpecialPowerAtObject`,
  `SpecialPowerAtLocation`).

## Steps

1. Add `src/Panopticon.Tools.ReplaySketch.Core/Model/PrimaryActionKind.cs` with values `Move`,
   `Attack`, `Build`, `Gather`, `SpecialPower`.
2. Add `src/Panopticon.Tools.ReplaySketch.Core/Model/PrimaryAction.cs` as a record carrying `Kind`,
   the `OrderType` it dispatches, an optional `TargetDefinitionName` (the structure a builder builds),
   and `FrameBudget`.
3. Add a static resolver (for example `PrimaryActionCatalog`) mapping every `UnitCatalog` definition
   name to a `PrimaryAction`. Dozers and workers resolve to `Build`, supply units to `Gather`,
   support units with an implemented ability to `SpecialPower`, remaining combat units to `Attack`,
   and anything not otherwise classified to `Move`.
4. Give each action kind a default `FrameBudget`; a build or gather budget must be larger than an
   attack budget. Values are a first estimate and will be tuned in `WI-P9-07`.
5. Add a `[Fact]` test asserting the resolver returns a non-null `PrimaryAction` for every entry in
   `UnitCatalog`, and that no entry resolves to a `Build` action without a `TargetDefinitionName`.
6. Follow the `Panopticon.Tools.ReplaySketch.Core` naming convention: private static fields are
   PascalCase in this project (IDE1006-enforced), not `s_`-prefixed.
7. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/PrimaryActionKind.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/PrimaryAction.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/PrimaryActionCatalog.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/PrimaryActionCatalogTests.cs` (new)

## Acceptance criteria

- [ ] Every `UnitCatalog` entry resolves to exactly one `PrimaryAction`; the test fails if a new
      catalog entry is added without a mapping.
- [ ] `Move` is reachable as an explicit fallback, not only as a default-initialized enum value.
- [ ] Every `Build` action carries a `TargetDefinitionName` drawn from `BuildingCatalog`.
- [ ] Each `PrimaryAction` carries a positive `FrameBudget`.
- [ ] No engine or `IGame` dependency is introduced into these types.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PrimaryActionCatalogTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build the layout, spawn host, timer, or baseline here; those are `WI-P9-02` through
  `WI-P9-07`.
- Do not add units to `UnitCatalog` in this card.
- Do not implement missing special-power handlers; a unit whose ability is unimplemented still gets a
  `SpecialPower` mapping and is reported as a gap at run time.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-1 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
