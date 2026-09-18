# Task Card `WI-C3-07`: Implement core PBR lighting

| Field | Value |
|---|---|
| Card ID | `WI-C3-07` |
| Roadmap phase | [Track C · C3](../../track-c-external-assets.md): Material and texture mapping |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C3-06` |
| Diagrams | [C3 architecture diagrams](../../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) |

## Goal

Complete the scaffolded shader with core metallic-roughness lighting and the remaining texture
roles defined by C3.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c3-material-texture-mapping.md` sections 5 and 6.
- PBR shaders and resource layouts from `WI-C3-06`.
- `src/OpenSage.Game/Assets/Shaders/Lighting.h`.
- `src/OpenSage.Game/Assets/Shaders/NormalMapped.frag`.
- `src/OpenSage.Game/Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs`.
- `src/OpenSage.Game.Tests/GameFixture.cs`.

## Steps

1. Implement metallic and roughness factors plus packed texture channel use.
2. Add tangent-space normal mapping with normal scale.
3. Add occlusion strength against the ambient contribution and emissive factor/texture output.
4. Select UV0 or UV1 independently for every texture binding.
5. Decode emissive samples from sRGB while leaving metallic-roughness, normal, and occlusion
   samples linear.
6. Add `[GameFact(SageGame.CncGenerals)]` shader-resource binding tests for all roles and both UV
   selections using the headless graphics device from `GameFixture`.

## Files likely to change

- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.vert`
- `src/OpenSage.Game/Assets/Shaders/PbrMetallicRoughness.frag`
- `src/OpenSage.Game/Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs`
- `src/OpenSage.Game.Tests/Graphics/Shaders/PbrShaderBindingTests.cs` (new)

## Acceptance criteria

- [ ] Every core PBR factor and texture role affects the documented shader input.
- [ ] Metallic-roughness uses B/G channels; occlusion uses R.
- [ ] Base-color and emissive are decoded from sRGB; data textures remain linear.
- [ ] UV0 and UV1 selection is independent per binding.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~PbrShaderBindingTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add material/pipeline caches or model-materializer integration.
- Do not add texture transforms, vertex colors, or advanced material extensions.
- Do not change fixed-function or named shader-material sources.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C3-05` is done, change `WI-C3-08` to `Ready`.
- Append a result line to `.exchange`.
