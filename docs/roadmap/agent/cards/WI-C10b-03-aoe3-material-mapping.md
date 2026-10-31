# Task Card `WI-C10b-03`: `.material.XMB` mapping onto C3 PBR

| Field | Value |
|---|---|
| Card ID | `WI-C10b-03` |
| Roadmap phase | [Track C · C10b](../../track-c-external-assets.md): AoE3 DE static Granny models |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C10b-02`, `WI-C10a-02`, `WI-C10a-03` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

Granny mesh materials resolve their `.material.XMB` and map BaseColor, Normals, Masks, and Details onto C3 metallic-roughness with DDT bindings, with the Masks channel layout confirmed.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 4 and 7.
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs` (`CreatePbrMetallicRoughness`), `ImportedPbrMetallicRoughness.cs`, `ImportedTextureBinding.cs`, `ImportedImageSource.cs`.
- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` channel conventions.
- `Render\materialdefs\default.materialdef.XMB` decoded by `WI-C10a-02`.

## Steps

1. Investigate first: decode musketeer and hussar Masks textures, report per-channel histograms, compare against the materialdef sliders, and record the layout (which channels carry metallic, roughness, AO, team color) in the deep dive.
2. Add `Aoe3MaterialConverter`: material name to `.material.XMB` path, read inputs, build `ImportedMaterial.CreatePbrMetallicRoughness` with BaseColor (sRGB), Normals, and a metallic-roughness and occlusion binding matching the recorded Masks layout; apply range sliders as factors.
3. If the Masks layout does not match C3's glTF channel order, record the mismatch and add a channel swizzle in the binding's material properties; do not change C3 shaders in this card.
4. Details maps and team color are recorded as material properties only.
5. A missing material or texture keeps the placeholder material and records a diagnostic with mesh and path.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Granny/Aoe3MaterialConverter.cs` (new)
- `GrannyMeshConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/Aoe3MaterialTests.cs` (new)
- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` (Masks layout)

## Acceptance criteria

- [ ] Masks layout recorded with histogram evidence.
- [ ] Musketeer materials bind four DDT textures with the recorded roles.
- [ ] Missing inputs degrade with diagnostics, never exceptions.
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

- Details blending and team color rendering.
- Shader changes.
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10b-04` to `Ready`.
- Tick C10b in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
