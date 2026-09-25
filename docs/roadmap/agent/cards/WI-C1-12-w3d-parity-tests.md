# Task Card `WI-C1-12`: Lock W3D import parity

| Field | Value |
|---|---|
| Card ID | `WI-C1-12` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C1-07`, `WI-C1-11` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Prove valid W3D models and animations retain their observable runtime description through the
import seam while legacy loaders remain active for side-by-side comparison.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 6-8.
- `docs/roadmap/implementation/track-c-c1-import-abstraction.md`: both audit traces.
- New C1 importer and materializer tests.
- `test/Panopticon.Simulation.Tests/Data/W3d/W3dFileTests.cs`: installed-file test pattern.
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`: fixed-time playback.
- Legacy model and animation loader entry points.

## Steps

1. Load representative Generals static, skinned, hierarchy-referenced, material, named
   shader-property, and animation assets through the legacy path.
2. Import and materialize the same assets directly through the new C1 path.
3. Compare every parity field listed in deep-dive section 7, including fixed-time animation bone
   transforms and visibility.
4. Commit stable expected descriptors. Use `[GameFact(SageGame.CncGenerals)]` for installed assets
   and keep synthetic materializer tests non-interactive.
5. If a valid shipped asset differs, preserve both descriptors and stop without changing expected
   values.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/W3dImportParityTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Graphics/Animation/AnimationInstanceTests.cs`

## Acceptance criteria

- [ ] Representative W3D assets match every descriptor field in the C1 parity contract.
- [ ] A gameplay animation state produces the same fixed-time bone transforms and visibility.
- [ ] Legacy and C1 paths remain independently callable for every comparison.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~W3dImportParityTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not switch loader ownership or remove legacy constructors.
- Do not fix unrelated W3D parser defects.
- Do not add glTF, retargeting, PBR, or preview-tool behavior.
- If valid shipped assets differ, mark the card `Blocked`; do not update expected values to accept
  unexplained drift.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C1-13` to `Ready`.
- Append a result line to `.exchange`, including the parity asset set.
