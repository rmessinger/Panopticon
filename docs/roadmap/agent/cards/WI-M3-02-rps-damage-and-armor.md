# Task Card `WI-M3-02`: rps counter classes and dbcombat matrix to Generals armor and damage types

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M3-02` |
| Roadmap phase | [M3](../../track-m-mass-battle.md): Mixed roster normalization |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M3-01` |
| Diagrams | [M3 normalization flow](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Replace the authored one-to-one UNRESISTABLE damage policy for the reference roster with an explicit authored mapping from EE2 rps counter classes to Generals damage types and armor sets, driven by `db/Simulation/dbcombat.csv`, with unmapped classes failing load.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md` (damage policy, dbcombat mask use, exclusions census).
- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/Ee2ImportPolicy.cs`, `Ee2UnitAdapter.cs` (`CombatMasks`, `TargetCategories`, `AttackRecords`, `ReadProperties`).
- `src/Panopticon.Simulation/Content/Importing/Units/ArmorRecord.cs`, `WeaponRecord.cs`, `UnitRecord.cs`.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitCombatMaterializer.cs` (armor and damage type materialization).
- `src/Panopticon.Simulation/Logic/Object/Damage/DamageType.cs`, `ArmorTemplate.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureContentImport.cs`; TestHarness profiles from WI-M3-01.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2TypedMaterializationTests.cs` (synthetic fixture pattern with an inline dbcombat fragment), `Ee2InventoryTests.cs`.

## Steps

1. Add `RpsDamageTypes` (rps class to Generals damage type name) to `Ee2ImportPolicy` and
   `CaptureContentImport`, authored in the two reference-roster profiles as:
   Heavy_Infantry PIERCE, Light_Mounted SLASH, Heavy_Mounted SIEGE, Heavy_Artillery BIG_ROCK,
   Air_Superiority HERO_RANGED, Bomber URUK, Helicopter HERO. These are distinct engine-known
   BFME-family damage types that no native Generals armor authors (audited against the install's
   `Armor =` lines), so EE2-authored damage stays one-to-one against native armor and native
   damage stays one-to-one against imported armor, while EE2-versus-EE2 resolves through the
   matrix. Record that rationale in the profile comments; the type names carry no semantic claim.
2. In `Ee2UnitAdapter`, when the mapping is active: consume the `rps` property, resolve the
   weapon damage type through the table, and emit one `ArmorRecord` per rps class
   (multipliers from the class's dbcombat defender row over the mapped attacker types,
   percent divided by 100, zeros included) with the unit record referencing it. Keep the
   existing zero/nonzero target-category masks.
3. Fail load explicitly (capability-policy style, `H2A-UNSUPPORTED-MECHANIC`) when: a unit's
   rps class is absent from the table while the mapping is active; a unit has rps while the
   mapping is inactive and rps is not excluded; or a mapped class has no dbcombat defender row.
   Keep the current behavior byte-identical when rps is excluded (the maceman bounded profile:
   policy damage type default, no armor record).
4. Drop `rps` from the reference-roster exclusions in both profiles and in
   `Ee2InventoryTests.ReferenceExclusions`; update `Ee2RosterPublicationTests` and
   `Ee2ContractValidationTests` imports to the mapped policy. The `ImpactEffectDamageTypes`
   presentation mapping stays (effect selection only).
5. Tests: extend the synthetic materialization tests (inline dbcombat fixture) to assert mapped
   weapon damage types, armor multipliers straight from the matrix, and each explicit failure
   mode; extend the installed inventory test to assert every reference row materializes an armor
   set and a damage type and that the published EE2 weapons no longer deal UNRESISTABLE damage.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/Ee2ImportPolicy.cs`, `Ee2UnitAdapter.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureContentImport.cs`
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`, `ee2-reference-validation.json`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2TypedMaterializationTests.cs`, `Ee2InventoryTests.cs`, `Ee2RosterPublicationTests.cs`, `Ee2ContractValidationTests.cs`
- `docs/roadmap/implementation/track-m-m3-mixed-roster.md`

## Acceptance criteria

- [ ] Every reference EE2 unit maps to an armor set and a damage type through the authored table.
- [ ] EE2-versus-EE2 armor multipliers reproduce the dbcombat matrix; EE2-versus-native and
      native-versus-EE2 damage apply one-to-one.
- [ ] Unmapped, unmapped-and-unexcluded, and matrix-missing classes fail load explicitly.
- [ ] The maceman bounded profile and its tests stay green unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2"
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~ScenarioCapture"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No balance authoring: multipliers come from the stock matrix; no invented values.
- No native Generals content changes; no patch-archive composition (stock db.zip layer only).
- Cross-game counter behavior beyond the one-to-one damage application is not authored here;
  the points budget and duel bounds (WI-M3-03) own cross-game fairness.

## On completion

- Mark `Done`, tick M3-2 in the checklist, append evidence to `.exchange`, and reconcile
  WI-M3-03 to `Ready`.
