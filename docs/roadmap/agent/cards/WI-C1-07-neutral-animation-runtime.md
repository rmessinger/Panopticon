# Task Card `WI-C1-07`: Materialize neutral animations

| Field | Value |
|---|---|
| Card ID | `WI-C1-07` |
| Roadmap phase | [Track C · C1](../../track-c-external-assets.md): Format-agnostic import abstraction |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C1-06` |
| Diagrams | [C1 architecture diagrams](../../deep-dives/c1-import-abstraction.md#9-architecture-diagrams) |

## Goal

Materialize `ImportedAnimation` as a format-neutral `ModelAnimation` and adapt the legacy loader
to the renamed runtime asset without switching importer ownership yet.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 3, 4, 6, and 7.
- `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs`.
- `src/OpenSage.Game/Graphics/Animation/W3DAnimation.cs`.
- `src/OpenSage.Game/Graphics/Animation/AnimationClip.cs`, `Keyframe.cs`, and
  `AnimationInstance.cs:13-55`.
- `src/OpenSage.Game/Data/Ini/IniParser.cs:716-727`.
- `src/OpenSage.Game/Logic/Object/AnimationState.cs:110-140`.
- Repository search results for `W3DAnimation`.

## Steps

1. Add `ImportedAnimationMaterializer` under `Content/Loaders/`.
2. Rename the runtime asset `W3DAnimation` to `ModelAnimation`; move no W3D parsing code into the
   renamed type.
3. Update `AnimationInstance`, asset-store collections, INI lazy references, model-condition data,
   and tests to use `ModelAnimation`.
4. Keep `OnDemandAnimationLoader` on the legacy compatibility entry point so `WI-C1-12` can compare
   that path with direct importer plus materializer output.
5. Preserve animation names, duration, channels, modes, flags, interpolation, and visibility
   assertions.

## Files likely to change

- `src/OpenSage.Game/Content/Loaders/ImportedAnimationMaterializer.cs` (new)
- `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs` (type rename only)
- `src/OpenSage.Game/Graphics/Animation/W3DAnimation*.cs` (rename/compatibility wrapper)
- Typed `W3DAnimation` references reported by repository search
- `src/OpenSage.Game.Tests/Graphics/Animation/`

## Acceptance criteria

- [ ] No runtime animation type or playback class references a W3D parser type.
- [ ] INI references resolve to `ModelAnimation`.
- [ ] The legacy animation loader remains callable for parity comparison.
- [ ] Existing animation playback assertions remain unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AnimationInstanceTests|FullyQualifiedName~W3dAnimationImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not modify model, skeleton, mesh, or material loading.
- Do not add bone-name remapping or change INI syntax.
- Do not alter animation modes, flags, interpolation, or visibility.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C1-12` to `Ready` once `WI-C1-11` is also `Done`.
- Append a result line to `.exchange`.
