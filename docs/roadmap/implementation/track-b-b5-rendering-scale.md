# Track B · B5 : Rendering at scale

Source of truth: [deep-dives/b5-rendering-at-scale.md](../deep-dives/b5-rendering-at-scale.md) :
[Architecture diagrams §6](../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams).

Checklist item: B5 Rendering at scale (GPU instancing, LOD/impostors, culling,
render-bucket/shadow).

| Card | Sub-phase | Scope |
|---|---|---|
| `WI-B5-01` | B5-1 | Render-path counters and render-scale probe; measured baseline into the deep dive |
| `WI-B5-02` | B5-2 | Source-agnostic GPU instancing: payload seam, instanced shader families, batched draws, pixel A/B |
| `WI-B5-03` | B5-3 | Small-feature LOD gate; sort once per frame, filter per pass |
| `WI-B5-04` | B5-4 | Render-bucket/shadow submit improvements: index-buffer bind hoist, debug-group gating |
| `WI-B5-05` | B5-5 | Anchor-scene budget verification, numbers, evidence artifacts |

## Implementation sequence

Strictly ordered. Each card leaves build, tests, format, and brace checks green.

1. `WI-B5-01` first: no point optimizing before the probe exists, and the baseline numbers
   are the contract the later cards are judged against.
2. `WI-B5-02` lands the seam (eligibility flag on `RenderItem`, payloads, instanced
   pipeline variants, batching in `DoRenderPass`), gated by
   `RenderPipeline.InstancingEnabled`.
3. `WI-B5-03` lands the LOD gate and the cull/sort split; independent of instancing and
   measured separately.
4. `WI-B5-04` lands the remaining submit-overhead removals in the same code paths.
5. `WI-B5-05` runs the verification matrix at the scale anchor (instancing on/off, LOD
   on/off, shadow tiers, rigid and skinned compositions), records the numbers and gaps in
   the deep dive, and produces the evidence screenshot.

## Code touch points

All under `src/` unless noted:

- `Panopticon.Simulation/Graphics/Rendering/`: `RenderPipeline`, `RenderItem`,
  `RenderItemCollection`, new `Instancing/` (instance pool, batcher, options).
- `Panopticon.Simulation/Graphics/`: `ModelMesh`, `ModelMeshPart`, `ModelMeshInstance`.
- `Panopticon.Simulation/Graphics/Shaders/`: `MeshShaderResources` (instance layout),
  `FixedFunctionShaderResources`, `MeshDepthShaderResources`,
  `PbrMetallicRoughnessShaderResources`, `ShaderSetBase` (mixed vert/frag names).
- `Panopticon.Simulation/Assets/Shaders/`: `Mesh.h`, new instanced shader files.
- `Panopticon.Rendering/`: `Material` (`InstancedPipeline`), `ShaderCrossCompiler`
  (vert/frag name overload).
- `Panopticon.Tools.TestHarness/`: `BuiltInScenario`, `RenderingScaleProbeRunner`,
  `Program`, `TestHarnessConfig`.
- `test/Panopticon.Simulation.Tests/Graphics/`: instancing and batching tests.

Out of scope for B5 (recorded as gaps in the deep dive, not silently dropped): skinned-mesh
instancing (per-instance skinning buffer), impostors, multi-LOD asset import,
`NormalMapped`/`Simple` instanced variants.
