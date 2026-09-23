# Task Card `WI-C4g-01`: Decode NIF morph blocks

| Field | Value |
|---|---|
| Card ID | `WI-C4g-01` |
| Roadmap phase | [Track C · C4g](../../track-c-external-assets.md): EE2 morph animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-06` |
| Diagrams | [C4g architecture diagrams](../../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams) |

## Goal

Extend `OpenSage.FileFormats.Nif` to decode NIF 10.1 `NiGeomMorpherController` and
`NiMorphData` blocks with exact stream consumption and validated references.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4g-ee2-morph-animation.md` sections 1, 3, 4, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 3 and 4.2 for controller and
  float-key precedent.
- `src/OpenSage.FileFormats.Nif/NifFile.cs:21-55`, `118-155`, and `157-312`.
- `src/OpenSage.FileFormats.Nif/NifBinaryReader.cs`.
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`.

## Steps

1. Change Status to `In progress`.
2. Add `NifGeomMorpherController` with the `NiTimeController` fields, `MorpherFlags` as `ushort`,
   `DataReference`, and `AlwaysUpdate`.
3. Add `NifMorphData` with morph count, vertex count, relative-target flag, and ordered morphs.
4. Add a small `NifMorph` support type with a float `NifKeyGroup` and `Vector3` vectors.
5. Reuse the C4c key-group support for linear and quadratic float keys. Reject TBC and unknown
   key types for this card.
6. Register the new blocks in `NifFile.ReadBlock`.
7. Validate that a morph controller's data reference points to `NiMorphData`.
8. Add synthetic parser tests for the `bld_farm_yam.kf` shape: 2 morphs, 720 vertices,
   `RelativeTargets = 1`, morph 0 linear zero keys, and morph 1 linear 0 to 1 keys.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.FileFormats.Nif/Blocks/NifGeomMorpherController.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifMorphData.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifKeys.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifMorphTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic morph streams parse to exact end-of-file.
- [ ] The decoded controller exposes flags, start, stop, target reference, morpher flags, data
      reference, and always-update.
- [ ] The decoded morph data exposes target count, vertex count, relative flag, keys, and vectors.
- [ ] A bad controller data reference throws `InvalidDataException`.
- [ ] TBC or unknown morph key types throw `NotSupportedException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifMorphTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add NIF 10.0.1.0 body parsing.
- Do not create runtime morph target types.
- Do not commit or embed extracted EE2 bytes.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4g-02` is done, change `WI-C4g-03` to `Ready`.
- Append a result line to `.exchange`.
