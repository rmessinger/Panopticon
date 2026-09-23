# Task Card `WI-C4e-01`: Decode EE2 property controllers

| Field | Value |
|---|---|
| Card ID | `WI-C4e-01` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4c-01` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Extend `OpenSage.FileFormats.Nif` to decode EE2 NIF 10.1 visibility, texture-transform, material-color, alpha, and UV data blocks with exact stream consumption.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 3, 4, 5, and 8.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 3, 4.2, and 7.
- `src/OpenSage.FileFormats.Nif/NifFile.cs:21-55`, `118-155`, and `157-312`.
- The KF key types and key-group helpers added by `WI-C4c-01`.
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`.

## Steps

1. Add block types for `NiVisController`, `NiTextureTransformController`, `NiMaterialColorController`, `NiAlphaController`, `NiVisData`, `NiBoolData`, `NiFloatData`, `NiPosData`, and `NiUVData`.
2. Factor the C4c time-controller prefix into shared parser code and reuse the C4c key-group decoder.
3. Decode `NiVisData` as count plus time and byte pairs. Decode `NiUVData` as four float key groups.
4. Register the new block types in `NifFile.ReadBlock`.
5. Validate references by controller family: visibility data, float data, position data, and UV data must match the controller type.
6. Add synthetic tests covering each block, exact consumption, wrong-type references, all texture-transform operation enum values, all material-color enum values, `NiVisData`, `NiBoolData`, and `NiUVData`.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.FileFormats.Nif/Blocks/NifPropertyControllers.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifAnimationDataBlocks.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifKeys.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifPropertyControllerTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic streams for every C4e block parse to exact end-of-file.
- [ ] Controller data references reject wrong block types with `InvalidDataException`.
- [ ] `NiVisData` byte keys and `NiBoolData` key groups preserve values exactly.
- [ ] TBC and unsupported key types follow the C4c failure contract.
- [ ] Existing C4a, C4b, and C4c parser tests pass unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifPropertyControllerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not import animations or touch `OpenSage.Game` conversion code.
- Do not add NIF 10.0.1.0 support.
- Do not implement `NiFlipController` or morph animation.
- Do not commit or embed EE2 asset bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4e-02` is done, change `WI-C4e-03` to `Ready`.
- If done and `WI-C4e-02` and `WI-C4e-03` are done, change `WI-C4e-04` to `Ready`.
- Append a result line to `.exchange`.
