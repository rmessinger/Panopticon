# Task Card `WI-C4a-05`: Decode NIF material properties

| Field | Value |
|---|---|
| Card ID | `WI-C4a-05` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4a-03` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Decode the fixed-function property blocks and by-name texture references used by the two target
models without loading textures or creating renderer resources.

## Context to read (and only this)

- Completed `WI-C4a-01` target descriptors.
- Niftools `nif.xml` definitions for `NiProperty`, `NiMaterialProperty`,
  `NiTexturingProperty`, `NiSourceTexture`, `NiAlphaProperty`, `NiSpecularProperty`,
  `NiZBufferProperty`, and `NiVertexColorProperty`.
- NIF scene-graph types and synthetic builder from `WI-C4a-03`.

## Steps

1. Add one immutable parser type per property block used by either target.
2. Decode ambient, diffuse, specular, emissive, glossiness, opacity, texture slots and source
   names, alpha flags/threshold, specular enablement, depth flags/function, and vertex-color
   flags.
3. Resolve each `NiTexturingProperty` source reference only to a `NiSourceTexture` block. Preserve
   source names verbatim; do not resolve `.texcache`.
4. Validate enum values, source references, slot counts, and flags. Reject target flags that have
   no explicit mapping in the C4a design.
5. Add synthetic tests for each target property type and malformed references.

## Files likely to change

- `src/OpenSage.FileFormats.Nif/Blocks/NifProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifMaterialProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifTexturingProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifSourceTexture.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifAlphaProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifSpecularProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifZBufferProperty.cs` (new)
- `src/OpenSage.FileFormats.Nif/Blocks/NifVertexColorProperty.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifPropertyTests.cs` (new)
- `src/OpenSage.Game.Tests/Data/Nif/NifTestDataBuilder.cs`

## Acceptance criteria

- [ ] Every property value listed by the completed target inventory is preserved.
- [ ] Texture source names remain plain CPU strings.
- [ ] Unsupported flags and malformed property references fail contextually.
- [ ] No code references Veldrid, `AssetStore`, runtime materials, `.texcache`, or `.pcpatch`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifPropertyTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode pixel data or player-color patches.
- Do not map properties into `ImportedMaterial`; `WI-C4a-07` owns that boundary.
- Do not add property blocks absent from both target files.

## On completion

- Set Status above to `Done` or `Blocked`.
- If `WI-C4a-04` and `WI-C4a-06` are also done and `WI-C1-08` is done, change
  `WI-C4a-07` to `Ready`.
- Append a result line to `.exchange`.
