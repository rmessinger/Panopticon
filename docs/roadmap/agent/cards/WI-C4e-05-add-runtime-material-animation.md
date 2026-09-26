# Task Card `WI-C4e-05`: Add runtime material animation

| Field | Value |
|---|---|
| Card ID | `WI-C4e-05` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C4e-03` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Evaluate bound property-animation channels into per-instance sub-object and material-slot state, and feed that state to rendering without mutating shared materials.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 6, 7, and 8.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs:280-365`.
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs` and `BoundModelAnimation.cs`.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs:126-137` and `281-321`.
- `src/Panopticon.Simulation/Graphics/ModelMeshInstance.cs:58-117`.
- `src/Panopticon.Simulation/Graphics/Shaders/MeshShaderResources.cs:17-60`.
- `src/Panopticon.Simulation/Graphics/Shaders/FixedFunctionShaderResources.cs:235-337`.

## Steps

1. Extend binding so animations with sub-object or material targets bind against a `Model` and validate all target references before playback.
2. Add per-instance state for sub-object visibility and material-slot overrides: ambient, diffuse, specular, emissive, alpha, UV offset, UV scale, and UV rotation.
3. Extend `AnimationInstance` evaluation to sample float, bool, and `Vector3` property clips into that state. Bone clip behavior must remain unchanged.
4. Extend mesh-part rendering resources so each `ModelMeshPartInstance` can bind default property state or sampled overrides without changing shared `MaterialPass` objects.
5. Make alpha below one select the transparent queue for the affected draw while preserving existing static blend behavior.
6. Add non-graphics unit tests for binding, sampling, default state reset, alpha queue selection decisions, and unchanged W3D direct-index playback.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs`
- `src/Panopticon.Simulation/Graphics/Animation/BoundModelAnimation.cs`
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`
- `src/Panopticon.Simulation/Graphics/ModelMeshInstance.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/MeshShaderResources.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/FixedFunctionShaderResources.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Animation/ModelPropertyAnimationTests.cs` (new)

## Acceptance criteria

- [ ] Property clips bind by model identity and fail before playback when sub-object or material targets are missing.
- [ ] Two `ModelInstance` objects using the same `Model` can hold different material animation samples.
- [ ] Shared `Material`, `MaterialPass`, and fixed-function material-cache buffers are not mutated by playback.
- [ ] Existing transform, visibility, and W3D animation tests pass unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelPropertyAnimationTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add NIF parsing or DDF lookup in this card.
- Do not implement PBR extension animation.
- Do not change C5 UI.
- Do not alter existing W3D tread behavior except to route it through the new default-compatible state.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4e-04` is done, change `WI-C4e-06` to `Ready`.
- Append a result line to `.exchange`.
