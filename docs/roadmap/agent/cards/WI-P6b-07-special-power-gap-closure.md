# Task Card: `WI-P6b-07` — Special-power gap closure

| Field | Value |
|---|---|
| Card ID | `WI-P6b-07` |
| Roadmap phase | [Track P · P6b](../../track-p-playable-baseline.md) — P6b gap closure, see [deep dive §2.3](../../deep-dives/unit-contract-validation.md#23-surfacing-unimplemented-handlers-as-gaps-not-silent-failures) and [§4 Sub-phases](../../deep-dives/unit-contract-validation.md#3-sub-phases) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-P6b-01`, `WI-P6b-02`, `WI-P6b-03`, `WI-P6b-04`, `WI-P6b-05`, `WI-P6b-06` |
| Diagrams | [P6b Architecture Diagrams](../../deep-dives/unit-contract-validation.md#6-architecture-diagrams) |

## Goal

Close or explicitly document every unimplemented `SpecialPower*` handler surfaced by the six P6b archetype scenarios so none can silently pass without an observable effect.

## Context to read (and only this)

- `docs/roadmap/deep-dives/unit-contract-validation.md:72-77` — gap handling rule.
- `docs/roadmap/deep-dives/unit-contract-validation.md:123-131` — sequencing and in-phase fixes.
- `docs/roadmap/implementation-checklist.md:326-329` — checklist AC for P6b gap closure.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:388-426,556-560` — special-power dispatch and default unimplemented-order logging.
- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerApplicator.cs:13-37` — no-target special powers and current throwing handlers.
- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerAtLocationApplicator.cs:26-89` — location special powers and current throwing handlers.
- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerAtObjectApplicator.cs:22-50` — object-target special powers and current throwing handlers.
- `src/OpenSage.Game/Logic/Object/SpecialPower.cs:116-180,823-843` — `SpecialPowerType` values and order flags.
- `src/OpenSage.Game/Logic/Object/SpecialPower/SpecialPower.cs:143-149` — base activation/reload behavior.
- `src/OpenSage.Game/Logic/Object/SpecialPower/OCLSpecialPower.cs:11-66,163-215` and `src/OpenSage.Game/Logic/Object/SpecialPower/CashHackSpecialPower.cs:9-30,60-81` — examples of concrete handlers to mirror.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6bInfantryContractTests.cs`, `P6bVehicleContractTests.cs`, `P6bAircraftContractTests.cs`, `P6bBuilderContractTests.cs`, `P6bSupportContractTests.cs`, `P6bSuperweaponContractTests.cs` — gap messages emitted by P6b-01 through P6b-06.

## Steps

1. Run each P6b archetype contract test and collect every failure message beginning `P6b gap:`. Do not use `git`; use test output and `.exchange` only.
2. For each gap, classify it as: `fix-now` (small handler/wiring issue), `already-handled-but-assertion-wrong`, or `out-of-scope-follow-up` (deep/cross-cutting behavior).
3. Fix `fix-now` handlers by wiring the correct existing module path. Preserve all existing switch branches, guards, argument validation, default/sentinel values, and reload/cooldown behavior.
4. If an assertion was wrong, fix the test so it checks the real observable effect; do not weaken it to `>= 0` or a no-op pass.
5. For every true out-of-scope gap, add a follow-up checklist/card reference in the result and keep the scenario failure explicit unless the roadmap owner has accepted a documented exclusion.
6. Re-run all six P6b contract tests together and ensure each exercised `SpecialPower*` order either passes with an observable effect or reports an accepted explicit gap.
7. If any new `.cs` files are created, ensure they are UTF-8 with BOM. Run `dotnet format src`, then verify with `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerApplicator.cs`
- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerAtLocationApplicator.cs`
- `src/OpenSage.Game/Logic/Orders/SpecialPower/SpecialPowerAtObjectApplicator.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6b*ContractTests.cs` (only to correct assertions/gap messages)
- `docs/roadmap/implementation-checklist.md` (tick the gap-closure item when complete)

## Acceptance criteria

- [ ] Every `P6b gap:` emitted by `WI-P6b-01` through `WI-P6b-06` is classified and recorded in `.exchange`.
- [ ] Every small `SpecialPower*` handler gap in scope is fixed with an observable effect and a passing contract assertion.
- [ ] Any out-of-scope handler is documented as an explicit follow-up; none silently succeeds without effect.
- [ ] Existing order/special-power branches, guards, default/sentinel values, reload timers, and command flags are preserved unless directly covered by a new assertion.
- [ ] All six P6b archetype contract tests run together and fail only for documented accepted gaps, or pass completely.
- [ ] `dotnet format src` has been run and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6bInfantryContractTests|FullyQualifiedName~P6bVehicleContractTests|FullyQualifiedName~P6bAircraftContractTests|FullyQualifiedName~P6bBuilderContractTests|FullyQualifiedName~P6bSupportContractTests|FullyQualifiedName~P6bSuperweaponContractTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement broad, high-risk special-power systems beyond gaps directly surfaced by the six P6b tests.
- Do not remove failing assertions to make validation green.
- Do not introduce skipped tests or Interactive-only acceptance gates.
- Do not run or rely on `git` commands.

## On completion

- Set Status above to `Done` (or `Blocked` + exact remaining gap reason).
- Tick the "All unimplemented special-power handlers surfaced by P6b-1 through P6b-6" item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing fixed handlers and documented follow-ups.
- List follow-up cards to create here:
  - None unless an accepted out-of-scope special-power gap remains.

## Result

<!-- append after completing -->
