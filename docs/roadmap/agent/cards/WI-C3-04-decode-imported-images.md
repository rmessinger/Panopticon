# Task Card `WI-C3-04`: Decode imported images

| Field | Value |
|---|---|
| Card ID | `WI-C3-04` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-01` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Decode exact external virtual paths and embedded encoded bytes into one CPU image representation
without allocating GPU resources.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 3-5.
- `src/OpenSage.Game/Content/Loaders/OnDemandTextureLoader.cs`.
- `src/OpenSage.Game/Data/Tga/TgaFile.cs`.
- `src/OpenSage.Game/Graphics/TextureAsset.cs`.
- `src/Veldrid.ImageSharp/ImageSharpTexture.cs`.
- `src/OpenSage.IO/FileSystem.cs` and `FileSystemEntry.cs`.
- PBR image-source types from `WI-C3-01`.

## Steps

1. Add an engine-owned decoder that opens an external source by its exact virtual path or reads
   copied embedded bytes.
2. Decode PNG and JPEG through the existing ImageSharp dependency. Add a stream-based TGA parser
   entry point and reuse the existing TGA conversion to produce RGBA8 pixels for both source
   forms.
3. Return immutable RGBA8 pixels, dimensions, mip intent, and source diagnostics without a
   `GraphicsDevice` or `Texture`.
4. Validate MIME type against decodable content and reject missing, empty, malformed, or
   unsupported sources with contextual exceptions.
5. Add synthetic external and embedded PNG/JPEG tests and missing/malformed-source tests.

## Files likely to change

- `src/OpenSage.Game/Content/Loaders/DecodedImportedImage.cs` (new)
- `src/OpenSage.Game/Content/Loaders/ImportedImageDecoder.cs` (new)
- `src/OpenSage.Game/Data/Tga/TgaFile.cs`
- `src/OpenSage.Game.Tests/Content/Importing/ImportedImageDecoderTests.cs` (new)

## Acceptance criteria

- [ ] External images open only the exact normalized virtual path.
- [ ] Embedded and external forms decode to the same RGBA8 values for equivalent content.
- [ ] PNG and JPEG are covered without adding a new image dependency.
- [ ] TGA sources produce the same RGBA8 contract.
- [ ] No GPU type enters the decoder contract.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedImageDecoderTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not apply SAGE texture path resolution to external model images.
- Do not add DDS block-compression decoding; DDS remains on the existing W3D texture path.
- Do not infer a missing image or substitute a neutral texture.
- Do not interpret color space while decoding pixels.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C3-05` to `Ready`.
- Append a result line to `.exchange`.
