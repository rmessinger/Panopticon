# Task Card `WI-P6c-01`: Special-power implementation

| Field | Value |
|---|---|
| Card ID | `WI-P6c-01` |
| Roadmap phase | [Track P · P6c](../../track-p-playable-baseline.md); plan: [validation-ground-gap-closure](../../plans/validation-ground-gap-closure.md) |
| Status | `Done` |
| Type | `implementation` |
| Effort | `L` |
| Prerequisites | `WI-P6b-07` |
| Diagrams | none (small surface: two applicators, one update module, host wiring) |

## Goal

Implement the special-power handlers that P6b surfaced as accepted explicit gaps and P9 reports as
validation-ground bucket-A gaps, so the Medic, Black Lotus, Hacker, and Hijacker are measured for
real instead of reported as gaps.

## Scope and INI anchors

All data from a vanilla Generals install (`INI.big`):

- Ambulance: `CleanupAreaPower` (location power `SPECIAL_CLEANUP_AREA`,
  `SpecialAbilityAmbulanceCleanupArea`, `RadiusCursorRadius = 110`).
- Black Lotus: `SpecialAbilityBlackLotusCaptureBuilding` (`StartAbilityRange = 150`,
  `PreparationTime = 6000`), `SpecialAbilityBlackLotusDisableVehicleHack`
  (`PreparationTime = 2000`), `SpecialAbilityBlackLotusStealCashHack`
  (`PreparationTime = 6000`, `EffectValue = 1000`).
- Hacker: `SpecialAbilityHackerDisableBuilding` (`StartAbilityRange = 150`,
  `PreparationTime = 3000`, `EffectDuration`).
- Hijacker: `HijackerUpdate` + `ConvertToHijackedVehicleCrateCollide`
  (`RequiredKindOf = VEHICLE`); contact-based capture, no special-power order path.

## Implementation notes

- Location/object applicators dispatch in
  `src/Panopticon.Simulation/Logic/Orders/SpecialPower/SpecialPowerAtLocationApplicator.cs` and
  `SpecialPowerAtObjectApplicator.cs`; mirror `ExecuteCashHack`/`OCLSpecialPower` for structure.
- Ownership transfer: `GameObject.Owner` (internal setter) plus team reassignment; money transfer:
  mirror `CashHackSpecialPower.Activate`.
- Disable: `GameObject.SetDisabled(DisabledType.UserParalyzed)` for the ability's effect duration.
- Hijack: `HijackerUpdate.Update()` scans for a valid enemy vehicle within contact distance on
  each tick; on contact, transfer `vehicle.Owner`, consume the hijacker.
- The P6b support contract test currently locks in the CleanupArea gap; update it to assert the
  real effect once implemented.

## Acceptance criteria

- [x] No applicator in scope throws `NotImplementedException` for the powers listed above.
- [x] Each power has an observable-effect assertion (heal amount, owner change, disabled state,
      cash delta, vehicle ownership) in `Panopticon.Simulation.Tests`.
- [x] The four validation-ground units complete with per-power completion predicates and carry
      baseline entries.
- [x] `dotnet build src`, `dotnet test` (non-Interactive suites), `dotnet format src
      --verify-no-changes` all pass.
- [x] Checklist items P6c-1 through P6c-4 are ticked with ACs met.

## Out of scope

- General-purpose `SpecialAbilityUpdate` state machines (walk-to-range, unpack/prep/pack
  animation timing). Effects apply on order dispatch at sandbox distances; duration data is read
  from the ability module where the effect needs it (disable duration).
- Remaining unimplemented powers (charges, disguise, superweapon strikes); they stay explicit gaps.
