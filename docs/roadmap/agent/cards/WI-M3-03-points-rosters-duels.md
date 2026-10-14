# Task Card `WI-M3-03`: Cross-game points budget, asymmetric roster manifests, and duel matrix

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M3-03` |
| Roadmap phase | [M3](../../track-m-mass-battle.md): Mixed roster normalization |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M3-02` |
| Diagrams | [M3 normalization flow](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Author the cross-game points budget (recorded rates and tolerance), two different 1000-unit mixed roster manifests with anti-air and equal points within tolerance, and an EE2-versus-Generals archetype duel matrix whose time-to-kill results land within authored bounds and are reported as machine-readable JSON.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 5 (points, manifests, mirrored control).
- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md` (row keys, stock stats, contract machinery).
- `src/Panopticon.Tools.TestHarness/Ee2ContractPhase.cs` (game-session contract pattern: spawn, orders, wait loops, JSON report).
- `src/Panopticon.Tools.TestHarness/ValidationGroundRunner.cs` (`ValidationGroundEe2ProfilePath` wiring), `TestHarnessConfig.cs`.
- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/Ee2CsvTable.cs`, `Ee2SourceFiles.cs` (CSV reading).
- `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-capacity-probe.json` (committed manifest style).
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2ContractValidationTests.cs` (installed session test pattern).

## Steps

1. Author the committed roster manifest `mass-battle-rosters.json` (TestHarness, shipped beside
   the executable) recording: the points rates (Generals `BuildCost` at 1 point per dollar; EE2
   resource columns summed at 5 points per resource unit, the median of the counterpart anchors
   Crusader/BattleMaster/Raptor/Aurora/Comanche/Tomahawk versus the EE2 resource sums, with the
   premium-Generals-infantry deviation recorded), the tolerance (0.5 percent of the larger
   total), and the two manifests:
   - generals-led (1000 units, 846,375 points): 455 AmericaTankCrusader, 130 AmericaVehicleHumvee,
     115 AmericaInfantryRanger, 100 AmericaJetRaptor, 120 GLAVehicleQuadCannon, 80 AmericaVehicleTomahawk;
   - ee2-led (1000 units, 845,900 points): 300 HeavyInfantry1UpgradeEpoch15, 150 LightMounted1UpgradeEpoch15,
     120 HeavyMounted1UpgradeEpoch15, 60 HeavyArtilleryUpgradeEpoch14, 80 FighterUpgradeEpoch15,
     40 BomberUpgradeEpoch15, 30 LightMounted2UpgradeEpoch13, 120 GLAVehicleQuadCannon, 100 GLAInfantryRebel.
   Both carry anti-air (QuadCannon plus Raptor or Fighter) and aircraft; aircraft count toward
   the 1000 and airfields do not (they are not units).
2. Add a budget evaluator in TestHarness that prices a manifest through the recorded rates:
   Generals entries against the booted game's `ObjectDefinition.BuildCost`, EE2 entries against
   the install's `db/TechTree/upgrade_unittypes.csv` resource columns read through `Ee2CsvTable`
   (the per-epoch cost columns live there; `dbtechtreenode.csv` carries zeros for these rows).
   It reports per-entry and total points, unit counts, anti-air presence, and the tolerance
   verdict as JSON.
3. Add `Ee2RosterBudgetTests` (Interactive, both installs): loads the committed manifest,
   prices both sides through the evaluator, and asserts 1000 units per side, anti-air present,
   differing compositions, and totals equal within the recorded tolerance.
4. Extend the validation ground with a duel phase: `Ee2ValidationProfile` gains a `Duels` list
   (name, EE2 definition, Generals definition, engagement distance, frame budget, authored
   min/max seconds); a new `Ee2DuelPhase` runs each duel in the same session after the contracts
   (ground units through real move-and-attack orders; aircraft spawned airborne through the M2
   fixed-wing pattern), measures frames from orders to the first death, and writes
   `ee2-duel-report.json` (per-duel definition pair, seconds, winner, bound verdict; plus the
   priced roster summary). Duels: infantry HeavyInfantry1UpgradeEpoch15 vs GLAInfantryRebel,
   light tank LightMounted1UpgradeEpoch15 vs AmericaTankCrusader, heavy tank
   HeavyMounted1UpgradeEpoch15 vs ChinaTankBattleMaster, artillery HeavyArtilleryUpgradeEpoch14
   vs AmericaVehicleTomahawk, fighter FighterUpgradeEpoch15 vs AmericaJetRaptor, bomber
   BomberUpgradeEpoch15 vs GLAVehicleQuadCannon (the Generals anti-air counterpart, since
   Generals fields no stock level bomber), helicopter LightMounted2UpgradeEpoch13 vs
   AmericaVehicleComanche.
5. Measure the duels once against the installs, then author each bound in
   `ee2-reference-validation.json` from the measured seconds with headroom and a rationale
   comment (stock weapon and hitpoint arithmetic; no bound may be tightened without evidence).
6. Add `Ee2DuelValidationTests` (Interactive, both installs): boots the session, imports the
   mapped roster, runs the duel phase, and asserts every duel resolves within its authored
   bounds and the report names every result; include a synthetic report-shape test.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/mass-battle-rosters.json`, `Panopticon.Tools.TestHarness.csproj`
- `src/Panopticon.Tools.TestHarness/RosterBudgetEvaluator.cs` (new), `Ee2DuelPhase.cs` (new)
- `src/Panopticon.Tools.TestHarness/Ee2ContractPhase.cs` (profile types), `ValidationGroundRunner.cs`, `TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/ee2-reference-validation.json`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2RosterBudgetTests.cs`, `Ee2DuelValidationTests.cs` (new)
- `docs/roadmap/implementation/track-m-m3-mixed-roster.md`

## Acceptance criteria

- [x] Generals `BuildCost` and EE2 resource costs convert to points through rates recorded in
      the committed manifest; the evaluator reports per-entry and total points.
- [x] Two different 1000-unit manifests, each with anti-air, total equal points within the
      recorded tolerance.
- [x] Every EE2-versus-Generals archetype duel, air included, resolves within its authored
      time-to-kill bounds; the JSON report names each duel, its seconds, winner, and verdict.
- [x] Standard validation passes.

## Result

Done 2026-10-11. `mass-battle-rosters.json` (shipped beside the harness executable) records the
rates (Generals 1 point per BuildCost dollar, EE2 5 points per summed resource unit, the
counterpart-anchor median), the 0.5 percent tolerance, and both mixed manifests: generals-led
846,375 points (455 Crusader, 130 Humvee, 115 Ranger, 100 Raptor, 120 QuadCannon, 80 Tomahawk)
and ee2-led 845,900 points (300 EE2 infantry, 150 light tanks, 120 heavy tanks, 60 artillery,
80 fighters, 40 bombers, 30 helicopters, 120 QuadCannon, 100 Rebels); 1000 units each, both
with anti-air and aircraft, 0.056 percent apart. `RosterBudgetEvaluator` prices them against
the booted game's BuildCosts and the install's tech-tree resource columns
(`db/TechTree/upgrade_unittypes.csv`; `dbtechtreenode.csv` carries zeros for these rows).
`Ee2DuelPhase` runs the seven archetype duels after the contracts and writes
`ee2-duel-report.json`; the shipped validation profile carries the duel matrix with bounds
authored from measured runs (infantry 13.2s Rebel, light tank 8.7s Crusader, heavy tank 10.8s
BattleMaster, artillery 10.7s Tomahawk, fighter 2.4s EE2 fighter, bomber 4.0s EE2 bomber,
helicopter 6.6s EE2 helicopter; Generals units win the ground duels, matching their 5-6x point
cost, and EE2 aircraft win the air duels). The profile also gained the infantry row key (it
publishes since WI-M2-05), so the duel roster matches the manifest. Engagement uses the
contract phase's killer pattern (per-frame weapon targets); frame-zero AttackObject orders on
mutually hostile combatants stall both fire cycles.

Validation: `Ee2RosterBudgetTests` 2/2 installed; `Ee2DuelValidationTests` 2/2 installed (plus
the synthetic report-shape fact); authored harness run
(`ValidationGroundEe2ProfilePath` set to the shipped profile, headless): standard Generals
ground unchanged (26 measured, 4 pre-existing timeouts, 1 gap, exit 3), EE2 contracts 6/6,
EE2 duels 7/7 with the report naming seconds, winner, and bounds for every duel; full
non-interactive suite only the pre-existing `ParkingPlaceBehavior_ReportLanding`; format and
brace checks clean.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2RosterBudget|FullyQualifiedName~Ee2DuelValidation"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No army-scale AI, batch runs, or map placement: M4 and M5 own those; the manifests are data
  plus a pricing check, not a battle scenario.
- No rebalancing of weapon or hitpoint values to force duel outcomes; bounds follow measured
  evidence.
- The mirrored control run is M5-2 calibration, not part of this card.

## On completion

- Mark `Done`, tick M3-3, set the M3 phase row to `Done` when all three cards hold, append
  evidence to `.exchange`, and create follow-up cards for M4 reconciliation if any runtime gap
  appeared.
