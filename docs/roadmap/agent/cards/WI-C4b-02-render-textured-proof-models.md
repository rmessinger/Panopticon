# Task Card `WI-C4b-02`: Render the C4a proof models textured

| Field | Value |
|---|---|
| Card ID | `WI-C4b-02` |
| Roadmap phase | [Track C · C4b](../../track-c-external-assets.md): EE2 cooked texture decoding |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4a-11`, `WI-C4b-01` |
| Diagrams | [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) |

## Goal

Resolve the C4a models' by-name texture references through `textures.zip`,
upload the decoded DXT mips as runtime textures, and re-capture both proof
models fully textured.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 6 and 9.2.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandTextureLoader.cs` (static texture
  upload from mip data) and `ScopedAssetCollection` (asset injection).
- `src/Panopticon.Simulation/Content/Importing/Nif/NifMaterialConverter.cs`.

## Steps

1. Importer: when the base texture's `cache/texcache/<name>.texcache` entry
   resolves through `AssetImportContext.FileSystem`, set the fixed-function
   shading to textured (`TextureStageCount = 1`, `TexturingEnabled = true`);
   otherwise keep the C4a untextured fallback. Bake NIF V-up UVs to the
   engine's texture orientation (`v' = 1 - v`).
2. TestHarness proof: mount `textures.zip` beside `graphics.zip` in one
   composite import file system; decode each referenced texcache through the
   C4b-01 parser and upload the mips as BC1/BC2/BC3 static textures
   (`CreateStaticTexture2D`); register each as a `TextureAsset` under its
   source `.tga` name so fixed-function materialization resolves it.
3. Re-capture both proof models textured (DXT3 alpha on the Bomber's blended
   materials must not break depth) and update the validation guide text.
4. Extend the interactive descriptor test: with `textures.zip` mounted the
   import is textured (stage count 1); with `graphics.zip` alone it stays at
   stage count 0.
5. Add focused non-interactive tests for the texcache name resolution and the
   DXT format mapping; no GPU required.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifMaterialConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSceneConverter.cs`
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`
- `src/Panopticon.Tools.TestHarness/Ee2TexcacheTextureInstaller.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/Ee2TexcacheTextureInstallerTests.cs` (new)

## Acceptance criteria

- [ ] Both proof captures render visibly textured; the Bomber's alpha-blended
      parts keep correct depth behavior.
- [ ] The untextured fallback still applies when a texcache entry is absent.
- [ ] Texture upload preserves DXT blocks without CPU transcoding.
- [ ] Deterministic repeat captures; no EE2 asset copied or committed.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario Ee2NifProof
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not decode `.pcpatch` player-color patches or apply player-color swapping.
- Do not add an EE2 game definition or a general texcache asset loader to
  shipped game paths.
- Do not change the C4 glTF pipeline.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C4b Status to `Done` and tick C4b in the implementation
  checklist.
- Append a result line to `.exchange`, including capture paths and verdicts.
