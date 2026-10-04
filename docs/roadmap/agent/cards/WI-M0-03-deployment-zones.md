# Task Card `WI-M0-03`: Deployment zones and reachability check as scenario data

| Field | Value |
|---|---|
| Card ID | `WI-M0-03` |
| Roadmap phase | [M0](../track-m-mass-battle.md): battle map close-out |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-M0-01` (deepdivide selected) |
| Diagrams | none (tool-only change mirroring `ValidationGroundRunner`; no engine architecture change) |

## Goal

Prove deepdivide can host the mass battle without editing it: a committed deployment profile (zone rectangles, airfield anchors, proxy roster) drives a new `MassBattleDeployment` TestHarness scenario that spawns 1000 units per side at twice footprint spacing with airfields behind each zone, moves one representative per distinct ground locomotor class between the zones through real engine move orders, and writes a JSON proof report.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 4: zones are scenario data, never map edits.
- `docs/roadmap/agent/cards/WI-M0-01-map-evaluation.md`: deepdivide survey record (bands, gap 480, centroids).
- `src/Panopticon.Tools.TestHarness/ValidationGroundRunner.cs:47-135`: direct-spawn game setup to mirror (skirmish start, inert victory, AI disabled, fixed timestep).
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:642-648`: real pathfinded move via `AIUpdate.SetTargetPoint`.
- `src/Panopticon.Simulation/Navigation/Navigation.cs:56-76`: `IsPassable` and `CalculatePath` (locomotor-agnostic grid).

## Steps

1. Author `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-deployment.json`: map name/path/provenance, two sides (zone rect, facing, airfield anchors, color), capacity probe (1000 per side as 600 Ranger + 280 Paladin + 120 Tomahawk, spacing factor 2), reachability probe knobs.
2. Add `MassBattleDeployment` to `BuiltInScenario`, config knobs (profile/report/screenshot paths), the profile + report models, and `MassBattleDeploymentRunner` (spawn capacity stage, airfield placement, separation check vs longest roster `AttackRange`, per-class reachability probes, report JSON, exit codes 0/1/5).
3. Build, `dotnet format`, brace-newline check.
4. Stage the community `deepdivide.map` into the install at `maps/deepdivide/deepdivide.map` (never committed).
5. Run headless against the staged map until the report passes; commit the report JSON beside this card.
6. Capture one deployment screenshot for the operator; document the windowed run command (operator-run).

## Files likely to change

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`
- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj`
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentProfile.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentReport.cs` (new)
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentRunner.cs` (new)
- `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-deployment.json` (new)
- `src/Panopticon.Tools.TestHarness/AGENTS.md` (scenario section)
- `docs/roadmap/agent/cards/WI-M0-03-deployment-proof.json` (proof artifact)

## Result

Run on the staged deepdivide (2026-10-04, headless, exit 0; proof in
[WI-M0-03-deployment-proof.json](WI-M0-03-deployment-proof.json), byte-identical across two runs):

| Check | Result |
|---|---|
| Capacity per side | 1000/1000 units inside the zone rect, 0 impassable placements (41/52 impassable slots skipped by the layout walk) |
| Airfields | 2/2 per side behind the zones on passable ground |
| Separation | zone gap 480 vs longest roster weapon range 350 |
| Grid path | centroid-to-centroid 2 nodes, end passable |
| Reachability | 3 ground classes arrive: Ground (AircraftCessna, 705 frames), Ground+Water (AmphibiousTransport, 1436), Ground+Rubble (GLAInfantryAngryMobMolotov02, 2820) |

Representative selection is the fastest definition per class (a class's best crossing
capability); near-zero-speed units (e.g. SpyDrone) would otherwise stretch frame budgets into
the millions, so budgets are clamped to `maxFrameBudget` (30000). Whole-map framing needs a
`FarPlaneDistance` bump past the 12000 default or the far deployment clips out of view.

## Acceptance criteria

- [x] Profile is scenario data only: no map edit, no map file committed; provenance recorded in the profile.
- [x] Headless run spawns 1000 units per side fully inside each zone rect at twice footprint spacing with zero impassable placements, plus two airfields placed behind each zone (between zone and map edge) on passable ground.
- [x] Zone edge gap exceeds the longest `AttackRange` among the spawned roster; both values recorded in the report.
- [x] A representative unit of every distinct ground locomotor `Surfaces` class used by AIUpdate-bearing object definitions arrives zone A to zone B within its frame budget, and `Navigation.CalculatePath` connects the two zone centroids.
- [x] Report JSON committed; run exits 0.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleDeployment
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No per-locomotor passability grid: the engine pathfinder is locomotor-agnostic today, so classes group locomotors by template `Surfaces` and one representative moves per class. Real per-locomotor pathing belongs to the pathfinding track (B series); recorded in the report.
- No map edits, no authored fallback (WI-M0-02 stays deferred), no M1 scenario work.
- Windowed load proof is operator-run; the command is documented in the AGENTS.md section.

## On completion

- Set Status above to `Done`.
- Update the roadmap phase Status if this card completes it.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none; M1-1 consumes the committed profile.
