# Task Card `WI-C3-10`: Test PBR materialization

| Field | Value |
|---|---|
| Card ID | `WI-C3-10` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C3-09` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Lock CPU validation, resource ownership, cache identity, and static/skinned materialization with
synthetic PBR assets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 4, 5, and 8.
- C3 focused tests from `WI-C3-01..08`.
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedModelMaterializerTests.cs`.
- `test/Panopticon.Simulation.Tests/GameFixture.cs`.
- Existing `[GameFact(SageGame.CncGenerals)]` graphics-test pattern.

## Steps

1. Use `GameFixture` to obtain a headless installed-game graphics device, then build synthetic
   static and two-influence skinned meshes with UV0, UV1, imported tangents, and generated
   tangents.
2. Materialize external and embedded images across every texture role and sampler family.
3. Assert validation occurs before GPU or file access for malformed factors, UVs, references,
   image sources, and tangent inputs.
4. Assert cache reuse and separation for every output-affecting state.
5. Assert fixed-function and named shader-material synthetic fixtures remain unchanged.
6. Keep pixel-level visual assertions for `WI-C3-11`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedModelMaterializerTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/PbrImportedModelTestData.cs` (new)
- C3 focused test files only when sharing the synthetic fixture

## Acceptance criteria

- [ ] Static and two-influence skinned PBR meshes materialize successfully.
- [ ] GPU-dependent tests are marked `[GameFact(SageGame.CncGenerals)]`.
- [ ] External and embedded image paths cover all five texture roles.
- [ ] Validation-order and cache-key contracts have complete assertions.
- [ ] Existing material paths retain their prior synthetic descriptors.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ImportedModelMaterializerTests|FullyQualifiedName~ImportedTextureMaterializerTests|FullyQualifiedName~PbrMetallicRoughnessShaderResourcesTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not weaken equality assertions to broad success-only checks.
- Do not load model or texture data from the game installation; it supplies only the headless
  graphics host.
- Do not add glTF fixtures or parser behavior.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-06` is done, change `WI-C4-09` to `Ready`.
- If done and `WI-C4a-10` is done, change `WI-C3-11` to `Ready`.
- Append a result line to `.exchange`.
