# Task Card `WI-C2-02`: Add runtime animation binding

| Field | Value |
|---|---|
| Card ID | `WI-C2-02` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C2-01` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Add the format-neutral runtime target/channel model and bind direct-index channels into immutable
playback clips.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 3-5.
- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`.
- `src/OpenSage.Game/Graphics/Animation/AnimationClip.cs`.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`.
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`.

## Steps

1. Add immutable `ModelAnimationTarget` and `ModelAnimationChannel` runtime values.
2. Add `BoundModelAnimation` as a name, duration, and immutable target-indexed `AnimationClip`
   array.
3. Change `ImportedAnimationMaterializer` to create unbound runtime channels and targets.
4. Add `ModelAnimation.Bind(ModelBoneHierarchy)` with direct-index handling only.
5. Retain the existing direct clip surface temporarily for active callers; mark its removal as
   part of `WI-C2-05`.
6. Add tests proving in-range direct targets preserve channel order, clip type, timing, and values.

## Files likely to change

- `src/OpenSage.Game/Graphics/Animation/ModelAnimationTarget.cs` (new)
- `src/OpenSage.Game/Graphics/Animation/ModelAnimationChannel.cs` (new)
- `src/OpenSage.Game/Graphics/Animation/BoundModelAnimation.cs` (new)
- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`
- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`

## Acceptance criteria

- [ ] Materialized animations retain unbound direct-index target semantics.
- [ ] Binding produces immutable clips with no importer or parser types.
- [ ] Direct-index binding preserves all observable channel data.
- [ ] Existing playback still compiles and behaves unchanged during the staged refactor.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelAnimationTests|FullyQualifiedName~AnimationInstanceTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not bind named targets yet.
- Do not add cache or warning behavior.
- Do not change gameplay activation or `AnimationInstance` evaluation.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C2-03` to `Ready`.
- Append a result line to `.exchange`.
