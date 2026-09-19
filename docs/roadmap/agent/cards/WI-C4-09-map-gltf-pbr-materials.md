# Task Card `WI-C4-09`: Map glTF PBR materials

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C4-09` |
| Roadmap phase | [Track C · C4](../../track-c-external-assets.md): First production external format |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-09`, `WI-C3-10`, `WI-C4-02`, `WI-C4-06` |
| Diagrams | [C4 architecture diagrams](../../deep-dives/c4-gltf-importer.md#11-architecture-diagrams) |

## Goal

Map glTF core metallic-roughness materials, images, texture coordinates, samplers, alpha, and
double-sided state into the completed C3 interchange contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4-gltf-importer.md` section 8.
- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 3-7.
- `src/OpenSage.Game/Content/Importing/ImportedMaterial.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedMeshPart.cs`.
- `src/OpenSage.Game/Content/Importing/ImportedImageSource.cs` (new from `WI-C3-01`).
- `src/OpenSage.Game/Content/Importing/ImportedSampler.cs` (new from `WI-C3-01`).
- `src/OpenSage.Game/Content/Importing/ImportedTextureBinding.cs` (new from `WI-C3-01`).
- `src/OpenSage.Game/Content/Importing/ImportedPbrMetallicRoughness.cs` (new from `WI-C3-01`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfResourceResolver.cs` (new from `WI-C4-02`).
- `src/OpenSage.Game/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new from `WI-C4-06`).

## Steps

1. Map base-color factor and texture, metallic and roughness factors and packed texture, normal
   texture and scale, occlusion texture and strength, and emissive factor and texture.
2. Map `OPAQUE`, `MASK`, and `BLEND`, alpha cutoff, and `doubleSided` to the C3 payload. Use glTF
   defaults when values are omitted.
3. Map texture-coordinate index 0 or 1 to the matching mesh-part UV array. Reject higher indices
   and reject a binding whose selected UV array is absent.
4. Map glTF wrap and filter constants to C3 sampler enums, including specified glTF defaults for
   a missing sampler or omitted filter value.
5. Convert external image URIs to exact normalized virtual paths returned by the resolver.
   Convert data-URI and buffer-view PNG or JPEG images to copied encoded bytes with MIME type and
   a deterministic diagnostic name.
6. Validate image MIME type and source consistency. Reject unsupported image formats, texture
   transforms, advanced material semantics, and incomplete core fallback.
7. Attach the converted material index to every primitive result without adding a source-format
   branch to C3 materialization.
8. Add tests covering every factor and texture role, external and embedded images, UV0 and UV1,
   sampler defaults and variants, alpha modes, double-sided state, and unsupported mappings.

## Files likely to change

- `src/OpenSage.Game/Content/Importing/Gltf/GltfMaterialConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfImageSourceConverter.cs` (new)
- `src/OpenSage.Game/Content/Importing/Gltf/GltfPrimitiveConverter.cs` (new from `WI-C4-06`)
- `src/OpenSage.Game.Tests/Content/Importing/GltfMaterialConverterTests.cs` (new)

## Acceptance criteria

- [ ] Every core PBR value in the deep-dive table maps to the corresponding C3 value.
- [ ] External images retain exact virtual-path identity; embedded images retain immutable bytes
      and MIME type.
- [ ] UV and sampler choices remain explicit and deterministic.
- [ ] Alpha and double-sided output uses C3 policy rather than duplicated render-state guesses.
- [ ] No SharpGLTF type crosses into a C3 or runtime signature.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GltfMaterialConverterTests|FullyQualifiedName~ImportedModelMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement `KHR_materials_*`, texture transforms, vertex colors, or morph materials.
- Do not decode pixels or allocate textures in the importer.
- Do not apply SAGE texture search paths to external image URIs.
- Do not modify C3 shader or cache policy unless a failing C3 contract test proves a defect.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-07` and `WI-C4-08` are done, change `WI-C4-10` to `Ready`.
- Append a result line to `.exchange`.
