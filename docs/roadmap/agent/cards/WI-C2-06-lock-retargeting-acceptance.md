# Task Card `WI-C2-06`: Lock retargeting acceptance

| Field | Value |
|---|---|
| Card ID | `WI-C2-06` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C2-05` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Lock the complete C2 binding contract with synthetic gameplay integration and W3D direct-index
parity tests.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 5-7.
- C2 tests added by `WI-C2-01..05`.
- `src/OpenSage.Game.Tests/Graphics/Animation/AnimationInstanceTests.cs`.
- `src/OpenSage.Game.Tests/Logic/Draw/W3dModelDrawTests.cs`.
- `src/OpenSage.Game.Tests/GameFixture.cs`.
- C1 W3D import parity tests.

## Steps

1. Build a synthetic model whose runtime hierarchy order differs from its named animation source
   order.
2. Use the installed-game `GameFixture` to create the graphics-backed model instance needed by
   the test, then activate the animation through an existing gameplay `AnimationState` and
   `W3dModelDraw`.
3. Assert translation, quaternion, and visibility values at fixed times on the intended bones.
4. Cover missing and duplicate named targets, repeated binding, and out-of-range direct targets in
   the same focused suite.
5. Re-run C1 W3D descriptor and fixed-time playback parity without changing expected values.
6. Mark C2 complete only when the full deep-dive acceptance list is represented.

## Files likely to change

- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationBindingTests.cs` (new)
- `src/OpenSage.Game.Tests/Logic/Draw/W3dModelDrawTests.cs`
- C1 W3D parity test files only if a shared fixture is required

## Acceptance criteria

- [ ] A reordered named animation drives the intended target bones through gameplay state.
- [ ] The graphics-backed integration test is marked `[GameFact(SageGame.CncGenerals)]`.
- [ ] Translation, quaternion, visibility, timing, and channel order remain intact.
- [ ] Every failure and cache behavior in deep-dive section 7 has a focused assertion.
- [ ] Existing W3D direct-index descriptors and fixed-time playback remain unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ModelAnimationBindingTests|FullyQualifiedName~AnimationTests|FullyQualifiedName~W3dImportParityTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a glTF importer or external-format fixture; C4 owns that proof.
- Do not update parity expectations to accept unexplained W3D drift.
- Do not add alias profiles or humanoid retargeting.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C2 Status to `Done` and tick C2 in the implementation checklist.
- If done, change `WI-C4-04` to `Ready`.
- Append a result line to `.exchange`, including the synthetic hierarchy and channel matrix.
