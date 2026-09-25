# Task Card `WI-C4g-05`: Bind morph weight animation

| Field | Value |
|---|---|
| Card ID | `WI-C4g-05` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4g-03`, `WI-C4g-04` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Convert NIF morph weight keys into C6 morph animation channels and bind them to materialized model
morph target sets during playback.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 5, 6, 7, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4.2 and 6.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationImporter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Gltf/GltfAnimationConverter.cs:121-132`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Graphics/ModelAnimation.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`.

## Steps

1. Change Status to `In progress`.
2. Add morph channel materialization beside existing bone channel materialization.
3. Bind morph channels by primitive id and target-name vector against the model's morph target
   sets.
4. Convert linear NIF float keys directly into weight vectors.
5. Convert quadratic NIF float keys through the C4c Hermite evaluator and resample at 30 Hz,
   including all source key times.
6. During animation update, evaluate bound morph tracks and set the current weight vector on the
   target `ModelMeshInstance`.
7. Add synthetic tests for a two-target morph channel with direct linear keys and a quadratic
   resampled curve.
8. Add an installed-asset test that samples `bld_farm_yam.kf` at 0, half duration, and end and
   observes weights 0, 0.5, and 1 for `Target1`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationImporter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifMorphConverter.cs`
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/Panopticon.Simulation/Graphics/ModelAnimation.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`
- `test/Panopticon.Simulation.Tests/Graphics/ModelMorphTests.cs`

## Acceptance criteria

- [ ] Morph animation binding fails when primitive ids or target names do not match.
- [ ] Linear weight keys preserve source key times and values.
- [ ] Quadratic weight keys are resampled to linear weight vectors with source key times included.
- [ ] `bld_farm_yam.kf` evaluates `Target1` weight 0 at start and 1 at end.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelMorphTests"
dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add material color, UV, alpha, or visibility controller playback.
- Do not add cubic interpolation to C6.
- Do not permit missing morph target sets to degrade into no-op playback.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4g-06` to `Ready`.
- Append a result line to `.exchange`.
