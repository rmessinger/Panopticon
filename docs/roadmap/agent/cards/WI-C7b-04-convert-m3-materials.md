# Task Card `WI-C7b-04`: Convert M3 materials onto C3 PBR

| Field | Value |
|---|---|
| Card ID | `WI-C7b-04` |
| Roadmap phase | [Track C · C7b](../../track-c-external-assets.md): StarCraft II static M3 models |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7b-02`, `WI-C7b-03` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Add `M3MaterialConverter` so opaque standard batches get textured C3 PBR materials, non-opaque batches are skipped, and texture paths resolve by the section 8.4 search order.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 8.1, 8.3, 8.4, and 12.
- `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs` (`CreatePbrMetallicRoughness`), `ImportedPbrMetallicRoughness.cs`, `ImportedTextureBinding.cs`, `ImportedSampler.cs`.
- `src/Panopticon.Simulation/Content/Importing/M3/M3ModelImporter.cs` from `WI-C7b-02`.

## Steps

1. Add `M3TexturePathResolver`: derive the model package (the path prefix up to and including `.sc2assets`), and try `<package>/<layer path>` then `mods/core.sc2mod/base.sc2assets/<layer path>`, case-insensitively. Missing files throw `FileNotFoundException` listing both attempts.
2. Add `M3MaterialConverter`: `MATM` type 1 only. Opaque (blend 0) standard materials map diffuse, normal, specular, and emissive 1 through `M3TextureConverter` into a PBR material with metallic 0 and repeat samplers, UV set from the layer UV source. Alpha test threshold > 0 gives `Mask`.
3. In `M3ModelImporter`, skip batches whose material is non-opaque, and collect a diagnostic list of skipped batches and ignored layers exposed on the importer result (for example `M3ImportDiagnostics`).
4. Fail with `InvalidDataException` when no batch is importable.
5. Synthetic tests: a diffuse+normal+specular material, an additive batch skipped, a decal layer ignored, `MATM` type 2 rejected, and the search-order fallback to `core.sc2mod`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/M3/M3MaterialConverter.cs`, `M3TexturePathResolver.cs`, `M3ImportDiagnostics.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/M3/M3ModelImporter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/M3MaterialConverterTests.cs` (new)

## Acceptance criteria

- [ ] Opaque materials bind base color, normal, metallic-roughness, and emissive textures with the right UV sets.
- [ ] Additive batches and decal layers appear in diagnostics, not in output.
- [ ] `teen.sc2assets` is never searched, and the `core.sc2mod` fallback works.
- [ ] `MATM` type 2 and a model with no importable batch fail explicitly.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No team color, no decal compositing, no effect materials.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7b-05` to `Ready`.
- Append a result line to `.exchange`.
