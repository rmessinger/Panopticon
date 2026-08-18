# Plan: Unit Validation Ground gap closure

> Scope: retire or re-classify the 10 roster gaps the Unit Validation Ground reports, so the
> baseline covers as much of the roster as the sandbox can genuinely measure. Owner doc for the
> `WI-VG-` cards. Background and sandbox rules live in
> [P9 deep dive](../deep-dives/p9-unit-validation-ground.md); this file does not repeat them.

## Current gap inventory

Source: headless run 2026-08-15, targets `CarAsian1` at `TargetHp=30`, standoff 80. Updated
2026-08-18: the Pathfinder is measured (per-unit target override, see WI-VG-04), the remaining
verdicts are re-grounded in the install's INI data, and bucket A is closed by P6c (implemented
handlers; see [track P](../track-p-playable-baseline.md)).

| # | Unit | Kind | Root cause | Bucket |
|---|---|---|---|---|
| 1 | `AmericaVehicleMedic` | SpecialPower | closed 2026-08-18 (P6c): `SPECIAL_CLEANUP_AREA` heals friendlies in the power radius; measured (1 frame) | A |
| 2 | `ChinaInfantryBlackLotus` | SpecialPower | closed 2026-08-18 (P6c): capture-building transfers ownership; measured (1 frame) | A |
| 3 | `ChinaInfantryHacker` | SpecialPower | closed 2026-08-18 (P6c): disable-building disables the target; measured (1 frame) | A |
| 4 | `GLAInfantryHijacker` | SpecialPower | closed 2026-08-18 (P6c): `HijackerUpdate` contact hijack; measured (51 frames) | A |
| 5 | `AmericaInfantryPathfinder` | Attack | closed 2026-08-18: see WI-VG-04 | B |
| 6 | `AmericaJetAurora` | Attack | `JetAIUpdate` has no attack state, so the bombing run cannot be commanded; `AuroraBombWeapon` is only delivered by the run | B |
| 7 | `ChinaVehicleTroopCrawler` | Attack | its only weapon (`TroopCrawlerAssault`, 1e-5 damage of type Deploy) is the passenger assault signal, not a damage source | B |
| 8 | `GLAInfantryAngryMobNexus` | Attack | the nexus weapon (0.001 damage) is harmless by design; damage comes from `SpawnBehavior` members that receive no attack order | B |
| 9 | `GLAInfantryTerrorist` | Attack | closed: point-blank placement override; measured | B |
| 10 | `ChinaVehicleHelix` | Attack | Zero Hour-only definition; absent from a vanilla Generals install | C |

Bucket C is resolved: `UnitOption.ZeroHourOnly` tags the entry, the scenario carries the target
game (set from the harness config), and the builder skips such entries on runs whose game lacks
them, so a vanilla run no longer reports the gap (31 total, 3 gaps, 28 measured).

## Work items

| Card | Title | Effort | Depends on |
|---|---|---|---|
| `WI-VG-01` | Re-verify catalog gaps against the car target | S | none |
| `WI-VG-02` | SpecialPower order construction in the host | M | none |
| `WI-VG-03` | Game-aware unit catalog | S | none |
| `WI-VG-04` | Sandbox mechanics for remaining unmeasurables | M/L | `WI-VG-01` verdicts |

### WI-VG-01: Re-verify catalog gaps against the car target

The `UnmeasurableAttackReasons` verdicts were captured against `AmericaBarracks`
(`StructureArmor`). The target is now `CarAsian1` (`TruckArmor`), so every verdict is stale until
re-tested. Known data points to check, not assume:

- `Pathfinder`: `TruckArmor` takes `Sniper = 1%`. The weapon's raw damage times 1% may still kill a
  30 HP target within the 250-frame attack budget, or may not. Measured answer required.
- `Terrorist` (point-blank suicide) and `Aurora` (bombing run) are mechanics gaps, not armor gaps;
  expected to remain unmeasurable at any standoff.

Method: remove one unit from `UnmeasurableAttackReasons`, run the headless ground, and read the
verdict (`OK`/`REGRESSED`/`TIMEOUT`). A `TIMEOUT` with zero target damage confirms the gap; restore
the entry. An `OK`/`NEW` measurement promotes the unit to the baseline (refresh + determinism check
per the standard capture workflow). Record each verdict in `.exchange`.

Exit: every bucket-B entry carries a current, target-specific verdict; promoted units are in the
baseline.

### WI-VG-02: SpecialPower order construction in the host

Done 2026-08-18, together with P6c (the engine-side handlers). The host builds real
SpecialPower orders (location powers at the subject position, object powers against a per-unit
subject), spawns ability subjects (damaged friendly for the Medic, neutral building for Black
Lotus and the Hacker, enemy vehicle for the Hijacker), and completes each unit on its per-power
observable effect (heal observed, ownership changed, target disabled). All four bucket-A units
carry baseline entries; two headless runs are byte-identical.

### WI-VG-03: Game-aware unit catalog

`ChinaVehicleHelix` is cataloged for the roster but exists only in Zero Hour, so a vanilla
Generals run always reports `definition not found`. Options, in preference order:

1. Tag Zero Hour-only entries (the `ZeroHourOnlyDefinitions` set already exists in
   `ValidationGroundTests`) in `UnitCatalog` itself, and have the builder skip them for
   `SageGame.CncGenerals` scenarios instead of emitting a gap.
2. Alternatively, key the roster on the running game and emit the catalog from
   `UnitCatalog.ForFaction` variants per game.

Exit: a vanilla run reports no `definition not found` gaps; Zero Hour coverage is preserved (or
explicitly deferred with a recorded decision).

### WI-VG-04: Sandbox mechanics for remaining unmeasurables

For bucket-B units that `WI-VG-01` confirms unmeasurable, close them by adding sandbox mechanics
rather than leaving permanent gaps. Candidate mechanics, each independently shippable:

- Close-quarters placement: per-unit `PlacementOverride` shrinking the standoff for point-blank
  units (`Terrorist`) below their weapon range, instead of the global 80. Done: the Terrorist is
  measured.
- Per-unit target override: `UnitOption.TargetOverride` replaces the shared car target for units
  whose weapon cannot damage it. Done 2026-08-18 for the Pathfinder (`GLAInfantryRebel` target,
  2-frame golden), and this surfaced an engine bug: Generals' `FLESHY_SNIPER` and Zero Hour's
  `SUBDUAL_MISSILE` share enum value 31, so every Generals sniper hit was classified as subdual
  damage and silently dropped (`DamageConstants.IsSubdualDamage`, fixed with a game-aware check;
  regression test `src/OpenSage.Game.Tests/Logic/DamageTypeCollisionTests.cs`). The
  `TargetOverrides_AreDamageableByPrimaryWeapon` data test keeps every override consistent with
  the install's weapon and armor INI.
- Bombing-run trigger: command `JetAIUpdate` aircraft through their AI update (attack order to a
  position/object) rather than a direct weapon command (`Aurora`), once the jet attack state
  machine supports it. Open: `JetAIUpdate` has no attack state.
- Transport load/fire: issue a load-then-attack sequence or fire-on-move command
  (`TroopCrawler`); horde members inherit the nexus target (`AngryMobNexus`) if the horde
  containment model exposes per-member weapons. Open: the TroopCrawler's only weapon is the
  Deploy-type passenger signal, and the mob nexus is a `SpawnBehavior` controller whose members
  receive no attack order.

Each mechanic lands with its unit promoted to the baseline. Items that require engine work beyond
the sandbox stay recorded gaps and escalate to the owning roadmap track (P6b for order paths,
Track D content expansion for horde/transport behaviors).

## Sequencing

`WI-VG-01` first (cheap, reclassifies the bucket-B list and may promote units for free).
`WI-VG-02` and `WI-VG-03` are independent and can proceed in parallel. `WI-VG-04` consumes
`WI-VG-01` verdicts. After every promotion: baseline refresh, two-run determinism diff, and the
`BaselineGoldenFrames_AreConsistentWithWeaponIniData` gate, per the P9 capture workflow.

## Non-goals

- No engine behavior changes inside validation-ground work items except through the escalation
  rule already documented in the P9 deep dive (gaps escalate to their owning track).
- No tolerance widening to absorb a failing unit.
