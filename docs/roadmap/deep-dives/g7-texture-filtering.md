# Deep Dive: Texture Filtering and Mip Quality (G7)

> Part of [Track G](../track-g-rendering-clarity.md).
>
> Audience: human + agent. Configurable world samplers and terrain mip policy.

## 1. Purpose and current problem

`StandardGraphicsResources.Aniso4xClampSampler` uses `SamplerDescription.Aniso4x` and clamp
addressing. `GlobalShaderResourceData` binds it in the forward resource set; radius cursor
resources also consume it. `Terrain.CreateTextures` expands smaller TGAs to the largest square
size with Lanczos3, then generates mips through `ImageSharpTexture`. `MipmapHelper` already uses
Lanczos3, so changing from a claimed box filter is not the phase's deliverable.

## 2. Constraints

- Faithful keeps 4x, zero bias and the existing resample/mip path.
- G6 owns colour-space-aware filtering. G7 must compose with it without requiring G6 to land first.
- Shadow comparison samplers, tile-data point sampling and UI samplers are separate roles.
- Negative bias cannot substitute for antialiasing; image sharpness alone is not a quality gate.

## 3. Design

Replace the world sampler's fixed name with a proposed configurable world-sampler binding.
G2 stores requested/effective anisotropy and mip LOD bias. Accept 1/2/4/8/16; choose the device's
supported maximum no higher than requested and report the effective value. Reject non-finite
bias and constrain the initial public override to [-1, 1]. Enhanced starts at zero bias; tuning
requires temporal evidence, not a single sharp frame.

Sampler keys include address mode, filter, anisotropy and bias. On switching, rebuild every
resource set that captured the old sampler, including the global forward set and radius cursor
resources; retire the old sampler only after queued commands complete.

Extract a proposed `TerrainMipBuilder` with explicit Faithful/Enhanced policies. Enhanced avoids
unnecessary repeated resampling, filters each mip at its target footprint, and applies G6's
linear-colour policy when active. Preserve texture-array layer order, cell size, wrap semantics
and cliff/blend metadata. The audit selects a tested kernel and records whether array padding
or edge handling is actually necessary; no speculative asset rewrite.

| Sub-phase | Card | Deliverable |
|---|---|---|
| G7-1 | WI-G7-01 | Sampler consumers, device limits and terrain filtering inventory. |
| G7-2 | WI-G7-02 | Configurable sampler, binding invalidation and explicit terrain mip policy. |

## 4. Validation

- Pure tests cover anisotropy fallback, finite bias limits and complete sampler cache keys.
- Faithful generated terrain mips match the existing builder's bytes.
- G3 grazing-angle captures compare 4x and effective 16x over identical camera positions.
- A fixed camera-motion sequence compares aligned terrain regions for temporal shimmer; masks
  exclude water, particles and intentional animation. Enhanced must not worsen the recorded
  temporal metric while improving legibility in the fixed regions.
- Switching presets updates global and cursor resource sets without stale disposed samplers.

## 5. Architecture Diagrams

Target design; terrain builder and sampler policy are proposed.

```mermaid
classDiagram
  class GraphicsSettings
  class WorldSamplerPolicy
  class StandardGraphicsResources
  class GlobalShaderResourceData
  class TerrainMipBuilder
  class Terrain
  GraphicsSettings --> WorldSamplerPolicy
  StandardGraphicsResources --> WorldSamplerPolicy
  GlobalShaderResourceData --> StandardGraphicsResources : bind effective sampler
  Terrain --> TerrainMipBuilder
  TerrainMipBuilder --> GraphicsSettings : mip and colour policy
```

```mermaid
sequenceDiagram
  participant C as G2 settings controller
  participant S as StandardGraphicsResources
  participant G as GlobalShaderResourceData
  participant T as TerrainMipBuilder
  participant R as RenderPipeline
  C->>S: Build supported anisotropy and bias sampler
  C->>G: Invalidate sampler-dependent binding
  C->>T: Rebuild terrain mips if policy changed
  G->>S: Bind effective sampler
  R->>R: Draw grazing terrain
  Note over R: G3 captures fixed camera-motion sequence
```

## Appendix: current code (agent context)

- `src/Panopticon.Simulation/Graphics/StandardGraphicsResources.cs`: fixed sampler descriptions.
- `src/Panopticon.Simulation/Graphics/Shaders/GlobalShaderResourceData.cs`: forward sampler binding.
- `src/Panopticon.Simulation/Graphics/Shaders/ShaderResourceManager.cs`: radius cursor sampler consumer.
- `src/Panopticon.Simulation/Terrain/Terrain.cs`: largest-size array, resampling and staging copies.
- `src/Veldrid.ImageSharp/MipmapHelper.cs`: current Lanczos3 mip generator.
