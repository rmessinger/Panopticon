# Task Card `WI-M1-01`: 1000 vs 1000 engagement scenario

| Field | Value |
|---|---|
| Card ID | `WI-M1-01` |
| Roadmap phase | [M1](../../track-m-mass-battle.md): capacity probe |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M0-03` (deployment profile, spawn helpers), B1 benchmark phase done |
| Diagrams | [M1 capacity probe flow](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Direct-spawn two hostile 1000-unit armies (ground blocks plus aircraft parked at pre-placed airfields) from one committed profile on the M0 deepdivide map, order a general attack from one definition, and run it headless and windowed. This is the engagement M1-2 measures; it uses no AI players, only scripted orders.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` sections 3 and 7: viewing model and scale dependencies.
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentRunner.cs`: boot, spawn, airfield placement, windowed step loop to reuse.
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentProfile.cs`: profile records to extend.
- `src/Panopticon.Simulation/Logic/Object/Update/ProductionUpdate.cs` lines 390-410: parking-place wiring for produced jets (parking slot report, taxiing locomotor, `JetAIUpdate.Base`, parked state, parking transform).
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`: jet state machine; a parked jet takes off when given a target point.

## Steps

1. Extend `MassBattleCapacityProfile` with an optional `Aircraft` block (definition, count); validation becomes ground blocks plus aircraft equal to `UnitsPerSide`. The M0 profile keeps 1200 ground units and no aircraft; author `mass-battle-deepdivide-capacity-probe.json` with 600 Ranger + 280 Paladin + 112 Tomahawk + 8 `AmericaJetAurora` per side.
2. Extract the M0 runner's shared placement helpers (ground position, spawn at, color parsing, airfield placement and validation, capacity layout walk) into an internal static helper class; the M0 runner keeps its behavior and report unchanged.
3. Add `MassBattleCapacityProbeRunner`: boot like M0 but with hostile teams (0 and 1), spawn both armies and airfields, park aircraft at airfield slots using the production wiring sequence, settle, then issue the general attack: every ground unit `SetTargetPoint` at the enemy zone center, every jet the same (unpark, taxi, take off); re-issue jet orders when a jet parks again while enemies remain.
4. Record engagement observations per side: spawned, alive, destroyed, and per-jet sortie state history (`CurrentJetAIState` transitions, departures, landings). A jet with two departures has rearmed and re-sortied.
5. Add `MassBattleCapacityProbe` to `BuiltInScenario`, `Program` dispatch, and config knobs (profile/report/screenshot paths, measured frame budget); document the windowed command in the TestHarness AGENTS.md.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentProfile.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentRunner.cs` (helper extraction only)
- `src/Panopticon.Tools.TestHarness/MassBattlePlacement.cs` (new, shared helpers)
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeReport.cs` (new)
- `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-capacity-probe.json` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/AGENTS.md`
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeProfileTests.cs` (new, install-free)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeScenarioTests.cs` (new, Interactive, scaled roster)

## Acceptance criteria

- [x] One profile drives both modes: the scenario runs headless and windowed with no code change between them.
  - Headless verified by the scaled Interactive scenario test; windowed mode shares the M0 boot/step path and is the documented operator-run proof (Xvfb presentation is a documented dead end on this host, see `docs/screenshots.md`).
- [x] Both sides spawn 1000 units (990-1000 ground after impassable-slot skips plus 8 parked aircraft); aircraft sit in airfield parking slots with `JetAIUpdate.Base` set.
- [x] After the general attack: every jet departs (takes off), fires or reaches the enemy zone, and a jet that lands and parks departs again while enemies remain (rearm and re-sortie observed).
  - Scaled test: 4 jets, launches 3/1/4/1, recoveries 2/0/3/0.
- [x] Ground units of both sides close to contact and take losses on both sides within the frame budget.
  - Scaled test: 6 and 5 losses over 5400 frames.
- [x] Engagement report JSON records per-side spawned/alive/destroyed and per-jet sortie cycles; run exits 0.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).
  - Full non-interactive suite: identical known-failure set as the base branch.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario MassBattleCapacityProbe
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~MassBattleCapacityProbe"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No engine AI: orders are scripted; M4 owns army-scale AI.
- No rearm mechanic implementation: rearm is observed as the park, heal, re-clip, re-sortie cycle the airfield already provides.
- No performance measurement: that is WI-M1-02 on the same runner.
- No new map edits; the probe runs on the staged deepdivide map.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none; WI-M1-02 measures this scenario.

Engine fixes required to make direct-spawned parked jets fly (each verified by the scenario
test): `PhysicsBehavior.ScrubVelocityLateral2D` plus per-update lateral scrub in the jet ground
states (no nonholonomic constraint in the friction model made taxiing jets orbit waypoints);
takeoff speed now reads `Physics.Velocity.Length()` (`GameObject.Speed` is never populated by
the sim); `ParkingPlaceBehavior.IsTaxiingPointBlocked` blocks only on aircraft actually taxiing
or starting (cross-blocked parked jets deadlocked) and `ReserveRunway` no longer overwrites
existing assignments; dying jets release their parking slot once instead of every frame;
taxiing locomotors set `AllowInvalidPosition` (runway bones sit inside the airfield footprint).
