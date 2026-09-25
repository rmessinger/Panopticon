# Task Card `WI-C2-05`: Route gameplay animation binding

| Field | Value |
|---|---|
| Card ID | `WI-C2-05` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C2-04` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Bind animations through the active model instance before playback while preserving existing INI
selection, modes, flags, and speed behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 2, 4, and 6.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`.
- `src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs:210-240`.
- `src/Panopticon.Simulation/Logic/Object/AnimationState.cs`.
- `test/Panopticon.Simulation.Tests/GameFixture.cs`.
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`.
- `test/Panopticon.Simulation.Tests/Logic/Draw/W3dModelDrawTests.cs`.

## Steps

1. Add `ModelInstance.CreateAnimationInstance` with the existing mode, flags, game-object, and
   random inputs.
2. Bind the selected `ModelAnimation` to `Model.BoneHierarchy` inside that method.
3. Change `AnimationInstance` to accept `BoundModelAnimation` and evaluate only its target-indexed
   clips while retaining the animation name and duration used by playback and diagnostics.
4. Route `W3dModelDraw.SetActiveAnimationState` through the model-instance method.
5. Remove the temporary unbound direct-clip compatibility surface introduced by `WI-C2-02`.
6. Update focused tests without changing playback expectations.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/ModelInstance.cs`
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`
- `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs`
- `src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`
- `test/Panopticon.Simulation.Tests/Logic/Draw/W3dModelDrawTests.cs`

## Acceptance criteria

- [ ] Gameplay animation activation binds against the active model hierarchy.
- [ ] `AnimationInstance` performs no source-format branch or bone-name lookup.
- [ ] Existing animation modes, flags, random start, speed, interpolation, and visibility remain
      unchanged.
- [ ] No production caller constructs an animation instance from an unbound animation.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~AnimationInstanceTests|FullyQualifiedName~W3dModelDrawTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add INI fields or alter model-condition selection.
- Do not modify weapon, particle, muzzle-flash, or attachment bone lookup.
- Do not change playback interpolation or animation-state semantics.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C2-06` to `Ready`.
- Append a result line to `.exchange`.
