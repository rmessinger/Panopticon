# Task Card `WI-C7c-01`: Add per-axis scale and absolute local clips

| Field | Value |
|---|---|
| Card ID | `WI-C7c-01` |
| Roadmap phase | [Track C · C7c](../../track-c-external-assets.md): StarCraft II skinned M3 animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4f-01`, `WI-C4f-04` |
| Diagrams | [C7c architecture diagrams](../../deep-dives/c7c-sc2-skinned-animation.md#13-architecture-diagrams) |

## Goal

Widen the C4f scale support in C1 and the runtime to per-axis bind scale and `ScaleX/Y/Z` channels, and add `AbsoluteLocal` animation composition, leaving every existing format byte-identical.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c7c-sc2-skinned-animation.md` sections 7 and 10.
- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 5 and 6 (the C4f surface being widened).
- `src/Panopticon.Simulation/Content/Importing/ImportedBone.cs`, `ImportedAnimation.cs`, `ImportedAnimationChannel.cs`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Graphics/ModelBone.cs`, `ModelBoneInstance.cs`, `Graphics/Animation/AnimationInstance.cs`, `AnimationClipType.cs`, `ModelAnimation.cs`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs:238-239`.

## Steps

1. `ImportedBone.BindScale` becomes `Vector3`. Keep the C4f float constructor as an overload mapping to `(s, s, s)`. Validate each axis finite and positive.
2. Add `ScaleX`, `ScaleY`, `ScaleZ` to `ImportedAnimationChannelType` and `AnimationClipType` (float keys), and map them in `ImportedAnimationMaterializer`.
3. Add `ImportedAnimationComposition { RestRelative, AbsoluteLocal }`, with `ImportedAnimation.Composition` defaulting to `RestRelative`. Mirror it on `ModelAnimation`.
4. `ModelBone`: add `Vector3 BindScale` and `RestMatrix = CreateScale(BindScale) * CreateFromQuaternion(rotation) * CreateTranslation(translation)`. `ModelBoneInstance.Matrix` uses `RestMatrix`. Update `StandaloneModelCapture` to use `RestMatrix`.
5. `ModelBoneInstance`: add absolute local state (translation, rotation, `Vector3` scale, active flag). `Matrix` returns the absolute `S * R * T` when active.
6. `AnimationInstance`: for absolute animations, write channel values to the absolute state, seeding untargeted components from the bone's bind values, and set the flag. Reset clears the flag.
7. Tests: a two-bone synthetic model with non-uniform bind scale and an absolute clip rotating the child, checking the world matrix at t = 0 and t = end. A rest-relative glTF or W3D test must produce the same matrices as before.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedBone.cs`, `ImportedAnimation.cs`, `ImportedAnimationChannel.cs`, `ImportedAnimationComposition.cs` (new)
- `src/Panopticon.Simulation/Content/Loaders/ImportedAnimationMaterializer.cs`
- `src/Panopticon.Simulation/Graphics/ModelBone.cs`, `ModelBoneInstance.cs`, `ModelBoneHierarchy` construction site
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs`, `AnimationClipType.cs`, `ModelAnimation.cs`
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedAssetTests.cs`, a new `AbsoluteAnimationTests.cs`

## Acceptance criteria

- [ ] Non-uniform bind scale and absolute clips reproduce the expected world matrices within 1e-5.
- [ ] `Transform.Scale` remains a float, and game-object code is untouched.
- [ ] All existing W3D, glTF, and NIF tests pass unchanged, and golden baselines are byte-identical (follow the determinism stop-and-report rule on any change).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No M3 code in this card.
- No inverse-transpose normals (deep dive section 7.4 decision).
- If C4f landed with a different surface than its deep dive, stop and reconcile the plan first.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C7b-05` is done, change `WI-C7c-02` to `Ready`.
- Append a result line to `.exchange`.
