# Track M · M2: EE2 combat units

> Audience: human + agent.

Scope: [M2 checklist](../implementation-checklist.md#m2-ee2-combat-units).
Architecture: [M2 pipeline](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).
Import policy boundaries: [H2a handoff](../deep-dives/h2a-unit-content-import.md#m2-handoff).

## Card sequence

| Card | Deliverable | Initial gate |
|---|---|---|
| [WI-M2-01](../agent/cards/WI-M2-01-ddf-roster-resolution.md) | Complete DDF inventory, inherited dependencies, authored reference roster | Ready |
| [WI-M2-02](../agent/cards/WI-M2-02-combat-aircraft-materialization.md) | Typed movement, combat, aircraft, and visual materialization | Reconcile roster evidence after 01 |
| [WI-M2-03](../agent/cards/WI-M2-03-scoped-roster-registration.md) | Scoped roster publication beside Generals | Reconcile after 02 |
| [WI-M2-04](../agent/cards/WI-M2-04-p9-contract-validation.md) | P9 contract results, aircraft rearm, phase report | Reconcile after 03 |

One card per commit; one M2 phase PR left open for operator merge. Draft cards become Ready only after prerequisite names and runtime contracts are reconciled with landed code.

## Reference roster and inventory

Card WI-M2-01 evidence, asserted directly against the operator install by
`Ee2InventoryTests` (Interactive; `EMPIRE_EARTH_II_PATH`).

### Archive layer policy

M2 reads the stock `zips/db.zip` layer only. Patch archives beside it (`db_150.zip`, `db_155.zip`,
and later) are reported by an `M2-ARCHIVE-LAYER` info diagnostic and are never composed, so stock
values are not mistaken for as-patched values (patch Tank e15 is HP 600 damage 45 against stock 300
and 30). Composing the patch layer needs an explicit table and config selection rule and stays out
of M2. The expansion archive set `zips_ee2x/` is out of M2 scope.

### Inventory census

All 117 `db/Units/*.ddf` files in the stock archive parse, including the three files that motivated
the reader extensions (`tankbarrier.ddf`, `test_building.ddf`, `test_human.ddf` author
`AnimName = }`; 13 files author `( Flag | Flag )` rally lists). Directly measured census:

| Scope | UnitType | UnitModel | UpgradeAbilities | UpgradeRefSet |
|---|---|---|---|---|
| Full archive (117 files) | 438 | 618 | 184 | 50 |
| `dbunitfiles.cfg` files (115) | 434 | 616 | 184 | 50 |

The read-only survey reported 432/615 for the cfg scope; the installed truth is 434/616 and the
assertions use the direct count. `dbunitfiles.cfg` lists 138 files of which 115 are unit DDFs; the
two test DDFs are not loaded by the game's own config. The unit table
`db/TechTree/upgrade_unittypes.csv` has 396 data rows, 34 columns (two named DISPLAYNAME), EPOCH 1
through 15.

### Reference roster

Row keys are exact `upgrade_unittypes.csv` keys; the row's UNIT cell names the UnitType, which is
why tanks publish through the LightMounted1 and HeavyMounted1 prefixes. Stock values below are
asserted by `ReferenceRosterRowsComposeWithStockValuesAndOverrides`.

| Class | Row key | UnitType | Epoch | rps | HP | LOS | Damage | Range | Reload | Visual |
|---|---|---|---|---|---|---|---|---|---|---|
| Infantry | HeavyInfantry1UpgradeEpoch15 | HeavyInfantry1 | 15 | Heavy_Infantry | 185 | 10 | 11 | 9 | 1.5 | HeavyInfantry1_15 |
| Light tank | LightMounted1UpgradeEpoch15 | Tank | 15 | LightMounted | 300 | 11 | 30 | 10 | 3.5 | LightMounted1_15 |
| Heavy tank | HeavyMounted1UpgradeEpoch15 | HeavyTank | 15 | HeavyMounted | 350 | 11 | 25 | 10 | 3 | HeavyMounted1_15 |
| Artillery | HeavyArtilleryUpgradeEpoch14 | HeavyArtillery | 14 | Heavy_Artillery | 225 | 10 | 150 | 15 | 4 | HeavyArtillery_14 |
| Fighter | FighterUpgradeEpoch15 | Fighter | 15 | Air_Superiority | 500 | 9 | 88 | 8 | 1 | Fighter_15 |
| Bomber | BomberUpgradeEpoch15 | Bomber | 15 | Bomber | 940 | 9 | 270 | 1.5 | 0.3 | Bomber_15 |
| Helicopter | LightMounted2UpgradeEpoch13 | LightMounted2 | 13 | Helicopter | 150 | 10 | 60 | 9 | 2 | LightMounted2_13 |

Roster selection notes, derived from the install:

- LightTank.ddf defines a UnitType but has no CSV UNIT rows; the light tank is the Tank line
  (LightMounted1UpgradeEpoch15).
- HeavyArtillery's latest row is epoch 14.
- Helicopter reference uses epoch 13: the e15 helicopter models (LightMounted2_15, HeavyMounted2_15)
  author no AnimName at all, while e13 has all four clips. Late epoch is satisfied at 13 of 15.
- HeavyMounted1_15 (heavy tank) authors no Attack1 animation (the source comments the clip out);
  recorded as a visual gap, not fabricated.

### Ability override composition

Each row's UPGRADEREFS name `UpgradeAbilities` records (for example
`LightMounted1UpgradeEpoch15` references `LightMounted1Epoch15Attack`). Their ability blocks are
composed over the UnitType chain: Attack overrides supply missileName, applyDamageTime, ordnance,
areaDamageRadius, and minRangeMobile; Move, VehicleMove, and AircraftMove overrides replace the
inherited locomotor values (Fighter e15 flies 8.05 tiles/s from its AircraftMove override; HeavyTank
e15 moves at 2.3 from HeavyMounted1Epoch15Move). CSV cells carry damage, range, reload, HP, and LOS
and beat the override placeholders (0, 0, 666). Stat-set refs (veteran and elite Improve sets) are
reported as non-gameplay metadata, not applied. Missiles resolve against `db/Visuals/ammo.ddf`
(TankShell 20, GuidedMissile 11, Bomb 2 tiles/s); a missile missing from that table is an explicit
import failure, never an instant-hit fallback.

### Explicit exclusions for the reference roster

The M2 profile carries these authored-but-unsimulated mechanics as explicit exclusions; each is
reported as `H2A-EXCLUDED-BY-POLICY`: ownertype, rps, FootprintType, SizeY, mass, popCount,
attributes, nonAttributes, stance, Garrisonable, Convertable, ConvertAttack, Cost,
GoesOnAirMissions, visualName, Attack.areaDamageFriendlyRatio, Attack.angularRange,
Attack.turretControllerName, Attack.ignoreBlocking, Attack.attackOnTheMove, Attack.WeaponType,
Attack.packTime, Attack.unpackTime, Attack.minRangeImmobile, Attack.capture,
Attack.startWithNoOrdnance, VehicleMove.followTerrainSlope, VehicleMove.mustMoveToTurn,
VehicleMove.bounce, AircraftMove.followTerrainSlope, AircraftMove.mustMoveToTurn,
AircraftMove.ElevChangeRate, AircraftMove.OnGroundScale, AircraftMove.CargoHeight,
AircraftMove.accel, Move.angAccel, Move.bounce, ProjectileMissileMove.type,
ProjectileMissileMove.accel, ProjectileMissileMove.angSpeed, Missile.PayloadAttack.

## Runtime gate

Existing path: `Ee2UnitAdapter` emits shared records; `UnitImportResolution` validates them; `UnitRuntimeMaterializer` constructs native module data; `UnitContentPublisher` installs source-qualified definitions and Track C visuals.

Planned extension: typed aircraft construction using `JetAIUpdateModuleData`, not non-Generals legacy module passthrough. Fixed-wing parking is evidenced by M1; helicopter return/rearm remains an explicit proof obligation. Unsupported required runtime behavior blocks affected reference entries.

## Validation evidence

WI-M2-01 (2026-10-08): `dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~DdfReader|FullyQualifiedName~Ee2"` with
`EMPIRE_EARTH_II_PATH` set: 83 passed, 1 pre-existing failure (`Ee2MorphImportTests.MuzzleCannonStillFailsAtTheNiUVControllerParseGuard`
fails identically on clean main; NIF parse guard, outside M2). Reader extensions, full-inventory
parse (117 files, zero parse errors), cfg and archive censuses, patch layer reporting, roster
composition with stock values, closure isolation (unrelated inventory failures downgrade to
`[inventory only]` warnings), and missing missile rejection are asserted by `DdfReaderTests`,
`Ee2RowCompositionTests` (synthetic, install-free), and `Ee2InventoryTests` (installed).
