# Task Card `WI-C10a-03`: DDT reader and image-decoder branch

| Field | Value |
|---|---|
| Card ID | `WI-C10a-03` |
| Roadmap phase | [Track C · C10a](../../track-c-external-assets.md): AoE3 DE archives and textures |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-C10a-01` for the interactive check only |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`.ddt` (`RTS3`) textures decode through `ImportedImageDecoder`, so C3 PBR bindings can reference DDT virtual paths directly.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 4 and 7 (DDT decision).
- `src/Panopticon.Simulation/Content/Loaders/ImportedImageDecoder.cs`, `DecodedImportedImage.cs`, `ImportedTextureMaterializer.cs`.
- `src/Panopticon.Simulation/Data/Dds/DdsBlockDecoder.cs`, `DdsImageFormat.cs`, `src/Panopticon.Simulation/Utilities/TextureMipMapData.cs`.

## Steps

1. Add `DdtFile.Read(stream)` in `Panopticon.FileFormats.Bar`: `RTS3`, usage, alpha, format, mip count, width, height, mip offset and size pairs. Formats 1 BGRA, 4 BC1, 5 BC1 alpha, 7 R8, 8 BC2, 9 BC3; others throw with the format code.
2. In `ImportedImageDecoder`, detect `RTS3` magic and decode the base mip: BC formats through `DdsBlockDecoder.DecodeBaseMip`, BGRA swizzled to RGBA, R8 replicated to RGB with alpha 255.
3. Keep mip regeneration in the materializer (`generateMipmaps`); do not upload stored mips.
4. Install-free tests with synthetic BC1, BC3, BGRA, and R8 DDTs; Interactive test decodes the four musketeer `matA` textures and records format and size.

## Files likely to change

- `src/Panopticon.FileFormats.Bar/DdtFile.cs` (new)
- `src/Panopticon.Simulation/Content/Loaders/ImportedImageDecoder.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/DdtDecodeTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic DDTs decode to the expected RGBA pixels.
- [ ] Unknown format codes fail with path and code.
- [ ] PNG, JPEG, TGA, and the DDS rejection path are unchanged (existing `ImportedImageDecoderTests` pass).
- [ ] Interactive: musketeer BaseColor, Normals, Masks, and Details decode; formats and sizes recorded in the deep dive.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- GPU block-compressed upload of DDT mips.
- Material mapping (`WI-C10b-03`).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10b-03` to `Ready` if `WI-C10b-02` and `WI-C10a-02` are done.
- Tick C10a in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
