# Task Card `WI-M5-02`: Roster deployment profile

| Field | Value |
|---|---|
| Card ID | `WI-M5-02` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | M3 (`WI-M3-03`, done) |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Spawn each side's M3 roster manifest, Generals and EE2 rows together, on the M0 map from one
deployment profile: ground units in the side's zone and aircraft parked at that side's
airfields. This replaces the probe's shared capacity blocks for the battle; the probe keeps them.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m5-mass-battle.md`: decisions 1 and 4.
- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentProfile.cs`: profile records
  (`MassBattleSideProfile`, `MassBattleCapacityProfile`, `AirfieldAnchors`).
- `src/Panopticon.Tools.TestHarness/RosterBudgetEvaluator.cs`: `RosterManifest`, `RosterSide`,
  `RosterEntry` (`Source`, `Aircraft`), `LoadDefault`.
- `src/Panopticon.Tools.TestHarness/mass-battle-rosters.json`: both manifests.
- `src/Panopticon.Tools.TestHarness/MassBattlePlacement.cs`: `SpawnGroundArmy`, `PlaceAirfields`.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: `ParkAircraft`.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`: `Import`, `EMPIRE_EARTH_II_PATH`,
  `ee2:<RowKey>` publish names; `ee2-reference-validation.json` `ContentImport` block.

## Steps

1. Add an optional `Roster` (manifest side id) to `MassBattleSideProfile` and an optional
   manifest path plus `ContentImport` block to the profile. Validation: a side has a roster or the
   profile has capacity blocks, not both; every roster side id exists in the manifest.
2. Import EE2 content before spawning: collect the `ee2` row keys of both rosters into the
   `ContentImport` block and run `UnitContentImportRunner.Import`. Resolve `ee2` entries as
   `ee2:<RowKey>`; an unresolved definition is a failure reason, not a skip.
3. Spawn ground entries (`Aircraft` false) per side through the zone layout walk, in manifest
   order. Generalize `SpawnGroundArmy` to take the side's block list instead of
   `profile.Capacity.Blocks`.
4. Park aircraft entries at the side's airfields. Count parking slots per airfield and author
   enough `AirfieldAnchors` per zone for the manifest's aircraft. If the zone cannot hold them,
   stop and mark `Blocked` with the slot shortfall (decision 4).
5. Author `mass-battle-deepdivide-battle.json`: the deepdivide map and zones from the M0 profile,
   `generals-led` on side A, `ee2-led` on side B, airfield anchors from step 4.
6. Add install-free profile tests (validation, `ee2:` resolution, aircraft split) and one
   Interactive spawn test with a scaled roster (both installs).

## Files likely to change

- `src/Panopticon.Tools.TestHarness/MassBattleDeploymentProfile.cs`
- `src/Panopticon.Tools.TestHarness/MassBattlePlacement.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleRosterSpawner.cs` (new)
- `src/Panopticon.Tools.TestHarness/mass-battle-deepdivide-battle.json` (new)
- `src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj` (copy the profile)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleRosterProfileTests.cs` (new)

## Acceptance criteria

- [x] Both sides spawn their full manifests: per-definition counts match the manifest, minus
  impassable-slot skips reported per definition.
- [x] Every aircraft entry is parked in an airfield slot with `JetAIUpdate.Base` set, or the card
  is `Blocked` with the measured shortfall.
- [x] EE2 units resolve through the H2a import path; no EE2 definition is hand-authored.
- [x] The M1 probe profile and report are unchanged.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet test src --filter "FullyQualifiedName~MassBattleRoster|FullyQualifiedName~MassBattleCapacityProbe"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No roster or points changes; the manifests are M3 output.
- No airborne spawning and no new airfield mechanics.
- No AI, outcome, or run loop: `WI-M5-04` owns the scenario.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Result: `MassBattleSideProfile.Roster` + profile `RosterManifestPath`/`ContentImport` with
    validation (roster on every side or none, never mixed with capacity blocks, ids resolve);
    `MassBattleRosterSpawner` (Validate, DefinitionName ee2: prefix, GroundBlocks/AircraftEntries
    partition, ImportEe2Content collecting the manifest's ee2 row keys into the profile's import
    block, SpawnSide through the zone walk + parking); `SpawnGroundArmy` takes the caller's block
    list; `MassBattlePlacement.ParkingPlaces`/`ParkAircraft`/`ResolveDefinition` shared by probe
    and spawner (source-prefixed names resolve through the ee2 source store). Authored
    `mass-battle-deepdivide-battle.json`: 25 anchors side A / 38 side B (AmericaAirfield parks 4,
    footprint BOX 224x148 rotated by the side facing, deepdivide playable area 6000x4000
    measured); zones widened symmetrically x 2200 -> 3800 after the first full-scale run
    exhausted side A at 820/900 before the Tomahawk block. Full-scale validation (temporary
    uncommitted fact, both installs): side A 900/900 ground + 100/100 parked, side B 850/850
    ground + 150/150 parked, no placement reasons. Tests: install-free profile tests 5/5,
    Interactive scaled spawn 1/1, probe/deployment filters 7/7; full non-interactive suite
    pending in the `.exchange` line.
