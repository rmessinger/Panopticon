# Task Card `WI-C4b-01`: Decode NiPixelData texcache containers

| Field | Value |
|---|---|
| Card ID | `WI-C4b-01` |
| Roadmap phase | [Track C · C4b](../../track-c-external-assets.md): EE2 cooked texture decoding |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-02`, `WI-C4a-05` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Decode `cache/texcache/*.texcache` containers (NIF 10.1.0.0 with one `NiPixelData`
block) into validated CPU mip data, preserving DXT blocks without transcoding.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` section 6 and section 9.1.1.
- `src/OpenSage.FileFormats.Nif/NifFile.cs` dispatch and footer validation.
- Niftools `nif.xml` definitions for `NiPixelFormat` and `NiPixelData`.

## Steps

1. Add an immutable `NifPixelData` block type and dispatch case. Wire layout
   (validated byte-exact against the three target textures and a corpus census):
   leading `u32 0`; format `u32` (4 = DXT1, 5 = DXT3, 6 = DXT5); RGBA masks
   `u32 x4`; bits-per-pixel `u32`; format repeat `u32` (must equal format);
   two zero `u32`s; palette ref (must be -1: palettes are out of scope);
   mip count `u32`; bytes-per-pixel `u32`; mip descriptors
   (`u32` width, height, offset); total pixel-byte count `u32`; pixel bytes.
2. Validate: accepted formats only (others throw `NotSupportedException`),
   offsets contiguous from zero, per-mip block sizes
   (`ceil(w/4) * ceil(h/4) * 8` for DXT1, `* 16` for DXT3/DXT5), and the mip
   chain summing to the declared total. `NifFile.Read` already enforces the
   footer and exact end-of-file.
3. Expose per-mip `(width, height, bytes)` accessors; no graphics, GPU, or
   asset-store work.
4. Synthetic tests build texcache bytes in code and cover the accepted layout,
   each validation failure, and the exact-bytes round trip.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/Blocks/NifPixelData.cs` (new)
- `src/OpenSage.FileFormats.Nif/NifFile.cs`
- `src/OpenSage.Game.Tests/Data/Nif/NifPixelDataTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [ ] The three target texcache entries parse with validated, contiguous mip
      chains totalling the declared pixel bytes.
- [ ] Unsupported formats, palette references, and inconsistent mip chains fail
      contextually.
- [ ] No transcoding: DXT block bytes are preserved verbatim.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifPixelDataTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode DXT pixels, palettes, or `.pcpatch` player-color patches.
- Do not create textures or reference Veldrid, the asset store, or TestHarness.
- Do not widen the C1 interchange graph.

## On completion

- Set Status above to `Done` or `Blocked`.
- Change `WI-C4b-02` to `Ready`.
- Append a result line to `.exchange`.
