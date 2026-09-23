# Task Card `WI-C4f-04`: Validate scale normals and bounds

| Field | Value |
|---|---|
| Card ID | `WI-C4f-04` |
| Roadmap phase | [Track C · C4f](../../track-c-external-assets.md): EE2 scale animation |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4f-03` |
| Diagrams | [C4f architecture diagrams](../../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams) |

## Goal

Lock the runtime effects of uniform rest and animated scale on mesh normals, render bounds, and
skinning matrix composition without adding C4d influence widening.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4f-ee2-scale-animation.md` sections 8 and 10.
- `src/OpenSage.Game/Graphics/ModelMesh.cs:51-109`: parent-bone world matrix and render bounds.
- `src/OpenSage.Game/Graphics/ModelInstance.cs:246-261`: skinning matrix upload and composition.
- `src/OpenSage.Game/Assets/Shaders/Common.h`: current `TransformNormal` helper.
- `src/OpenSage.Game/Assets/Shaders/FixedFunction.vert` and `FixedFunction.frag`: EE2 proof
  shader path.
- `src/OpenSage.Game.Tests/Graphics/ModelInstanceTests.cs`: runtime transform tests, if present.

## Steps

1. Add a runtime test that a scaled rigid parent bone expands a mesh bounding box through
   `AbsoluteBoneTransforms`.
2. Add or update shader helper behavior so transformed mesh normals are normalized after uniform
   scale. Keep zero-length input guarded from `NaN` output.
3. Add a graphics-free test or shader utility test, if available, that a uniform scale does not
   change final normal direction.
4. Add a skinning matrix unit test proving rest scale participates in `inverseBind * relative`.
5. Do not change vertex influence counts or C4d skin import policy.
6. Run the C4c proof tests to ensure unscaled models remain unchanged.

## Files likely to change

- `src/OpenSage.Game/Assets/Shaders/Common.h`
- `src/OpenSage.Game/Assets/Shaders/FixedFunction.vert`
- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.vert`
- `src/OpenSage.Game/Graphics/ModelInstance.cs`
- `src/OpenSage.Game.Tests/Graphics/ModelInstanceTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImporterTests.cs`

## Acceptance criteria

- [ ] Uniform scale changes rigid render bounds by the expected factor.
- [ ] Uniform scale does not change normalized normal direction in the fixed-function proof path.
- [ ] Skinning matrix composition includes rest scale without changing influence count limits.
- [ ] Existing C4c unit-scale proof assertions remain green.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelInstanceTests|FullyQualifiedName~NifAnimationImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement C4d skin import or influence widening.
- Do not change material lighting formulas beyond normal normalization needed for scale.
- Do not add EE2 installed-asset tests. Those belong to `WI-C4f-05`.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4f-05` to `Ready`.
- Append a result line to `.exchange`.
