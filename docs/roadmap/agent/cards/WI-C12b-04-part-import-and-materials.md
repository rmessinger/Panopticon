# Task Card `WI-C12b-04`: `VMPF` parts and Complete Edition materials

| Field | Value |
|---|---|
| Card ID | `WI-C12b-04` |
| Roadmap phase | [Track C · C12b](../../track-c-external-assets.md): SHOGUN 2 static models and parts |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C12b-02`, `WI-C12b-03` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

`VMPF` version 3 soldier and horse parts import in bind pose, and RMV2 meshes and parts bind `texture_basecolor`, `texture_mra`, `texture_normal`, or legacy `texture_diffuse` on C3 metallic-roughness materials.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 4, 9, 11, and the results of `WI-C12b-01`.
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs`, `ImportedPbrMetallicRoughness.cs`, `ImportedTextureBinding.cs`.
- `src/Panopticon.Simulation/Content/Loaders/ImportedTextureMaterializer.cs`.

## Steps

1. Add `VmpfFile.Read(stream)` for version 3 with half-float positions; versions 0 and 2 throw with path. Keep skin influences on the parsed file for `WI-C12c-03`.
2. Route `.variant_part_mesh` paths through `Rmv2ModelImporter`, producing bind-pose meshes.
3. Add `Shogun2MaterialConverter.Convert(textureDirectory, context)`: probe the four Complete Edition maps, then `texture_diffuse`; build `ImportedMaterial.CreatePbrMetallicRoughness` with base colour, metallic-roughness, normal, and occlusion bindings.
4. If `WI-C12b-01` found an MRA order other than the glTF layout, add a channel remap to `ImportedTextureBinding` applied by `ImportedTextureMaterializer` after decode, and cover it with a test.
5. Ignore `texture_colour_mask` with a recorded diagnostic; parts render untinted.
6. Add an Interactive test that imports every `VMPF` version 3 part and binds the proof head and horse body materials.

## Files likely to change

- `src/Panopticon.FileFormats.Shogun2/VmpfFile.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Shogun2/Shogun2MaterialConverter.cs` (new), `Rmv2ModelImporter.cs`
- `src/Panopticon.Simulation/Content/Importing/ImportedTextureBinding.cs`, `Content/Loaders/ImportedTextureMaterializer.cs` (only if a remap is needed)
- `test/Panopticon.Simulation.Tests/Content/Importing/Shogun2/`

## Acceptance criteria

- [ ] 551 version 3 parts import or each failure is listed with reason.
- [ ] Proof head and horse body materials bind base colour, metallic-roughness, normal, and occlusion.
- [ ] Legacy-only parts bind base colour only.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Shogun2Part"
```

## Out of scope / guardrails

- Skin weights (`WI-C12c-03`).
- Colour-mask tinting.
- `.unit_variant` composition (`WI-C12c-04`).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C12b-05` to `Ready`.
- Append a result line to `.exchange`.
