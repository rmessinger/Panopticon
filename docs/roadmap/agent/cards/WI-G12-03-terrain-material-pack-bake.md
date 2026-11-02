# Task Card `WI-G12-03`: Terrain material pack bake

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-03` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G12-01`, `WI-G12-02` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Bake mapped open materials into validated, source-hashed terrain material packs outside the repository, with colour matching and correct mips for colour, normal, and roughness.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) sections 3.1 to 3.3 and both sequences.
- `src/Panopticon.Simulation/Content/TerrainTexturePack.cs` (layout, manifest, staging to copy).
- `src/Veldrid.ImageSharp/PrefilteredTerrainMipBuilder.cs`, `LinearMipBuilder`.

## Steps

1. Add `TerrainMaterialPack` (root `PANOPTICON_MATERIAL_PACK`, else `LocalApplicationData/Panopticon/MaterialPacks`; key `<builder version>/<mapping hash>/<terrain name>/<size>`; `manifest.json` with source hashes, licence record, map kinds, and mip SHA-256 values; staged write and rename publish; invalid packs preserved). Reuse the G7 manifest validation pattern.
2. Add `TerrainMaterialPackBuilder.Bake(map, workDir, installation, sizes)`:
   - seam check (edge difference against the opposite edge, reported per material);
   - resize to each target array size in linear light;
   - colour match per `colourMatch` against the original entry's linear mean (and standard deviation for `meanAndContrast`);
   - colour mips through `PrefilteredTerrainMipBuilder`;
   - normal mips by averaging then renormalizing; roughness mips widened by the lost normal variance; occlusion averaged.
3. Add `BuiltInScenario.TerrainMaterialBake` with `--work-dir` and optional `--scenario` (one map, as the G7 bake does); write a bake report with per-entry status, seam scores, colour shift, and licence.
4. Add a generator for the committed synthetic pack under `test/Panopticon.Simulation.Tests/Data/MaterialPack/` (checkerboard and noise colour, tilted-plane normals, gradient roughness; CC0 by construction) at 64x64 to keep it small.

## Files likely to change

- `src/Panopticon.Simulation/Content/TerrainMaterialPack.cs`, `TerrainMaterialPackBuilder.cs` (new)
- `src/Panopticon.Tools.TestHarness/TerrainMaterialBakeRunner.cs` (new), `BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `Program.cs`
- `test/Panopticon.Simulation.Tests/Content/TerrainMaterialPack*Tests.cs` (new), synthetic pack data

## Acceptance criteria

- [ ] Manifest round-trip, corrupt-pack preservation, colour match, and normal renormalization tests pass.
- [ ] An operator bake of Alpine Assault reports every used entry baked or kept original.
- [ ] No downloaded or baked material is committed beyond the synthetic pack.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario TerrainMaterialBake --headless --work-dir "<work dir>"
```

## Out of scope / guardrails

- Runtime loading (`WI-G12-04`) and shader changes (`WI-G12-05`).
- Do not change the G7 pack format or its builder version.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G12-04` to `Ready`.
- Append a result line to `.exchange`.
