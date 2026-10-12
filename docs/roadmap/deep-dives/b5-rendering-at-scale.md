# Deep Dive: B5 Rendering at scale

> Part of [Track B: Scale](../track-b-scale.md). GPU instancing for repeated unit models,
> distance LOD/culling, and render-bucket/shadow submit improvements so presentation keeps up
> with large logic scenes.
>
> Audience: human overview of the design; agent context for the B5 cards. Grounded against
> commit `bc5661dda` (origin/main at authoring); file references are that revision.

## 1. Current state

Per-frame presentation flow (all types exist today):

| Step | Code | Per-frame cost at N units |
|---|---|---|
| Object walk | `Scene3D.BuildRenderList` (`src/Panopticon.Simulation/Scene3D.cs:416`) | N objects; per-object frustum cull via `RoughCollider`, then `Drawable.BuildObjectRenderList` (`src/Panopticon.Simulation/Client/Drawable.cs:474`) |
| Model walk | `W3dModelDraw.BuildRenderList` → `ModelInstance.BuildRenderList` (`src/Panopticon.Simulation/Graphics/ModelInstance.cs:304`) → `ModelMesh.BuildRenderListWithWorldMatrix` (`src/Panopticon.Simulation/Graphics/ModelMesh.cs:94`) | one `AxisAlignedBoundingBox.Transform` plus one `RenderItem` add per mesh part, to the Opaque or Transparent bucket, plus one to the Shadow bucket |
| Item struct | `RenderItem` (`src/Panopticon.Simulation/Graphics/Rendering/RenderItem.cs:9`) carries `Material`, `BoundingBox`, `World`, index range, and a `BeforeRenderDelegate` | struct copies into `RenderItemCollection._items` (`RenderItemCollection.cs:43`) |
| Cull + sort | `RenderItemCollection.CullAndSort` (`RenderItemCollection.cs:67`) runs per pass | frustum test per item, then `List.Sort` of visible indices, per pass |
| Passes | `RenderPipeline.Execute` (`src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:200`) runs `DoRenderPass` (`RenderPipeline.cs:459`) for: Shadow bucket once per shadow cascade (`ShadowMapRenderer.RenderShadowMap`, `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowMapRenderer.cs:35`), Opaque, Transparent, Water, plus Opaque again for water refraction and reflection when water is visible | up to 7 cull+sort+draw sweeps of the item list |
| Per-item draw | `DoRenderPass` per item: `PushDebugGroup($"Render item: {name}")` (string interpolation), `BeforeRenderCallback` → `ModelMeshInstance.OnBeforeRender` (`src/Panopticon.Simulation/Graphics/ModelMeshInstance.cs:233`) updates a per-mesh `ConstantBuffer<RenderItemConstantsVS>` and binds resource set 3, then `SetIndexBuffer`, `DrawIndexed(count, 1, ...)` | one draw call, one index-buffer bind, one resource-set bind, one debug-group pair per item per pass |

Per-instance GPU state today: `MeshShaderResources.RenderItemConstantsResourceLayout`
(`src/Panopticon.Simulation/Graphics/Shaders/MeshShaderResources.cs:21`) binds
`MeshConstants` (shared per skinning/house-color combination), `RenderItemConstantsVS`
(world matrix + UV scroll, per `ModelMeshInstance`), `SkinningBuffer` (per `ModelInstance`),
and `RenderItemConstantsPS` (house color, opacity, tint, property-animation overrides, per
`ModelMeshPartInstance`). Every mesh part of every unit is drawn with instance count 1.

Model sources are already unified at this layer: W3D (`W3dModelImporter`), glTF
(`GltfModelImporter`), and EE2 NIF (`NifModelImporter`) all materialize into
`Model`/`ModelMesh`/`ModelMeshPart` and submit `RenderItem`s through the same path, so an
instancing seam placed at `RenderItem`/`RenderPipeline` level is source-agnostic by
construction. This is the coordination requirement from
[Track B](../track-b-scale.md): instancing must not be W3D-specific, and WorldBuilder (B4a-7)
and ReplaySketch (A5-5) viewports reuse it because they render through the same
`RenderPipeline`.

Culling that exists: per-object `RoughCollider` frustum cull in `Scene3D.BuildRenderList`
(gated by `Scene3D.FrustumCulling`), per-item frustum cull plus water clipping planes in
`CullAndSort`. No distance or screen-size LOD of any kind; `StaticGameLod`/`DynamicGameLod`
INI data (`src/Panopticon.Simulation/Lod/`) is parsed but not applied to rendering.

Measurement that exists: B1 (`BenchmarkMetricRecorder`,
`src/Panopticon.Benchmarks.Simulation/BenchmarkMetricRecorder.cs`) records logic tick spans
(`gameLogic`, `scene3d`, `network`) through `LogicStepper.SystemSpan`; the TestHarness
capacity probe (`MassBattleCapacityProbeRunner.RunEngagement`,
`src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs:425`) drives frames and
writes a `BenchmarkRunRecord`. None of these measure the render pass: headless probe loops
call `Game.Update` only and never `Game.Render`. Presentation has no counters.

## 2. Problem at the scale anchor

The [scale anchor](../open-questions.md) is 10k units at ~30 fps single-node. A 10k-unit scene
with a two-part-per-unit model produces roughly 20k Opaque items and 20k Shadow items per
frame. At 4 shadow cascades plus forward plus water passes, the current pipeline performs
about 100k+ single-instance draw calls, one `SetIndexBuffer` and one debug-group pair per
draw, and 6 sorts of a 20k-element list, every frame. The CPU submit cost dominates before
the GPU raster cost matters. Two structural fixes:

1. Draw repeated identical geometry once with an instance count instead of N times.
2. Stop paying per-item and per-pass overhead that does not depend on item state (sorting,
   index-buffer binds, debug-group recording), and stop submitting geometry whose screen
   size is negligible.

Skinned meshes cannot batch under one draw with this design: the per-instance
`SkinningBuffer` is a distinct bound buffer per `ModelInstance`. Skinned meshes stay on the
per-item path; this is an explicit, recorded limitation (see Budgets). Rigid hierarchies
(vehicles, structures, imported static/props), whose per-mesh world matrix is written
per-instance into the instance stream, batch fully.

## 3. Design

### 3.1 Instancing seam (source-agnostic)

The seam is the `RenderItem` → `RenderPipeline.DoRenderPass` boundary, not any importer:

- `RenderItem` gains an `InstancingEligible` flag and, for eligible items, an instance
  payload (`World`, `UVOffset`, `HouseColor`, `Opacity`, `TintColor`) captured at
  construction. Eligibility is decided in `ModelMesh.BuildRenderListWithWorldMatrix`, the
  single point every model source flows through, and requires: mesh not `Skinned`, no morph
  vertex buffer, mesh not `CameraOriented`, no forced blend, and item PS constants at
  defaults (no property-animation overrides, opacity 1, white tint; house color varies per
  instance and is part of the payload).
- Eligible items sort by material then mesh-part identity (a stable interned id on
  `ModelMeshPart`), so equal-geometry runs are contiguous after `CullAndSort`.
- `DoRenderPass` groups maximal contiguous runs with equal instanced pipeline, index buffer,
  and index range, appends their payloads into a frame instance pool, and issues one
  `DrawIndexed(indexCount, runLength, ...)` per run. Ineligible items draw exactly as today.
- Per-instance data reaches the shader through a structured buffer, not extra vertex
  attributes: the mesh vertex layout already occupies the guaranteed minimum of 16 vertex
  attribute locations (`MeshShaderResources.MeshVertex.VertexDescriptors`,
  `MeshShaderResources.cs:162`), so instance attributes would exceed the Vulkan
  minimum `maxVertexAttributeCount`. A `readonly buffer` indexed by an explicit batch start
  (a small UBO updated per batch, the same update-then-bind pattern
  `ModelMeshInstance.OnBeforeRender` already uses) keeps the layout portable and the vertex
  layout untouched.

Instanced pipeline variants are opt-in per shader family, behind `Material.InstancedPipeline`
(nullable; null means ineligible). B5 lands variants for the families a repeated-model scene
actually uses: `FixedFunctionInstanced` (forward, W3D fixed-function materials), a
`PbrMetallicRoughnessInstanced` vertex variant (forward, glTF/SC2 imports; the fragment
shader reads no item constants and is reused verbatim), and `MeshDepthInstanced` (shadow
pass; all shadow items share `MeshDepthShaderResources.Material` today). `NormalMapped` and
`Simple` shader-material families stay per-item; recorded as a follow-up gap, not silently.

The instance payload layout (GLSL `Mesh.h` addition) is one std430 struct shared by every
instanced family: `mat4 World; vec4 UVOffset; vec4 HouseColorOpacity; vec4 Tint;`
(112 bytes). House color and tint travel as flat varyings from the instanced vertex shader
to the instanced fragment shader; override branches compile out because eligibility
requires default PS state.

`RenderPipeline.InstancingEnabled` (default on) toggles the whole path for A/B measurement
and as a safety valve.

### 3.2 Distance LOD gate (small-feature culling)

One gate, applied where items are built, covering both Opaque and Shadow buckets: in
`ModelMesh.BuildRenderListWithWorldMatrix`, after computing the world matrix, project the
mesh bounding sphere to screen and return early when its radius falls below a threshold
(default 1 px). A whole model at sub-pixel size contributes nothing to either pass. The
threshold and enable flag live on a mutable static `RenderScaleOptions`
(the `PhysicsBackendPolicy.Current` pattern) so the probe and tools can sweep it without
graphics-settings plumbing. Camera fields needed for the projection
(`FieldOfView`, `ScreenSize`, `Position`) already exist
(`src/Panopticon.Simulation/Graphics/Cameras/Camera.cs`).

Impostors (billboard replacements at distance) are out of scope for B5 and recorded as a
follow-up; the gate is the LOD floor. Model-asset multi-LOD selection (importing W3D HLod
arrays beyond the first) is likewise out of scope; the importer currently keeps one LOD
(`W3dModelImporter`).

### 3.3 Render-bucket and shadow submit improvements

All are state-independent overhead removals in `RenderItemCollection`/`DoRenderPass`:

- Sort once per frame per bucket: item keys do not depend on the frustum. `CullAndSort`
  splits into a per-frame sort of the full item list and a per-pass visible-index filter
  that preserves order. Four-cascade shadows drop from 6 sorts of the visible list to 1 sort
  of the full list plus 6 linear filters.
- Bind the index buffer only when it changes across the pass (it is currently bound per
  item).
- Record per-item debug groups only when a debug flag is set; today every item allocates a
  formatted string and records a debug-group pair per pass.
- The Shadow bucket keeps per-cascade frustum culling (already correct); with the LOD gate
  above, negligible items never enter the bucket.

### 3.4 Measurement: the render-scale probe

A TestHarness built-in scenario `RenderingScaleProbe`
(`--built-in-scenario RenderingScaleProbe`), modeled on `MassBattleCapacityProbeRunner`:

- Boots a real game headless (offscreen Vulkan device, `GraphicsDeviceUtility`), spawns a
  configurable repeated-model field (default 10k of one rigid definition) plus optionally
  the b1 anchor composition (10k Rangers, skinned), fixes the camera over the field, runs
  warmup frames then measured frames of `game.Update` + `game.Render`, drains with
  `GraphicsDevice.WaitForIdle`, and writes a `BenchmarkRunRecord` through the B1 writers
  (`BenchmarkReportWriter`).
- `RenderPipeline` publishes per-frame `RenderStatistics` (items per bucket, visible items
  per pass, draw calls, instanced batches, instances drawn, instances per batch) and a
  `RenderSpan` event (`buildRenderList`, `shadowPass`, `forwardPass`, `render` total,
  `gpuIdle` drain), mirroring `LogicStepper.SystemSpan`.
- A/B switches via environment and probe options: instancing on/off, LOD gate on/off,
  shadow tier, unit count, definition.
- Captures a PNG at the end (`FrameCapturer.CaptureToPng`, the capacity-probe pattern) for
  visual evidence and for the pixel A/B check below.

Correctness gate: for eligible items, instanced and per-item draws must produce identical
pixels (same matrices, same constants, same submission order within a run). The probe
verifies this by capturing one frame with instancing on and off and comparing bytes;
any difference is a deviation, not a tuning knob.

## 4. Budgets (scale anchor)

Anchor: 10k units at ~30 fps (33.3 ms frame budget) per the
[scale-anchor Decision](../open-questions.md). B5 budgets presentation only; the logic-side
budget belongs to B2/B3.

| Budget | Target at 10k rigid repeated models | Measured |
|---|---|---|
| Render submit + GPU (update excluded), median | within 33.3 ms frame budget on the dev-host GPU (RADV offscreen) | filled by WI-B5-01/05 |
| Draw calls per frame, opaque + shadow | two orders of magnitude below item count (instances batched per mesh part) | filled by WI-B5-01/05 |
| Skinned meshes (b1 anchor composition) | not instanced; recorded gap with its measured frame cost | filled by WI-B5-05 |

If a budget cannot be met on the available render path, the gap is recorded here with its
measurement; numbers are never tuned to flatter the result.

## 5. Sub-phases and cards

| Sub-phase | Card | Scope |
|---|---|---|
| B5-1 baseline | `WI-B5-01` | render-path counters, `RenderSpan`/`RenderStatistics`, the `RenderingScaleProbe` scenario, baseline numbers into this deep dive |
| B5-2 instancing | `WI-B5-02` | instance payload seam, instanced shader families, batched draws, pixel A/B |
| B5-3 LOD/culling | `WI-B5-03` | small-feature LOD gate, sort-once filter-per-pass |
| B5-4 submit/shadow | `WI-B5-04` | index-buffer bind hoist, debug-group gating, shadow-path verification |
| B5-5 budget proof | `WI-B5-05` | anchor-scene verification runs, numbers, evidence artifacts |

Cards live in [`../agent/cards/`](../agent/cards/); the sequence is strictly in this order.

## 6. Architecture Diagrams

Grounded in the post-B5 types. `<<new>>` marks types B5 introduces.

```mermaid
classDiagram
  class RenderPipeline {
    <<RenderPipeline.cs>>
    +bool InstancingEnabled
    +RenderStatistics Statistics
    +event RenderSpan(name, span)
    +Execute(context)
    -DoRenderPass(bucket, frustum)
  }
  class RenderList {
    <<RenderList.cs>>
    +Opaque RenderBucket
    +Shadow RenderBucket
  }
  class RenderItem {
    <<RenderItem.cs>>
    +Material Material
    +Matrix4x4 World
    +bool InstancingEligible
    +InstancePayload Payload
    +int Key
  }
  class InstancePool {
    <<new>>
    +Reset()
    +Append(in InstancePayload) int
    +DeviceBuffer Buffer
    +ConstantBuffer~InstancedItemConstants~ StartBuffer
  }
  class InstancedBatcher {
    <<new>>
    +TryBeginRun(item) bool
    +AppendInstance(item)
    +EndRun(commandList, draw)
  }
  class Material {
    <<Rendering/Material.cs>>
    +Pipeline Pipeline
    +Pipeline? InstancedPipeline
  }
  class MeshDepthShaderResources {
    <<Shaders/MeshDepthShaderResources.cs>>
    +Material Material
    +Material InstancedMaterial
  }
  class ModelMesh {
    <<ModelMesh.cs>>
    -BuildRenderListWithWorldMatrix(...)
  }
  class ModelMeshPart {
    <<ModelMeshPart.cs>>
    +int InstanceId
  }
  class RenderScaleOptions {
    <<new, static>>
    +bool SmallFeatureCulling
    +float SmallFeatureCullingPx
    +bool Instancing
  }
  class RenderingScaleProbeRunner {
    <<new, TestHarness>>
    +Run(config) report
  }

  RenderPipeline --> RenderList : clears and passes buckets
  RenderPipeline --> InstancedBatcher : groups eligible runs
  InstancedBatcher --> InstancePool : appends payloads, draws run
  RenderList --> RenderItem : Opaque and Shadow buckets
  ModelMesh --> RenderItem : constructs, sets eligibility and payload
  ModelMesh --> ModelMeshPart : per-part items share instance id
  ModelMesh --> RenderScaleOptions : LOD gate
  RenderItem --> Material : InstancedPipeline selects the run pipeline
  MeshDepthShaderResources --> Material : shared shadow, instanced variant
  RenderingScaleProbeRunner --> RenderPipeline : reads Statistics and RenderSpan
```

Instanced frame flow (opaque pass; the shadow pass is identical with the depth material and
a world-only payload):

```mermaid
sequenceDiagram
  participant RP as RenderPipeline.Execute
  participant S3D as Scene3D.BuildRenderList
  participant MM as ModelMesh.BuildRenderListWithWorldMatrix
  participant B as InstancedBatcher
  participant P as InstancePool
  participant CL as CommandList

  RP->>S3D: walk objects (per-object frustum cull as today)
  S3D->>MM: model transform + condition state
  MM->>MM: LOD gate (screen radius < threshold, return)
  MM->>S3D: RenderItem (InstancingEligible + payload) to Opaque and Shadow
  RP->>RP: sort once per bucket, filter visible per pass
  loop each contiguous eligible run (same pipeline, index buffer, range)
    RP->>B: TryBeginRun(item)
    B->>P: Append(item.Payload) for each instance
    B->>CL: UpdateBuffer(instance buffer range, start UBO)
    B->>CL: SetGraphicsResourceSet(3, instance set)
    CL->>CL: SetPipeline(material.InstancedPipeline) if changed
    CL->>CL: SetIndexBuffer once per run
    CL->>CL: DrawIndexed(indexCount, runLength, startIndex, 0, 0)
  end
  RP-->>RP: Statistics (batches, instances, draws), RenderSpan spans
```

## 7. Measured baseline

Filled by `WI-B5-01` (before any B5 rendering change) and updated by `WI-B5-05` (after).
Host: offscreen Vulkan (RADV, Radeon RX 7600) headless probe, 1024x768, Enhanced preset
unless noted. Scene: 10k repeated rigid models, idle, fixed camera.

| Metric | Baseline (per-item path) | After B5-2..B5-4 |
|---|---|---|
| Frame (update + render + GPU drain), median ms | TBD | TBD |
| Render span only, median ms | TBD | TBD |
| Opaque + Shadow items per frame | TBD | TBD |
| Draw calls per frame | TBD | TBD |
| Instanced batches / instances per frame | 0 / 0 | TBD |
| FPS vs 33.3 ms budget | TBD | TBD |

Anchor-composition gap (10k skinned Rangers, no instancing possible): TBD by `WI-B5-05`.

## 8. Validation

- Standard build/test/format/brace checks (`agent/README.md#standard-validation`).
- GraphicsFact unit tests for the instanced shader families and batching (headless Vulkan
  gate via `GraphicsFactAttribute`), including the pixel A/B comparison.
- Probe runs on this host for numbers; screenshots through the probe for evidence.
- Track G regressions: `GraphicsSettings`-related suites, shadow (`ShadowSettingsTests`,
  `ShadowFrustumCalculatorTests`), `SingleSamplePipelineVariantTests`, linear-shading and
  proportions tests stay green; the visual-regression golden manifest is host-pinned, so
  any intended pixel change would require regenerating goldens on the reference host and is
  treated as a deviation here (B5 requires pixel-identical output for eligible items).

## 9. Related

- [Track B](../track-b-scale.md): phase row and coordination note.
- [A3 sim/presentation seam](a3-sim-presentation-seam.md): B5 lives entirely on the
  presentation side and reads logic state only.
- [B1 benchmarking](b1-benchmarking.md): record schema and statistics reused by the probe.
- [G8 shadow quality](g8-shadow-quality.md): shadow cascades the submit improvements must
  not regress.
- Scale anchor Decision: [open-questions.md](../open-questions.md).
