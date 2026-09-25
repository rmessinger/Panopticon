# Task Card `WI-P6b-08`: P6b scenario matrix

| Field | Value |
|---|---|
| Card ID | `WI-P6b-08` |
| Roadmap phase | [Track P · P6b](../../track-p-playable-baseline.md): P6b scenario matrix, see [deep dive §4](../../deep-dives/p6b-unit-contract-validation.md#4-scenario-matrix) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P6b-01`, `WI-P6b-02`, `WI-P6b-03`, `WI-P6b-04`, `WI-P6b-05`, `WI-P6b-06`, `WI-P6b-07` |
| Diagrams | [P6b Architecture Diagrams](../../deep-dives/p6b-unit-contract-validation.md#6-architecture-diagrams) |

## Goal

Create the P6b `ScenarioMatrix` test that sweeps `archetype × faction`, prunes cells without a reference unit via `ConstraintFilter`, and runs the full unit contract for each valid cell.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:31-45`: archetype definitions and USA reference units.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:47-67`: full six-point contract each cell must assert.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:92-119`: matrix axes, `ConstraintFilter`, per-cell assertions, and pass/fail purpose.
- `docs/roadmap/implementation-checklist.md:330-335`: checklist AC for the P6b matrix.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs:9-152`: `SweepAxis`, `ConstraintFilter`, `AssertionTemplate`, and expansion.
- `src/Panopticon.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs:9-23`: matrix execution via `HarnessRunner`.
- `src/Panopticon.Tools.ReplaySketch.Core/Assertions/MatrixReport.cs:7-56`: aggregate report shape and cell keys.
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs:372-448`: current matrix test style.
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bInfantryContractTests.cs`, `P6bVehicleContractTests.cs`, `P6bAircraftContractTests.cs`, `P6bBuilderContractTests.cs`, `P6bSupportContractTests.cs`, `P6bSuperweaponContractTests.cs`: reusable per-archetype contract scenario builders/assertions.

## Steps

1. Create `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bScenarioMatrixTests.cs` as a real xUnit `[Fact]` test file that runs in CI under `dotnet test src --filter Category!=Interactive`. A `[GameFact]` may be supplemental only; it cannot be the acceptance gate.
2. Build a `ScenarioMatrix` with axes:
   - `archetype`: `infantry`, `vehicle`, `aircraft`, `builder`, `support`, `superweapon`
   - `faction`: `USA`, `GLA`, `China`
3. Add a reference-unit resolver used by the matrix. Use the deep-dive reference units where specified; for other factions, resolve definitions by `KindOf`/command-set capability and keep the mapping explicit in one helper. If no valid reference unit exists for a cell, the resolver returns `null`.
4. Add a `ConstraintFilter` whose expression documents `hasReferenceUnit(archetype, faction)` and whose predicate prunes cells where the resolver returns `null`.
5. For every remaining cell, run the same full six-point contract as the archetype tests: produced/built, move-or-static, archetype action, attack-or-decline, every weapon mode and command-set `SpecialPowerType`, and death/sell cleanup.
6. Assert the aggregate `MatrixReport` includes per-cell keys for `archetype` and `faction`; failures must name the exact cell and assertion. Unimplemented handlers must appear as explicit `P6b gap:` failures, never skips or no-op passes.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify with `dotnet format src --verify-no-changes`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bScenarioMatrixTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6b*ContractTests.cs` (only to expose reusable builders/assertions)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs` (only if existing `ConstraintFilter` plumbing is insufficient)

## Acceptance criteria

- [ ] The matrix axes are exactly `archetype × faction` with archetypes `{infantry, vehicle, aircraft, builder, support, superweapon}` and factions `{USA, GLA, China}`.
- [ ] A `ConstraintFilter` prunes only cells lacking a valid reference unit; the test asserts both the raw 18-cell count and the final valid-cell count.
- [ ] Every valid cell runs the full six-point contract: produce/build, move-or-static, archetype-specific action, attack-or-decline, all weapon modes/special abilities, and die/sell cleanup.
- [ ] Death cleanup in every cell verifies removal from `GameLogic`, owner tracking, and selection state.
- [ ] Unimplemented special-power handlers are reported as explicit `P6b gap:` failures with the cell key; none silently passes.
- [ ] The matrix test is real `[Fact]` CI coverage; it is not skipped and does not rely solely on `[GameFact]` or `Category=Interactive`.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6bScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not invent unsupported reference units just to fill every cell; prune missing cells with `ConstraintFilter`.
- Do not reduce coverage to USA-only or to one archetype.
- Do not mark this done with skipped tests, placeholder assertions, or Interactive-only coverage.
- Do not change production behavior except for tiny reusable seams required by the already-passing archetype tests.

## On completion

- Set Status above to `Done` (or `Blocked` + exact remaining matrix/gap reason).
- Tick the "P6b scenario matrix" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` with raw/valid cell counts and any failing cell keys.
- Confirm `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bScenarioMatrixTests.cs` was created and the matrix test runs in CI.

## Result

<!-- append after completing -->
