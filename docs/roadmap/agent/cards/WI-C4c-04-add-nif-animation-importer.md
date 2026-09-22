# Task Card `WI-C4c-04`: Add the NIF KF animation importer

| Field | Value |
|---|---|
| Card ID | `WI-C4c-04` |
| Roadmap phase | [Track C · C4c](../../track-c-external-assets.md): EE2 rigid KF animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-02`, `WI-C4c-03` |
| Diagrams | [C4c architecture diagrams](../../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams) |

## Goal

Implement `NifAnimationImporter` so that a model-qualified KF key becomes a CPU-only
`ImportedAnimation` with bone-name, rest-relative C1 channels.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4, 6, and 7.
- `src/OpenSage.Game/Content/Importing/IAnimationImporter.cs` and `AssetImportContext.cs`.
- `src/OpenSage.Game/Content/Importing/Gltf/GltfAnimationImporter.cs` (key form and null rules)
  and `GltfAnimationConverter.cs:121-132,211-229` (identity scale and offset rule).
- The skeleton entry point from `WI-C4c-02` and the samplers from `WI-C4c-03`.

## Steps

1. Parse `<model-key>#kf=<archive-relative-kf-path>`. Return null for a malformed key or a
   missing `cache/nifcache/<model-key>.nifcache` or KF entry.
2. Read the KF with `NifFile.Read`. Require block 0 to be `NifControllerSequence` and apply the
   section 4.3 text-key and timing rules.
3. Validate controllers: flags `0x2C`, frequency 1, phase 0, next and target refs `-1`.
4. Build the model skeleton through the `WI-C4c-02` entry point. Resolve each target
   case-insensitively and reject duplicate or missing targets.
5. Validate scale (section 4.5) and accumulation-root constancy (section 4.4).
6. Emit channels:
   - quaternion keys as one `Quaternion` channel;
   - XYZ rotation through `NifRotationSampler`;
   - linear translation as `TranslationX/Y/Z`.
   Convert samples and rests to the engine frame, then apply the C4 offset rule. Use
   `ImportedAnimationTarget.CreateBoneName` and `Linear` interpolation.
7. Name the animation after the KF file stem.
8. Add synthetic tests for:
   - offsets that reconstruct authored samples (`offset * rest`);
   - accumulation-root rejection;
   - non-unit scale rejection;
   - missing-target rejection;
   - flag rejection;
   - duration from text keys.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationImporter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationKey.cs` (new)
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImporterTests.cs` (new)

## Acceptance criteria

- [ ] Every section 7 condition maps to its stated result, with asset context in messages.
- [ ] Reconstructed `offset * rest` equals the converted authored sample within 1e-4.
- [ ] The importer performs no GPU, `AssetStore`, or ZIP API work, and no NIF parser type
      reaches `OpenSage.Game/Graphics/`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifAnimationImporterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not parse DDF files, add root motion, or register NIF in game definitions or the C5
  external catalog.
- Do not change the glTF converter. Replicate its offset formula.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4c-05` to `Ready`.
- Append a result line to `.exchange`.
