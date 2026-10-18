# Task Card `WI-C8b-01`: Resolve shader-material families, per-face ids, and `WsClassicEnv`

| Field | Value |
|---|---|
| Card ID | `WI-C8b-01` |
| Roadmap phase | [Track C · C8b](../../track-c-external-assets.md): BFME-family static meshes |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-C8a-04` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Remove the three section 4.1 mesh conversion hazards in `W3dMaterialConverter`, so every BFME-family shader-material mesh converts to a family that has a shader set.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4.1, 4.2, and 6.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dMaterialConverter.cs`.
- `src/Panopticon.Simulation/Graphics/Shaders/ShaderResourceManager.cs:79-89`, `SimpleShaderResources.cs`, `NormalMappedShaderResources.cs`, `ShaderMaterialShaderResources.cs` (`GetCachedMaterial`).
- `src/Panopticon.FileFormats.W3d/W3dMaterialPass.cs`, `W3dVertexMappingType.cs`.

## Steps

1. Add `W3dMaterialConverter.ResolveShaderFamily(string typeName)`: strip a trailing `.fx` case-insensitively, then match `NormalMapped` or `Simple` case-insensitively and return the canonical key. Any other family returns `Simple` and a fallback flag.
2. On fallback, build `Simple` properties from the source: `Texture_0` from the first texture-kind property (`Texture_0`, `DiffuseTexture`, or any other), and `ColorEmissive` from `ColorEmissive` when present, else white. Log warning `W3D200 <mesh>: shader family '<name>' rendered as Simple`.
3. Add a test asserting the converter's known families equal the `ShaderResourceManager` dictionary keys, so they cannot drift. Check how `GetCachedMaterial` treats missing constant members and record it in the test name or a comment.
4. Per-face shader-material ids: when `ShaderMaterialIds` has one entry per triangle, group consecutive triangles into runs (as `GetMaterialRuns` does for fixed function) and emit one material and one mesh part per run. A count other than 1 or the triangle count throws `InvalidDataException`.
5. Map `W3dVertexMappingType.WsClassicEnv` to `ImportedTextureMappingType.WsEnvironment`. If the renderer treats them differently, map to the closer one and record the choice in `.exchange`.
6. Synthetic tests: `WaterShader.FX` and `DefaultW3D.fx` fall back to `Simple` with the expected properties; `NormalMapped.FX` resolves to `NormalMapped`; a two-material per-face mesh yields two parts with the right index ranges; `WsClassicEnv` converts.
7. Rerun `SageW3dCorpusTests` with all installs and remove the `shader-family`, `multi-shader-material`, and `ws-classic-env` entries from the manifest.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/W3d/W3dMaterialConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/W3dMaterialConverterTests.cs` (new or existing)
- `test/Panopticon.Simulation.Tests/Data/W3d/SageW3dCorpus.json`

## Acceptance criteria

- [ ] Step 6 tests pass.
- [ ] The corpus manifest has no `shader-family`, `multi-shader-material`, or `ws-classic-env` entries and the corpus tests pass.
- [ ] `W3dImportParityTests` pass unchanged with `CNC_GENERALS_PATH` set.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
CNC_GENERALS_ZH_PATH="/path/to/Command & Conquer Generals - Zero Hour" \
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~W3d|FullyQualifiedName~SageW3dCorpus"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not add shader sets or port `.fx` effects. Water and `Default` shading stay approximate.
- Do not route shader materials through C3 PBR (section 6 decision).
- If a determinism or golden-baseline test fails, stop and report per `AGENTS.md`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8b-02` to `Ready`.
- Append a result line to `.exchange`.
