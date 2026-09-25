# Task Card `WI-C3-05`: Materialize imported textures

| Field | Value |
|---|---|
| Card ID | `WI-C3-05` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C3-04` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Upload decoded imported images as UNorm textures and cache texture and sampler resources by their
complete scoped identities.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` section 5.
- Image decoding types from `WI-C3-04`.
- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs`.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandTextureLoader.cs`.
- `src/Veldrid.ImageSharp/ImageSharpTexture.cs`.
- `src/Panopticon.Simulation/Content/Loaders/AssetLoadContext.cs`.
- `test/Panopticon.Simulation.Tests/GameFixture.cs`.
- Existing `[GameFact(SageGame.CncGenerals)]` graphics-test pattern.
- Veldrid sampler creation patterns in `src/Panopticon.Simulation/Graphics/`.

## Steps

1. Add `ImportedTextureMaterializer` owned by the model materialization lifetime.
2. Upload decoded images as ordinary RGBA8 UNorm textures with deterministic mip generation.
3. Map imported U/V address and minification, magnification, and mip filters to Veldrid samplers.
4. Cache embedded textures by immutable content identity and external textures by scoped
   file-system/path identity.
5. Cache samplers by every imported sampler field.
6. Add `[GameFact(SageGame.CncGenerals)]` resource-identity and disposal tests using
   `GameFixture` and its headless graphics device.

## Files likely to change

- `src/Panopticon.Simulation/Content/Loaders/ImportedTextureMaterializer.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedTextureMaterializerTests.cs` (new)

## Acceptance criteria

- [ ] Equivalent sources in one asset scope reuse one uploaded texture.
- [ ] Different embedded bytes or scoped external identities do not collide.
- [ ] Sampler cache keys distinguish every address and filter field.
- [ ] Color-space role is not baked into or used to duplicate uploaded UNorm texture data.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ImportedTextureMaterializerTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not create PBR materials or pipelines.
- Do not upload sRGB-format variants of the same image.
- Do not use process-global caches or retain resources after their owning scope is disposed.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C3-07` is done, change `WI-C3-08` to `Ready`.
- Append a result line to `.exchange`.
