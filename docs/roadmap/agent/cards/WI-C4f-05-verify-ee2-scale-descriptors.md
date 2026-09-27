# Task Card `WI-C4f-05`: Verify EE2 scale descriptors

| Field | Value |
|---|---|
| Card ID | `WI-C4f-05` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4f-04` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Add installed-asset tests for the DDF-bound EE2 scale proof clips, including positive rest scale and
the singular-rest failure contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 2, 3, 9, and 10.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`: EE2 install guard and archive
  mounting.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2KfAnimationImportTests.cs`: C4c installed KF tests
  after `WI-C4c-05`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`: materialization path.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs:173-182`: animation binding entry point.

## Steps

1. Add interactive `Ee2KfScaleAnimationImportTests` using the existing `EMPIRE_EARTH_II_PATH`
   guard and `graphics.zip` plus `db.zip` mounts.
2. Import and materialize the skinless proof clips:
   - `AB14_stealthbomber.nif` with `ab14_stealthbomber_takeoff.kf`;
   - `AB14_stealthbomber.nif` with `ab14_stealthbomber_idle.kf`;
   - `AB15_orbital.nif` with `ab15_orbital_takeoff.kf`;
   - `afb13_StrikeFighter.nif` with `afb13_StrikeFighter_takeoff.kf`.
3. Assert each clip emits `Scale` channels for the expected target names and value ranges from the
   deep dive.
4. Bind through `ModelInstance.CreateAnimationInstance` and sample fixed times. Assert the target
   bone local or relative matrix scale equals the expected source scale divided by rest scale.
5. Import `CIV1_RoyalChariot_Egyptian_idle01.kf` and assert `Bone01` uses rest scale 0.8499998
   with scale offset 1.
6. Import `LLA11_FieldArtillery_idle01.kf` and assert it throws `NotSupportedException` naming
   `boom` and rest scale `0`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2KfScaleAnimationImportTests.cs` (new)

## Acceptance criteria

- [x] Skinless DDF-bound scale clips import, materialize, bind, and sample deterministically.
- [x] Positive non-unit rest scale reconstructs the authored source scale.
- [x] Singular rest scale fails before playback with a message naming `boom`.
- [x] Tests are `Category=Interactive` and never copy extracted assets.
- [x] Standard validation passes.

Deviations:

- `NifGeometryData` now consumes EE2 10.1 float RGBA vertex colors (user decision: widen parser,
  keep proof) so `CIV1_RoyalChariot_Egyptian` imports; colors are read and ignored.
- RoyalChariot proof uses `ImportSkeleton` (bone rest + channel assertion) because its skinned
  shape stays rejected by the C4d non-unit-rest-skin guard; full mesh conversion is C4f out of
  scope. Its idle01 keys are constant 0.85, so no `Scale` channel survives the identity drop.
- Deep-dive table values are min to max, not time order; takeoff clips start at 1.0 and ease down
  to the listed minimum.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2KfScaleAnimationImportTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add screenshots or TestHarness capture plans.
- Do not change DDF state playback.
- Do not loosen the singular-rest failure to make `LLA11_FieldArtillery` pass.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4f-06` to `Ready`.
- Append a result line to `.exchange`.
