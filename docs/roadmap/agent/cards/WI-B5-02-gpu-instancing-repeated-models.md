# Task Card `WI-B5-02`: Source-agnostic GPU instancing for repeated models

| Field | Value |
|---|---|
| Card ID | `WI-B5-02` |
| Roadmap phase | [Track B · B5](../../track-b-scale.md): B5-2 instancing |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B5-01` |
| Diagrams | [B5 diagrams §6](../../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams) (class + sequence, grounded) |

## Goal

Repeated identical mesh parts (same material, index buffer, index range) across many model
instances draw as one instanced `DrawIndexed` call instead of N single-instance calls, for
any model source, with pixel-identical output.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md`: sections 2, 3.1, 6.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItem.cs`, `RenderItemCollection.cs`,
  `RenderPipeline.cs` (`DoRenderPass`).
- `src/Panopticon.Simulation/Graphics/ModelMesh.cs` (`BuildRenderListWithWorldMatrix`),
  `ModelMeshPart.cs`, `ModelMeshInstance.cs` (`OnBeforeRender`, per-part constants).
- `src/Panopticon.Simulation/Graphics/Shaders/MeshShaderResources.cs`
  (`RenderItemConstantsResourceLayout`, `RenderItemConstantsVS/PS`, mesh constants cache),
  `FixedFunctionShaderResources.cs`, `MeshDepthShaderResources.cs`,
  `PbrMetallicRoughnessShaderResources.cs`, `ShaderSetBase.cs`.
- `src/Panopticon.Rendering/Material.cs`, `ShaderSet.cs`, `Shaders/ShaderCrossCompiler.cs`.
- `src/Panopticon.Simulation/Assets/Shaders/Mesh.h`, `FixedFunction.vert`, `FixedFunction.frag`,
  `MeshDepth.vert`, `PbrMetallicRoughness.vert`.
- `test/Panopticon.Simulation.Tests/Graphics/Shaders/SingleSamplePipelineVariantTests.cs`
  (GraphicsFact pattern), `test/Panopticon.Simulation.Tests/GraphicsFactAttribute.cs`.

## Steps

1. `Mesh.h`: add the shared `InstanceData` std430 struct (`mat4 World; vec4 UVOffset; vec4
   HouseColorOpacity; vec4 Tint;`), an `InstancedItemConstants` UBO (`uint InstanceStart`
   plus padding), and an instance-buffer binding macro for set 3.
2. New shaders: `FixedFunctionInstanced.vert` (+ `.frag` variant reading house
   color/opacity/tint from flat varyings; override branches compile out),
   `MeshDepthInstanced.vert` (world-only), `PbrMetallicRoughnessInstanced.vert`. Base
   fragment shaders are reused where they read no item constants (`MeshDepth.frag`,
   `PbrMetallicRoughness.frag`) via a vert/frag name-pair overload on
   `ShaderCrossCompiler.GetOrCreateCachedShaders` and `ShaderSetBase`.
3. `Material` gains `Pipeline? InstancedPipeline`. Populate it in
   `FixedFunctionShaderResources.GetCachedMaterial` (per pipeline key),
   `MeshDepthShaderResources` (one instanced material beside `Material`), and the
   `PbrMetallicRoughness` family. Each family also creates one shared instance resource set
   layout (mesh constants + start UBO + instance SSBO) and a set builder.
4. `RenderItem`: add `bool InstancingEligible`, an instance payload
   (World already present; add UVOffset, HouseColor, Opacity, Tint), and extend the sort
   key with a stable mesh-part id for eligible items so equal-geometry runs sort contiguously.
   `ModelMeshPart` gains the interned `InstanceId`.
5. `ModelMesh.BuildRenderListWithWorldMatrix`: compute eligibility (not `Skinned`, no
   `MorphVertexBuffer`, not `CameraOriented`, not forced-blend, PS constants at defaults
   with material instanced pipeline present) and fill the payload for both the Opaque and
   Shadow adds.
6. New `Rendering/Instancing/`: `InstancePool` (grow-only device buffer + CPU staging,
   per-frame `Reset`, `Append`, `UpdateRange(commandList)`),
   `InstancedItemConstants` buffer, and the run-grouping logic used by `DoRenderPass`
   (maximal contiguous eligible runs sharing instanced pipeline, index buffer, start
   index, index count; one `DrawIndexed(indexCount, runLength, startIndex, 0, 0)` per run;
   update start UBO + bind instance set before each run).
7. `RenderPipeline.InstancingEnabled` (default true, honors `RenderScaleOptions`): when
   off, every item draws exactly as today. Statistics count batches, instances, and
   instanced draw calls.
8. Tests (GraphicsFact): instanced pipelines exist for the three families and are distinct
   from base pipelines; a synthetic draw (two quads, same material) renders byte-identical
   pixels through N per-item draws vs one instanced draw; eligibility unit tests for the
   skinned/morph/camera-oriented/override exclusions (CPU-side, no GPU needed where
   possible).

## Files likely to change

- `src/Panopticon.Simulation/Assets/Shaders/Mesh.h`, `FixedFunctionInstanced.vert/.frag`,
  `MeshDepthInstanced.vert`, `PbrMetallicRoughnessInstanced.vert` (new)
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItem.cs`, `RenderPipeline.cs`,
  `Instancing/InstancePool.cs`, `Instancing/InstancedBatcher.cs` (new)
- `src/Panopticon.Simulation/Graphics/ModelMesh.cs`, `ModelMeshPart.cs`
- `src/Panopticon.Simulation/Graphics/Shaders/MeshShaderResources.cs`,
  `FixedFunctionShaderResources.cs`, `MeshDepthShaderResources.cs`,
  `PbrMetallicRoughnessShaderResources.cs`, `ShaderSetBase.cs`
- `src/Panopticon.Rendering/Material.cs`, `Shaders/ShaderCrossCompiler.cs`, `Shaders/ShaderSet.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Rendering/Instancing/` (new)

## Acceptance criteria

- [ ] `RenderStatistics` shows one instanced batch per repeated mesh part at 10k units in
      the probe (instance count near item count for rigid models), with `InstancingEnabled`
      on; turning it off restores single-instance draws and identical statistics shape to
      baseline.
- [ ] Probe pixel A/B: one captured frame with instancing on vs off is byte-identical for a
      rigid repeated-model scene (compare PNG bytes or per-pixel via ImageSharp; any diff is
      a deviation, not a tweak target).
- [ ] Skinned, morph, camera-oriented, and override-carrying items never enter an
      instanced run (unit-tested eligibility).
- [ ] A PBR-material mesh flows through an instanced run (GraphicsFact), proving the seam
      is not W3D-specific.
- [ ] Standard validation passes, and shadow, linear-shading, proportions, and
      single-sample pipeline suites stay green.

## Validation commands

```sh
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet test src --filter 'Category!=Interactive'
dotnet test src --filter 'FullyQualifiedName~Instancing'
CNC_GENERALS_PATH='/home/rmessinger/dev/Command and Conquer Generals' \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario RenderingScaleProbe --b5-unit-count 10000
# A/B pixels: run twice with/without --b5-no-instancing and diff the two PNGs
```

## Out of scope / guardrails

- No new logic-side reads: payload values are the same ones the per-item path already
  submits (A3 boundary unchanged).
- Transparent and Water buckets are never instanced.
- Do not touch `NormalMapped`/`Simple` families (recorded gap) or skinned instancing.
- Do not change `GraphicsSettings` presets; the toggle is `RenderPipeline`/options level.
- Pixel differences between instanced and per-item output are failures, not tuning targets.

## On completion

- Set Status to `Done`.
- Append a `.exchange` line.
- Follow-up cards: none new (WI-B5-03 next).
