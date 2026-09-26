# Task Card `WI-C7b-03`: Decode DDS and derive C3 texture images

| Field | Value |
|---|---|
| Card ID | `WI-C7b-03` |
| Roadmap phase | [Track C · C7b](../../track-c-external-assets.md): StarCraft II static M3 models |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7b-01` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Add a DXT1/DXT5 CPU decoder and `M3TextureConverter`, which produces embedded RGBA PNG images per the section 8.3 channel rules.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 8.2 and 8.3.
- `src/Panopticon.Simulation/Data/Dds/DdsFile.cs`: header parsing to reuse if it exposes the top mip bytes. Otherwise parse the 128-byte header locally.
- `src/Panopticon.Simulation/Content/Importing/ImportedImageSource.cs` (`CreateEmbeddedBytes`).
- `src/Panopticon.Simulation/Content/Loaders/ImportedImageDecoder.cs`: PNG decode path the embedded images use.

## Steps

1. Add `DxtDecoder` in `Panopticon.FileFormats.M3`: decode BC1 (with 3-color and 1-bit alpha mode) and BC3 blocks to RGBA8 for any width and height, clipping partial blocks.
2. Add `M3TextureConverter` with rules `BaseColor`, `Normal`, `Roughness(specularity)`, and `Emissive(channelSelector)`, as in section 8.3. The input is a DDS byte array. The output is `ImportedImageSource.CreateEmbeddedBytes(png, "image/png", <path>#<rule>)`.
3. Encode PNG with ImageSharp (already referenced by `Panopticon.Simulation`). Cache results per importer instance by path and rule.
4. Reject non-DXT1/DXT5 four-CCs with `NotSupportedException` naming the four-CC.
5. Tests: hand-built 4x4 BC1 and BC3 blocks against known colors, the normal rule producing unit length within 1e-2 for a DXT5nm texel, `r0 = 0.395` for specularity 80, and channel selectors 0 to 5.

## Files likely to change

- `src/Panopticon.FileFormats.M3/DxtDecoder.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/M3/M3TextureConverter.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/M3/DxtDecoderTests.cs`, `test/Panopticon.Simulation.Tests/Content/Importing/M3TextureConverterTests.cs` (new)

## Acceptance criteria

- [ ] BC1 and BC3 test blocks decode to the expected RGBA values exactly.
- [ ] Normal rule output is unit length within 1e-2, and roughness matches the formula.
- [ ] `ImportedImageDecoder` still rejects DDS unchanged.
- [ ] Non-DXT four-CCs fail naming the four-CC.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not modify C3 (`ImportedImageDecoder`, `ImportedTextureMaterializer`) or the C4b installer.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C7b-02` is done, change `WI-C7b-04` to `Ready`.
- Append a result line to `.exchange`.
