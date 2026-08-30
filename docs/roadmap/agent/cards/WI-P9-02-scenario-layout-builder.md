# Task Card `WI-P9-02`: Scenario and fixed layout builder

| Field | Value |
|---|---|
| Card ID | `WI-P9-02` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-2 Scenario and layout model, see [deep dive §2.1](../../deep-dives/p9-unit-validation-ground.md#21-shape-of-the-scenario) and [§2.9](../../deep-dives/p9-unit-validation-ground.md#29-controlled-scenario-inputs) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P9-01` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Add `ValidationGroundScenario` and `ValidationGroundBuilder` so a scenario produces a complete
`SpawnPlan` (unit positions, target positions, target assignment, order sequence) with no engine
dependency.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.1, §2.4 (target assignment by index),
  §2.9, and §3 (component placement table).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PrimaryActionCatalog.cs`: the resolver from
  `WI-P9-01`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/UnitCatalog.cs`: roster and faction grouping.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/BuildingCatalog.cs`: destructible-target and
  supply-source definition names.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PositionSpec.cs`: the existing normalized/landmark
  position model, for how positions are expressed elsewhere in this project.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs`: the existing scenario record, as a
  shape reference only. P9 is not a replay and must not export a `.rep`.

## Steps

1. Add `src/OpenSage.Tools.ReplaySketch.Core/Model/ValidationGroundScenario.cs`: map path, roster,
   layout parameters (row spacing, faction row offsets, target-cluster centre and radius), target and
   supply-source definition names, `SettleFrames`, and `Seed`.
2. Add `src/OpenSage.Tools.ReplaySketch.Core/Model/SpawnPlan.cs` with `UnitPlacement` (definition
   name, player index, position, resolved `PrimaryAction`, target index) and `TargetPlacement`.
3. Add `src/OpenSage.Tools.ReplaySketch.Core/Services/ValidationGroundBuilder.cs` with a
   `Build(ValidationGroundScenario)` method laying units out in one row per faction facing the target
   cluster, assigning targets by index (`unitIndex % targetCount`), and resolving each unit's action
   through `PrimaryActionCatalog`.
4. Keep the builder free of `IGame`, `Scene3D`, and `GameLogic` references. Positions are planar; the
   host samples terrain height in `WI-P9-03`.
5. If randomness is used at all, take it from a seeded generator constructed from `Seed`, never from
   a shared or ambient `Random`.
6. Add `[Fact]` tests asserting two `Build` calls with the same scenario produce equal plans, that
   every roster unit appears exactly once, that every unit has a valid target index, and that units
   from different factions do not share a position.
7. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ValidationGroundScenario.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/SpawnPlan.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ValidationGroundBuilder.cs` (new)
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/ValidationGroundBuilderTests.cs` (new)

## Acceptance criteria

- [ ] `Build` returns equal plans across repeated invocations for the same scenario and seed.
- [ ] Every `UnitCatalog` roster entry appears exactly once in the plan with a resolved
      `PrimaryAction`.
- [ ] Target assignment is by index and is stable when target ordering in the catalog is unchanged.
- [ ] No two placements share a position.
- [ ] `ValidationGroundBuilder` compiles with no reference to `IGame`, `Scene3D`, or `GameLogic`.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ValidationGroundBuilderTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not spawn anything or touch a live game here; that is `WI-P9-03`.
- Do not export a `.rep` or reuse `ReplayScenario`. P9 is a direct-spawn sandbox, not a replay.
- Do not sample terrain height in the builder; it has no terrain access by design.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-2 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
