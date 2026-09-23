# Task Card `WI-C4i-05`: Lock legacy family rejections

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-05` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4i-02`, `WI-C4c-04` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Add focused tests and diagnostics for legacy controller families that C4i inventories but leaves to
C4e, C4g, and C4d.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 3.1, 4.3, 6, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` section 7.
- KF block dispatch and converter files from `WI-C4i-02` and `WI-C4i-03`.
- Existing C4c unsupported-controller tests.

## Steps

1. Add synthetic or installed fixtures that encounter `NiMaterialColorController`, `NiVisController`,
   `NiUVController`, and `NiGeomMorpherController` in 10.0.1.0 files.
2. Assert each failure message names the unsupported block and owning phase: C4e for material,
   visibility, and UV, C4g for morph.
3. Add a fixture for a DDF-bound skinned target model and assert it remains blocked on C4d rather
   than silently importing as rigid.
4. Add a fixture for each 4.2.2.0 file name and assert the importer rejects the header version
   before attempting inline block parsing.
5. Keep all tests free of copied EE2 bytes. Use installed archives or synthetic streams.

## Files likely to change

- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImportTests.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifKeyframeTests.cs`
- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.Game/Content/Importing/Nif/NifAnimationImporter.cs`

## Acceptance criteria

- [ ] Unsupported legacy property, visibility, UV, and morph controllers fail with owning phases.
- [ ] Skinned legacy target tests fail with a C4d-oriented diagnostic until C4d implements skinning.
- [ ] NetImmerse 4.2.2.0 files fail with the explicit C4i rejection.
- [ ] No unsupported controller family is silently skipped.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifAnimationImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement C4e, C4g, or C4d features.
- Do not parse 4.2.2.0 inline block bodies beyond the rejection needed for diagnostics.
- Do not change DDF data.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4i-04` is done, change `WI-C4i-06` to `Ready`.
- Append a result line to `.exchange`.
