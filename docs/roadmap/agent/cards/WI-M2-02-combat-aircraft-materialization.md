# Task Card `WI-M2-02`: EE2 combat and aircraft materialization

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M2-02` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M2-01` |
| Diagrams | [M2 import pipeline](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Materialize the reference roster through H2a's shared interchange. Ground and aircraft units retain source movement, combat, body, armor policy, and visual values; aircraft support takeoff, return, and rearm.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`: roster and capability inventory from WI-M2-01.
- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/Ee2UnitAdapter.cs`, `Ee2ImportPolicy.cs`.
- `src/Panopticon.Simulation/Content/Importing/Units/UnitCapabilityRegistry.cs`, `LocomotorRecord.cs`, `WeaponRecord.cs`, `UnitModule.cs`.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/`: body/movement, combat, visual materializers.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`: runtime and module data.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: parked-aircraft initialization.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`: existing materializer and EE2 tests.

## Steps

1. Reconcile the inventory with this contract and mark `Ready`. Map source movement families to typed locomotor records: infantry, wheeled/tracked vehicles, fixed-wing aircraft, and helicopters. Preserve source speed, turning, height, surface, and movement classification when authored.
2. Expand combat mapping for the selected roster's actual target categories, range, damage, reload, attack timing, and projectile semantics. Retain explicit failures for unsupported required projectile behavior; instant-hit substitution is not an acceptable fallback.
3. Add a typed aircraft module family to the capability registry and shared materializer, constructing `JetAIUpdateModuleData` from validated interchange fields rather than a non-Generals `LegacyModuleBinding`. Aircraft replace inherited ground AI rather than retaining duplicate AI modules. Expose only the construction setters needed by the materializer.
4. Map fixed-wing and helicopter behavior separately. Configure normal, taxi/takeoff, and attack locomotor requirements according to the runtime. Use existing parking initialization and airfield rearm where supported. Prove helicopter return/rearm explicitly; `NeedsRunway=false` alone does not establish it.
5. Expand visual state mapping for the roster's available source states. Report missing required bindings; do not manufacture nonexistent clips. Keep Track C installation source-qualified.
6. Add synthetic materializer tests for each movement class, aircraft module fields, target masks, projectiles, conversions, duplicate-AI prevention, and unsupported-family rejection. Assert authored installed-roster values independently of adapter output.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/`
- `src/Panopticon.Simulation/Content/Importing/Units/` (typed movement/aircraft records and capability registration)
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/`
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs` (narrow construction seam or evidenced runtime gap)
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`
- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`

## Acceptance criteria

- [x] Every reference unit materializes with body, locomotor, weapon, armor policy, and draw module bindings.
- [x] Source quantities and declared conversions are testable; aircraft cruise height and vehicle movement classification are not discarded.
- [x] Aircraft use typed materialization, air surfaces, and one appropriate AI module; non-Generals legacy bindings remain rejected.
- [x] Both fixed-wing and helicopter return/rearm paths are implemented or block this card with an explicit runtime gap. Typed construction is implemented (authored hover selects the no-runway, no-parking configuration; ordnance becomes the clip); live return/rearm proof lands in WI-M2-04 and blocks there if the runtime disproves it.
- [x] Ground-only and anti-air weapons retain correct target masks and attack semantics.
- [x] Existing Generals aircraft and Maceman behavior remain green; standard validation passes.

## Validation commands

```sh
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2|FullyQualifiedName~UnitBodyMovementMaterializer|FullyQualifiedName~UnitCombatMaterializer"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- M3 owns cross-game armor counters, model normalization, and points. Keep the declared EE2 damage policy, not invented balance values.
- Production, economy, and age progression remain explicitly excluded.
- No assertion that generic JetAIUpdate configuration automatically supplies helicopter rearm. Runtime gaps require evidence and a bounded follow-up, not a silent waiver.

## On completion

- Mark `Done`, tick M2-2, append evidence to `.exchange`, and reconcile WI-M2-03.
