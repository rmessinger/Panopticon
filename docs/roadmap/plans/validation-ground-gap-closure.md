# Plan: Unit Validation Ground gap closure

> Scope: retire or re-classify the 10 roster gaps the Unit Validation Ground reports, so the
> baseline covers as much of the roster as the sandbox can genuinely measure. Owner doc for the
> `WI-VG-` cards. Background and sandbox rules live in
> [P9 deep dive](../deep-dives/p9-unit-validation-ground.md); this file does not repeat them.

## Current gap inventory

Source: headless run 2026-08-15, targets `CarAsian1` at `TargetHp=30`, standoff 80.

| # | Unit | Kind | Root cause | Bucket |
|---|---|---|---|---|
| 1 | `AmericaVehicleMedic` | SpecialPower | `ValidationGroundHost.BuildOrder` returns null; no per-unit `SpecialPowerType` argument construction | A |
| 2 | `ChinaInfantryBlackLotus` | SpecialPower | same | A |
| 3 | `ChinaInfantryHacker` | SpecialPower | same | A |
| 4 | `GLAInfantryHijacker` | SpecialPower | same | A |
| 5 | `AmericaInfantryPathfinder` | Attack | cataloged as unmeasurable: sniper weapon vs target armor | B |
| 6 | `AmericaJetAurora` | Attack | cataloged: `JetAIUpdate` bombing run not triggered by a direct weapon command | B |
| 7 | `ChinaVehicleTroopCrawler` | Attack | cataloged: transport, no direct-fire weapon discharge | B |
| 8 | `GLAInfantryAngryMobNexus` | Attack | cataloged: horde composite, per-unit weapon targeting does not apply | B |
| 9 | `GLAInfantryTerrorist` | Attack | cataloged: suicide weapon, point-blank only | B |
| 10 | `ChinaVehicleHelix` | Attack | Zero Hour-only definition; absent from a vanilla Generals install | C |

Bucket C is resolved: `UnitOption.ZeroHourOnly` tags the entry, the scenario carries the target
game (set from the harness config), and the builder skips such entries on runs whose game lacks
them, so a vanilla run no longer reports the gap (31 total, 9 gaps).

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

`ValidationGroundHost.BuildOrder` deliberately returns null for
`PrimaryActionKind.SpecialPower` instead of dispatching a malformed order. The engine-side
applicators exist and were hardened in `WI-P6b-07`
(`src/OpenSage.Game/Logic/Orders/SpecialPower/`); the missing piece is per-unit argument
construction in the host:

1. Resolve the unit's `SpecialPower` module (`GameObject.FindBehavior<SpecialPowerModule>` or the
   definition's `SpecialPower` list) to a concrete `SpecialPowerType`.
2. Map the type to the order path the applicators implement (`SpecialPowerAtObject`,
   `SpecialPowerAtLocation`, no-target `SpecialPower`) and build the `Order` with the arguments
   the applicator parses (power id, target object id or position, flags).
3. Completion predicates in `UnitActionTimer`/observation seam per power class: heal amount
   (`Medic`), cash delta or hack completion (`BlackLotus`, `Hacker`), vehicle ownership transfer
   (`Hijacker`). Spawn a suitable subject object per unit (a damaged friendly for the medic, an
   enemy vehicle for the hijacker) the way targets are spawned today.
4. Any power whose applicator throws `NotImplementedException` stays a gap, with the reason
   promoted from the exception message rather than a generic string.

Exit: the four bucket-A units are either measured with baselines or carry engine-specific gap
reasons pointing at the unimplemented applicator.

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
  units (`Terrorist`) below their weapon range, instead of the global 80.
- Bombing-run trigger: command `JetAIUpdate` aircraft through their AI update (attack order to a
  position/object) rather than a direct weapon command (`Aurora`), once the jet attack state
  machine supports it.
- Transport load/fire: issue a load-then-attack sequence or fire-on-move command
  (`TroopCrawler`); horde members inherit the nexus target (`AngryMobNexus`) if the horde
  containment model exposes per-member weapons.

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
