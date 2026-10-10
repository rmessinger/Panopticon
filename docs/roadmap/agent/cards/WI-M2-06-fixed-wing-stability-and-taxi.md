# Task Card `WI-M2-06`: Fixed-wing flight stability and taxi

| Field | Value |
|---|---|
| Card ID | `WI-M2-06` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-M2-04` (blocked follow-up recorded there) |
| Diagrams | none (materializer constants) |

## Audience

Agent.

## Goal

Remove the fixed-wing M2 blocker: imported air locomotors carry the engine-native airborne
sideslip damping, aircraft get a distinct ground taxiing locomotor, and the full fixed-wing cycle
(return, refill, second damaging sortie) becomes deterministic and asserted, unblocking
`WI-M2-04` and M2-4.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`: WI-M2-04 evidence section (the
  divergence record: excursions 2.5k to 380k world units, ~58-unit taxi turn radius).
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitBodyMovementMaterializer.cs`:
  air locomotor construction (~line 108) and `AddLocomotors` taxiing-set reuse (~lines 271-298).
- `src/Panopticon.Simulation/Logic/Object/Behaviors/PhysicsBehavior.cs`
  `ApplyFrictionalForces` (~lines 926-977): what `Apply2DFrictionWhenAirborne` changes.

## Grounding (measured, from Generals `INI.big`)

Every Generals crewed aircraft locomotor (AuroraJet, RaptorJet, MIG, Comanche, Chinook, and the
rest) sets `Surfaces=AIR`, `AllowAirborneMotiveForce=Yes`, `ZAxisBehavior=SURFACE_RELATIVE_HEIGHT`,
and `Apply2DFrictionWhenAirborne=Yes` (turn rates 120-180 deg/s). Generals authors separate taxi
locomotors: `BasicJetTaxiLocomotor` (`Surfaces=GROUND`, speed 50, turn rate 180, acceleration 100;
turn radius ~16 units, which fits parking bone spacing) and `BasicHelicopterTaxiLocomotor`
(30/90/30).

The imported air locomotor sets only `AllowAirborneMotiveForce`; without the 2D friction flag,
`ApplyFrictionalForces` applies only weak aerodynamic friction while airborne, so
`MoveTowardsPositionOther`'s forward-speed maintenance accumulates lateral velocity through
sustained turns and heading reversals: the runaway observed as the M2 blocker. This is an import
gap, not a physics-fidelity question.

## Files

- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitBodyMovementMaterializer.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2TypedMaterializationTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2ContractValidationTests.cs`

## Steps

1. Set `Apply2DFrictionWhenAirborne = true` beside `AllowAirborneMotiveForce` on air-surface
   locomotor imports (engine-native sideslip damping; no engine physics change).
2. Author a distinct taxiing locomotor for the `Taxiing` set instead of reusing the air
   locomotor: `Surfaces=GROUND`, speed 50 units/s, turn rate 180 deg/s, acceleration 100
   units/s^2 (BasicJetTaxiLocomotor values, through the existing conversion helpers), named with
   a taxi suffix and source-qualified. The same authored-default class as the existing
   `AircraftSpeedRampFrames` ramp.
3. Extend the synthetic materialization tests: air locomotors assert the friction flag; the
   taxiing set asserts a distinct ground locomotor with the taxi constants.
4. Run `Ee2ContractValidationTests` twice (installed). If the full fixed-wing cycle is now
   deterministic, strengthen the installed assertions from reported to asserted: return, refill,
   and second damaging sortie for the fighter and the bomber.
5. If divergence persists in either run, stop and preserve the report: the remaining question is
   an engine physics behavior, not import wiring, and needs an operator decision.

## Acceptance criteria

- [ ] Synthetic tests assert the friction flag and the distinct taxiing locomotor.
- [ ] Installed contract run, repeated, asserts the full fixed-wing cycle for fighter and bomber
      (return, refill, second damaging sortie).
- [ ] The helicopter and ground contracts stay green; the maceman profile stays green.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2TypedMaterialization|FullyQualifiedName~Ee2ContractValidation"
```

## Out of scope / guardrails

- No engine physics edits: the fix sets existing engine flags on imported templates and authors
  imported taxi constants; if that is insufficient, report rather than change the flight model.
- No balance changes to authored EE2 speeds and turn rates.

## On completion

- Set Status to `Done`, append `.exchange`, update the M2 implementation doc WI-M2-04 evidence,
  tick M2-4 in the checklist, set the M2 phase status in the track doc, and flip `WI-M2-04` to
  `Done`.
