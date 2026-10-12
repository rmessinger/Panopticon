# Task Card `WI-M3-01`: Model scale and tiles-to-world factor

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M3-01` |
| Roadmap phase | [M3](../../track-m-mass-battle.md): Mixed roster normalization |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M2-04` |
| Diagrams | [M3 normalization flow](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Normalize imported EE2 models and gameplay distances to Generals conventions through one recorded derivation: an authored tiles-to-world factor and an authored model-unit scale, applied by the runtime, with a committed bounds manifest and a test comparing imported EE2 model heights and lengths against Generals reference units.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 5 (Rosters, normalization bullets).
- `docs/roadmap/track-g-rendering-clarity.md` G10 row and `test/Panopticon.Simulation.Tests/Graphics/Proportions/ReferenceScaleManifest.cs` (Generals units-per-meter anchor and the FX-exclusion measurement pattern).
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitConversionPolicy.cs` (declared distance conversion).
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitVisualMaterializer.cs` and `UnitMaterializationContext.cs`.
- `src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs` (`SetWorldMatrix`, G10 `GetPresentationScale`).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureContentImport.cs`, `CaptureProfileValidator.cs`.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`, profiles `capture-ee2-reference-roster.json`, `ee2-reference-validation.json`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2RosterPublicationTests.cs`, `Ee2ContractValidationTests.cs`.

## Steps

1. Record the derivation as authored constants in the two reference-roster profiles
   (`capture-ee2-reference-roster.json`, `ee2-reference-validation.json`), replacing the M2-era
   value 12: `GameUnitsPerSourceDistance` 18.2505 and a new `GameUnitsPerSourceModelUnit` 0.36501.
   Derivation (measured from the installs; recorded in the profile comments and the M3 plan doc):
   EE2 NIF models are authored in decimeters (epoch-15 infantry standing mesh 20.08 units,
   late-era light-tank hull 100.94 units), one EE2 tile spans 50 model units (infantry `SizeX`
   0.125 tile = 6.25 units against a 5.50-unit mesh width; tank `SizeX` 0.7 tile = 35 units
   against 39.30; artillery `SizeX` 1 tile = 50 units against 37.57), and Generals scale is
   3.6501 game units per meter (G10 Crusader hull anchor). So one tile is 5 m = 18.2505 game
   units and one model unit is 0.1 m = 0.36501 game units; the pair satisfies
   18.2505 = 50 x 0.36501.
2. Add `GameUnitsPerSourceModelUnit` (nullable, default null = no scaling) to
   `CaptureContentImport` with validation in `CaptureProfileValidator`, thread it through
   `UnitContentImportRunner` into a new `UnitMaterializationContext` conversion, and stamp it on
   the `W3dModelDrawModuleData` built by `UnitVisualMaterializer` through a narrow internal
   setter.
3. Apply the model scale in `W3dModelDraw.SetWorldMatrix` by folding `Matrix4x4.CreateScale`
   into the world matrix before it reaches the model instance (local-frame scale, translation
   unaffected), so bones, attachments, client model bounds, shadows, and the render list all
   follow. Keep the G10 `GetPresentationScale` render-list override independent (it is
   name-keyed for native objects only). The maceman bounded profile
   `capture-mixed-source-ee2.json` keeps its recorded M2 values and no model scale.
4. Commit the authored bounds manifest as test data
   (`Data/Content/Importing/Units/Ee2/ee2-model-scale-bounds.json`, copied to output): one entry
   per reference row naming the EE2 model key, the Generals reference object, and authored
   ratio bounds for height and length, with rationale comments. Measured at the authored scale:
   infantry height ratio 0.50, light tank 1.23, heavy tank 0.78, artillery 0.77, fighter 0.49,
   bomber 0.49, helicopter 0.75; the ground vehicles land near parity and the infantry and
   fixed-wing deviations mirror Generals' own oversized-infantry and undersized-aircraft asset
   conventions (G10 per-object references and `VisualScaleOverrides`).
5. Add `Ee2ModelScaleTests` (Interactive; both installs): measure each reference model's
   FX-excluded merged extents through `NifModelImporter` (M3-1 measured bounds above), apply the
   authored model scale, measure the Generals reference model extents through `W3dModelImporter`,
   and assert height and length ratios within the manifest bounds. Also assert the two reference
   profiles record the consistent factor pair (18.2505 = 50 x 0.36501) and that a materialized
   reference definition's draw module carries the scale. Add a synthetic no-install test for the
   world-matrix scale folding.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureContentImport.cs`, `CaptureProfileValidator.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json`
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`, `ee2-reference-validation.json`
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitMaterializationContext.cs`, `UnitVisualMaterializer.cs`
- `src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2ModelScaleTests.cs` and test project content entries
- `test/Panopticon.Simulation.Tests/Data/Content/Importing/Units/Ee2/ee2-model-scale-bounds.json`
- `docs/roadmap/implementation/track-m-m3-mixed-roster.md`

## Acceptance criteria

- [x] Both reference-roster profiles record 18.2505 and 0.36501 with the derivation, and a test
      enforces the 50-unit consistency invariant.
- [x] Imported EE2 draw modules scale their world matrix by the authored model scale; a synthetic
      test proves the folding math; native definitions and the maceman bounded profile are
      unchanged.
- [x] `Ee2ModelScaleTests` compares every reference model's scaled height and length against the
      Generals reference model within the committed manifest bounds.
- [x] Standard validation passes.

## Result

Done 2026-10-11. Factor 12 replaced by the derived pair (18.2505 tiles-to-world, 0.36501 per
model unit) in both reference-roster profiles with the derivation recorded; the maceman bounded
profile is untouched. Model scale folds into the imported draw's world matrix
(`W3dModelDraw.ApplyImportedModelScale`), with `W3dModelDrawModuleData.ImportedModelScale` set by
`UnitVisualMaterializer` through `UnitMaterializationContext.GameUnitsPerSourceModelUnit`.

Two normalization findings landed beyond the constants, both evidenced:

- EE2 authors attack ranges as ground-plane tile distances; imported EE2 weapons set
  `WeaponTemplate.MeasuresRangeOnGround` so the engine's 3D range check cannot let cruising
  altitude consume the authored range. At the normalized factor the bomber's 1.5-tile range from
  a 4-tile cruise never reached the ground target (the M2 factor-12 pass only fit through
  target-radius subtraction); with the ground-plane check the full fixed-wing rearm cycle is
  deterministic again.
- The fixed-wing contract now spawns aircraft at their factor-converted cruising height instead
  of a fixed 48-unit offset; spawning below it let the JetAI landing flow take over before the
  wings model reached its authored minimum speed.

Validation: `Ee2ModelScaleTests` 2/2 (no install) and `Ee2ModelScaleInstalledTests` 2/2 (both
installs; measured ratios inside the committed bounds); `Ee2ContractValidationTests` 2/2 at the
new factor; `Ee2RosterPublicationTests` 3/3; `Ee2TypedMaterializationTests` 6/6 (gained the
ground-range assertion); maceman `ScenarioCaptureRunnerTests` and `UnitContentImportIntegrationTests`
6/6 unchanged; full non-interactive suite shows only the pre-existing
`ParkingPlaceBehavior_ReportLanding` failure; solution format and brace checks clean.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2ModelScale|FullyQualifiedName~Ee2RosterPublication|FullyQualifiedName~Ee2ContractValidation"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No gameplay rebalancing: weapon damage, hitpoints, and speeds stay authored; only unit
  conversions change (distances through the factor, model extents through the scale).
- No change to Generals-native definitions, the G10 presentation-scale overrides, or
  `UnitConversionPolicy`'s quantity contract.
- Do not scale collision geometry through the model scale: gameplay geometry already converts
  through `GameUnitsPerSourceDistance`.
- The maceman bounded profile stays byte-identical in behavior (factor 12, no model scale).

## On completion

- Mark `Done`, tick M3-1 in the checklist, append evidence to `.exchange`, and reconcile
  WI-M3-02 to `Ready`.
