# Track M · M3: Mixed roster normalization

> Audience: human + agent.

Scope: [M3 checklist](../implementation-checklist.md#m3-mixed-roster-normalization).
Architecture: [M3 normalization flow](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).
M2 machinery: [M2 implementation plan](track-m-m2-ee2-combat-units.md).

## Card sequence

| Card | Deliverable | Initial gate |
|---|---|---|
| [WI-M3-01](../agent/cards/WI-M3-01-model-scale-and-factor.md) | Authored tiles-to-world factor and model scale, bounds manifest, height test | Ready || [WI-M3-02](../agent/cards/WI-M3-02-rps-damage-and-armor.md) | rps and dbcombat mapping to Generals damage types and armor sets | Ready (scale evidence reconciled) |
| [WI-M3-03](../agent/cards/WI-M3-03-points-rosters-duels.md) | Points budget, two 1000-unit manifests, duel matrix and report | Ready (damage evidence reconciled) |

One card per commit; one M3 phase PR left open for operator merge. Draft cards become Ready only
after prerequisite names and runtime contracts are reconciled with landed code.

## Authored conversion derivation (WI-M3-01)

Measured from the operator installs (asserted by `Ee2ModelScaleTests`):

- EE2 NIF models are authored in decimeters: the epoch-15 heavy-infantry standing mesh is 20.08
  units tall and the late-era light-tank hull 100.94 units long.
- One EE2 tile spans 50 model units: infantry `SizeX` 0.125 tile = 6.25 units against the 5.50
  unit mesh width, tank `SizeX` 0.7 tile = 35 against 39.30, artillery `SizeX` 1 tile = 50
  against 37.57. The bomber and helicopter meshes overflow their cells (bomber width 59.34
  units, helicopter width 66.56 against 35); recorded as a stock-data deviation.
- Generals scale is 3.6501 game units per meter (G10 Crusader hull anchor,
  `reference-scale-manifest.json`).

Authored constants: `GameUnitsPerSourceDistance` 18.2505 (one tile = 5 m) and
`GameUnitsPerSourceModelUnit` 0.36501 (one model unit = 0.1 m), satisfying
18.2505 = 50 x 0.36501. The M2-era value 12 was an unformalized profile constant; M3 replaces
it in the reference-roster profiles only. The maceman bounded profile keeps its recorded values.

Scaled model heights land at 0.77 to 1.23 of the Generals vehicle references; infantry and
fixed-wing land near 0.50, mirroring Generals' own oversized-infantry and undersized-aircraft
asset conventions (G10 per-object references and `VisualScaleOverrides`). Bounds are committed
in `test/Panopticon.Simulation.Tests/Data/Content/Importing/Units/Ee2/ee2-model-scale-bounds.json`.

Runtime seams:

- The model scale folds into the imported draw's world matrix (`W3dModelDraw.ApplyImportedModelScale`,
  local frame before placement), so bones, attachments, client model bounds, and shadows follow;
  native draws keep scale 1 and the G10 render-list override stays independent.
- EE2 attack ranges are authored as ground-plane tile distances, so imported EE2 weapons set
  `WeaponTemplate.MeasuresRangeOnGround` and the engine's range check uses the horizontal
  component for them. Without this the 3D check lets a cruising altitude consume the authored
  range (a 1.5-tile bomb range can never reach the ground from a 4-tile cruise; the M2-era
  factor-12 bomber contract only passed through target-radius subtraction). Native weapons keep
  the 3D check.
- The fixed-wing contract spawns aircraft at their authored cruising height (the factor-converted
  `PreferredHeight`) instead of a fixed 48-unit offset; spawning below it made the JetAI landing
  flow take over before the wings model reached its authored minimum speed.

## Damage class mapping (WI-M3-02)

EE2 rps classes map one-to-one onto distinct engine damage types that no native Generals armor
authors (audited against the install's `Armor =` lines): Heavy_Infantry PIERCE, Light_Mounted
SLASH, Heavy_Mounted SIEGE, Heavy_Artillery BIG_ROCK, Air_Superiority HERO_RANGED, Bomber URUK,
Helicopter HERO. Consequences, recorded with the mapping:

- EE2 versus EE2 resolves through the stock `db/Simulation/dbcombat.csv` matrix (defender row,
  mapped attacker columns, percent divided by 100).
- EE2 versus Generals applies authored EE2 damage one-to-one (native armors author none of the
  mapped types), replacing M2's UNRESISTABLE policy with the same effective values through type
  choice.
- Generals versus EE2 applies native damage one-to-one (imported armors author no native types).

## Points budget and manifests (WI-M3-03)

Rates: Generals `BuildCost` at 1 point per dollar; EE2 resource columns summed at 5 points per
resource unit (median of the counterpart anchors: Crusader 900 vs 159, BattleMaster 800 vs 159,
Raptor 1400 vs 280, Aurora 2500 vs 500, Comanche 1500 vs 145, Tomahawk 1200 vs 400; Generals
infantry are premium, Ranger 225 vs 119, recorded as an accepted deviation). Tolerance 0.5
percent. Committed in `mass-battle-rosters.json` with both manifests and their totals
(846,375 versus 845,900 points, 0.056 percent apart).

## Validation evidence

Filled per card as each lands.
