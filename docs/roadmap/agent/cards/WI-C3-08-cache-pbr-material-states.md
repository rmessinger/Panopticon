# Task Card `WI-C3-08`: Cache PBR material states

| Field | Value |
|---|---|
| Card ID | `WI-C3-08` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-05`, `WI-C3-07` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Create and cache complete PBR materials and pipelines for opaque, masked, blended, and
double-sided rendering.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 5 and 6.
- PBR shader resources from `WI-C3-06..07`.
- Texture and sampler materializer from `WI-C3-05`.
- `src/OpenSage.Game/Graphics/Shaders/FixedFunctionShaderResources.cs`.
- `src/OpenSage.Rendering/Material.cs`.
- `src/OpenSage.Game.Tests/GameFixture.cs`.
- Existing transparent surface ordering in `src/OpenSage.Game/Graphics/Rendering/`.

## Steps

1. Add `GetCachedMaterial` and `GetCachedPipeline` to the PBR shader resources.
2. Map opaque, mask, and blend modes to the documented blending, depth-write, alpha-test, and
   surface-type behavior.
3. Disable culling for double-sided materials and cull back faces otherwise.
4. Include every output-affecting factor, texture identity and role, UV selection, sampler,
   alpha mode/cutoff, culling state, and pipeline state in cache keys.
5. Bind neutral engine-owned textures only for omitted optional bindings.
6. Add `[GameFact(SageGame.CncGenerals)]` cache separation/reuse tests for every key dimension
   using the headless graphics device from `GameFixture`.

## Files likely to change

- `src/OpenSage.Game/Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs`
- `src/OpenSage.Game/Graphics/StandardGraphicsResources.cs`
- `src/OpenSage.Game.Tests/Graphics/Shaders/PbrMetallicRoughnessShaderResourcesTests.cs` (new)

## Acceptance criteria

- [ ] Equivalent material descriptions reuse one material and pipeline.
- [ ] Every output-affecting state dimension has a cache-separation assertion.
- [ ] Blend materials use transparent ordering and disable depth writes.
- [ ] Missing referenced images never receive neutral fallback textures.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~PbrMetallicRoughnessShaderResourcesTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add order-independent transparency.
- Do not modify fixed-function or named shader-material cache behavior.
- Do not omit a field from a key because a current test asset uses its default.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C3-02` and `WI-C3-03` are done, change `WI-C3-09` to `Ready`.
- Append a result line to `.exchange`.
