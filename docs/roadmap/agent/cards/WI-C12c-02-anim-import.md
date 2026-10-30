# Task Card `WI-C12c-02`: `AnimFile` reader, skeletons, and clip import

| Field | Value |
|---|---|
| Card ID | `WI-C12c-02` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12c-01` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A `Shogun2AnimationImporter` (id `ANIM`) imports version 1 clips as `ImportedAnimation` with bone-name TRS channels, and builds the `ImportedSkeleton` for a skeleton type from a clip bone table.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 5, 9, 11, and the results of `WI-C12c-01`.
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs`, `ImportedAnimation.cs`, `ImportedAnimationChannel.cs`, `ImportedAnimationTarget.cs`, `ImportedSkeleton.cs`, `ImportedBone.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/` animation importer (precedent).

## Steps

1. Add `AnimFile.Read(stream)` to `src/Panopticon.FileFormats.Shogun2` with the header, bone table, and frame layout from `WI-C12c-01`; unsupported files throw with path.
2. Add `Shogun2AnimationImporter : IAnimationImporter` emitting `ImportedAnimationTarget.CreateBoneName` channels at the clip frame rate, and register `ANIM` in `ExternalFormatFactory`.
3. Add skeleton import from a clip bone table (names, parents, frame 0 as bind pose); `Rmv2ModelImporter.ImportSkeleton` delegates to it for a named skeleton clip.
4. Add synthetic clip tests and an Interactive test importing every version 1 clip with counts by bone count and failure reason.

## Files likely to change

- `src/Panopticon.FileFormats.Shogun2/AnimFile.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Shogun2/Shogun2AnimationImporter.cs` (new), `Rmv2ModelImporter.cs`, `ExternalFormatFactory.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/`

## Acceptance criteria

- [ ] The four proof clips import with 48 (man) or 40 (horse) bone targets and no missing targets against their own skeleton.
- [ ] Corpus counts are recorded in deep dive section 5.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Anim"
```

## Out of scope / guardrails

- Skin binding (`WI-C12c-03`).
- Rider and mount pairing (C9d).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12c-03` to `Ready` if `WI-C12b-04` is `Done`.
- Set `WI-C12c-05` to `Ready` if `WI-C12a-02` is `Done`.
- Append a result line to `.exchange`.
