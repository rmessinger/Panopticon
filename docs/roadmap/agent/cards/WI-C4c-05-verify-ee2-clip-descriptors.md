# Task Card `WI-C4c-05`: Verify EE2 clip descriptors

| Field | Value |
|---|---|
| Card ID | `WI-C4c-05` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4c-04` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Lock installed-asset import, C2 binding, and sampled bone poses for the seven DDF-bound proof
clips and the unreferenced negative clip.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 2 and 8.
- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`: install guard and archive
  mounting.
- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs` and
  `src/OpenSage.Game/Graphics/ModelInstance.cs:170-183`.

## Steps

1. Add interactive `Ee2KfAnimationImportTests` using the existing `EMPIRE_EARTH_II_PATH` guard
   and `graphics.zip` mount.
2. Import each section 2 clip with its model key. Assert duration, channel count, and target
   names.
3. Materialize model and clip, then bind through `ModelInstance.CreateAnimationInstance` and
   assert that no binding error occurs.
4. Advance to fixed times and read bone transforms. Assert:
   - Galley Idle offsets are identity within 1e-4;
   - `prop_01` turns 90 degrees within 0.5 degrees from 0 to 1/12 s of Walk;
   - Bomber world bone transforms at `TakeOff` time 0 equal rest;
   - `root NonAccum` at `TakeOff` 1.333 s equals Walk at time 0.
5. Assert that importing `ab11_TwinEngineBomber.kf` against the Bomber throws and names `prop_1`.

## Files likely to change

- `src/OpenSage.Game.Tests/Content/Importing/Ee2KfAnimationImportTests.cs` (new)

## Acceptance criteria

- [ ] All seven clips import, materialize, and bind deterministically.
- [ ] Every section 8 pose assertion passes.
- [ ] The negative clip fails before playback with a message naming `prop_1`.
- [ ] Tests are `Category=Interactive` and never copy extracted assets.
- [ ] Standard validation passes.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2KfAnimationImportTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change importer behavior to make a test pass. Report deviations instead.
- Do not add captures. Those belong to `WI-C4c-06`.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4c-06` to `Ready`.
- Append a result line to `.exchange`.
