# Task Card `WI-C4e-03`: Extend C1 property channels

| Field | Value |
|---|---|
| Card ID | `WI-C4e-03` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C4e-01`, `WI-C4e-02` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Add format-neutral animation targets and channel types for sub-object visibility and material-slot property animation, without changing existing W3D or C4c bone animation behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 6, 7, and 8.
- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 3, 4, and 8.
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationTarget.cs`.
- `src/Panopticon.Simulation/Content/Importing/ImportedKeyframe.cs`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs`, `ModelAnimationTarget.cs`, `AnimationClipType.cs`, and `BoundModelAnimation.cs`.

## Steps

1. Add imported and runtime target kinds for sub-object name and material slot.
2. Add channel types for `TextureTranslateU`, `TextureTranslateV`, `TextureScaleU`, `TextureScaleV`, `TextureRotate`, four material-color fields, and `MaterialAlpha`.
3. Add `Vector3` keyframe payload support for material color channels.
4. Update constructors and validation so bone-only channels retain existing direct-index and bone-name behavior.
5. Update `ImportedAnimationMaterializer` to carry the new target and channel values into `ModelAnimation`.
6. Add unit tests for valid material-slot channels, sub-object visibility channels, invalid keyframe kinds, and unchanged W3D direct-index materialization.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationTarget.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedKeyframe.cs`
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClipType.cs`
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimationTarget.cs`
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimationChannel.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAnimationTests.cs`

## Acceptance criteria

- [x] Existing transform and visibility bone channels materialize byte-for-byte equivalent runtime clips.
- [x] Sub-object visibility and material-slot channels validate their target kind and keyframe value kind.
- [x] Material color channels require `Vector3` keyframes.
- [x] Material alpha and UV channels require float keyframes.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedAnimation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement NIF conversion in this card.
- Do not evaluate runtime material animation in `AnimationInstance` yet.
- Do not change gameplay INI animation selection.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4e-01` and `WI-C4e-02` are done, change `WI-C4e-04` to `Ready`.
- If done, change `WI-C4e-05` to `Ready`.
- Append a result line to `.exchange`.
