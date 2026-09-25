# Task Card `WI-C4-04`: Add animation interpolation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-04` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C2-06` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Carry linear or step interpolation from imported channels through C2 binding into playback,
without changing W3D channel results.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` section 7.
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimationChannel.cs` (new from `WI-C2-02`).
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClip.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dAnimationImporterTests.cs`.
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`.
- `test/Panopticon.Simulation.Tests/Graphics/Animation/ModelAnimationTests.cs`.

## Steps

1. Add imported-layer and runtime-layer interpolation enums with only `Linear` and `Step`.
2. Add immutable interpolation to `ImportedAnimationChannel`, defaulting every compatibility
   constructor to `Linear`.
3. Map interpolation through `ImportedAnimationMaterializer`, `ModelAnimationChannel`, binding,
   and `AnimationClip`.
4. In `AnimationInstance`, keep the existing scalar lerp and quaternion slerp for `Linear`.
   Evaluate `Step` by applying the previous key value until the next key time.
5. Preserve visibility's existing held-value behavior and reject unsupported enum values at
   construction or materialization.
6. Add midpoint and exact-key tests for forward and reverse step playback, linear parity tests,
   and W3D assertions proving every imported channel is `Linear`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationInterpolation.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs`
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInterpolation.cs` (new)
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimationChannel.cs` (new from `WI-C2-02`)
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClip.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dAnimationImporterTests.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Animation/ModelAnimationTests.cs`

## Acceptance criteria

- [ ] Imported, unbound runtime, bound, and playback channels retain interpolation.
- [ ] `Step` holds the previous scalar or quaternion value between keys.
- [ ] Exact key times select the authored key in forward and reverse playback.
- [ ] Existing W3D channels remain linear with unchanged fixed-time values.
- [ ] Visibility playback remains unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AnimationInstanceTests|FullyQualifiedName~ModelAnimationTests|FullyQualifiedName~W3dAnimationImporterTests|FullyQualifiedName~ImportedAssetTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add cubic interpolation, tangents, resampling, or glTF parsing.
- Do not change channel targets, binding cache identity, animation duration, or loop policy.
- Do not represent interpolation with SharpGLTF enums.
- Do not change W3D keyframe generation.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-01`, `WI-C4-02`, and `WI-C4-05` are done, change `WI-C4-08` to `Ready`.
- Append a result line to `.exchange`.
