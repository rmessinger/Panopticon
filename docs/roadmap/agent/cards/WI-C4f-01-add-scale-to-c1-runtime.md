# Task Card `WI-C4f-01`: Add scale to C1 and runtime animation

| Field | Value |
|---|---|
| Card ID | `WI-C4f-01` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-06` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Add a uniform rest-scale field and a float scale animation channel to the C1 interchange and
runtime animation path, with default behavior unchanged for existing assets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 5, 6, and 9.
- `src/Panopticon.Simulation/Content/Importing/ImportedBone.cs`: current C1 bone fields.
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs` and `ImportedKeyframe.cs`:
  channel types and float keyframes.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`: channel mapping.
- `src/Panopticon.Simulation/Graphics/ModelBone.cs`, `ModelBoneHierarchy.cs`, and `ModelBoneInstance.cs`:
  runtime rest transform and offset composition.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClipType.cs` and `AnimationInstance.cs`:
  runtime clip evaluation.
- `src/Panopticon.Simulation/Transform.cs`: existing uniform scale support.

## Steps

1. Add `BindScale` to `ImportedBone`. Keep existing constructors by forwarding to a new overload
   with `bindScale: 1.0f`.
2. Validate bind scale is finite. Reject negative values in the constructor.
3. Pass `BindScale` through `ModelBoneHierarchy` to `ModelBone`, and construct the bone `Transform`
   with translation, rotation, and scale.
4. Add `Scale` to `ImportedAnimationChannelType` and `AnimationClipType`.
5. Reuse `ImportedKeyframeKind.Float` for scale by updating `ImportedKeyframe.GetExpectedKind`.
6. Map imported `Scale` channels to runtime `Scale` clips in `ImportedAnimationMaterializer`.
7. Reset `AnimatedOffset.Scale` to `1.0f` in `AnimationInstance.ResetBoneTransforms`.
8. Evaluate step and linear `Scale` clips into `AnimatedOffset.Scale`.
9. Add focused unit tests that a default imported bone keeps scale 1, a scaled rest bone produces a
   scaled `ModelBone.Transform.Matrix`, and a scale clip interpolates into `AnimatedOffset.Scale`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedBone.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedKeyframe.cs`
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/Panopticon.Simulation/Graphics/ModelBone.cs`
- `src/Panopticon.Simulation/Graphics/ModelBoneHierarchy.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClipType.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedModelMaterializerTests.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`

## Acceptance criteria

- [ ] Existing constructor call sites compile without passing scale.
- [ ] Rest scale defaults to 1 and preserves existing W3D, glTF, and C4c descriptors.
- [ ] A runtime scale clip sets `AnimatedOffset.Scale` and resets it to 1 when playback restarts.
- [ ] Invalid negative or non-finite rest scale is rejected before runtime materialization.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AnimationInstanceTests|FullyQualifiedName~ImportedModelMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not parse NIF scale keys or alter NIF importer behavior.
- Do not add vector or non-uniform scale.
- Do not change C2 named-target binding semantics.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4f-02` is done, change `WI-C4f-03` to `Ready`.
- Append a result line to `.exchange`.
