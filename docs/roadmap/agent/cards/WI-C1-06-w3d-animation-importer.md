# Task Card `WI-C1-06`: Extract W3D animation parsing

| Field | Value |
|---|---|
| Card ID | `WI-C1-06` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-05` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Move W3D key parsing and all channel conversion into `W3dAnimationImporter`, returning
`ImportedAnimation` while leaving the active runtime animation loader unchanged.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 4-7.
- `docs/roadmap/implementation/track-c-c1-import-abstraction.md` section
  `Current animation import path`.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandAnimationLoader.cs`.
- `src/Panopticon.Simulation/Graphics/Animation/W3DAnimation.cs` and both partial conversion files.
- Interchange animation types from `WI-C1-04`.

## Steps

1. Add `W3dAnimationImporter : IAnimationImporter` under `Content/Importing/W3d/`.
2. Move W3D file resolution, parsing, all five supported channel conversions, source-name
   validation, and ignored `UnknownBfme` handling into the importer.
3. Replace the existing `W3DAnimation` conversion bodies with a temporary compatibility wrapper
   that builds the same runtime asset from the imported value. Keep the active loader unchanged.
4. Return null for an invalid top-level key or missing top-level source. Throw contextual
   exceptions for malformed and unsupported source data.
5. Leave `OnDemandAnimationLoader`, INI references, and playback code unchanged.
6. Add focused importer tests for uncompressed, visibility, time-coded, adaptive-delta, and motion
   channels.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/W3d/W3dAnimationImporter.cs` (new)
- `src/Panopticon.Simulation/Graphics/Animation/W3DAnimation*.cs` (compatibility delegation)
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dAnimationImporterTests.cs` (new)

## Acceptance criteria

- [ ] `W3dAnimationImporter` covers all five audited W3D channel representations.
- [ ] Missing, malformed, and unsupported inputs follow the C1 failure contract.
- [ ] The active animation loader and runtime types are unchanged.
- [ ] W3D channel decoding has one implementation shared by the importer and compatibility path.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W3dAnimationImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add the animation materializer or rename `W3DAnimation`; `WI-C1-07` owns that cutover.
- Do not change INI, playback, model, or material loading.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-07` to `Ready`.
- Append a result line to `.exchange`.
