# G7 Implementation: Texture Filtering

> Audience: agent. Card sequence and evidence for [G7](../deep-dives/g7-texture-filtering.md).

## Scope

Configurable world sampler and explicit terrain mip policy, not global sampler replacement.
Source of truth: [design and diagrams](../deep-dives/g7-texture-filtering.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G7-01 | Sampler consumer/cache table and terrain mip inventory | none |
| WI-G7-02 | Effective anisotropy/bias, invalidation and terrain builder | WI-G7-01, WI-G2-03, WI-G3-03 |

## Audit (WI-G7-01, complete)

Produced by WI-G7-01 against `2f15eadb1`.

### Sampler inventory

Two anisotropic sampler sources exist; the split is the phase's central finding.

| Sampler | Created at | Description | Lifetime |
|---|---|---|---|
| `StandardGraphicsResources.Aniso4xClampSampler` | `Graphics/StandardGraphicsResources.cs:40-52` | `SamplerDescription.Aniso4x` with U/V/W `Clamp`, `MaximumAnisotropy = (uint)settings.Anisotropy`, `LodBias = (int)MathF.Round(settings.MipLodBias)` | One per game session, `DisposableBase`-owned |
| `StandardGraphicsResources.LinearClampSampler` | `StandardGraphicsResources.cs:54-60` | `SamplerDescription.Linear` with U/V/W `Clamp` | Same |
| `StandardGraphicsResources.PointClampSampler` | `StandardGraphicsResources.cs:62-68` | `SamplerDescription.Point` with U/V/W `Clamp` | Same |
| `GraphicsDevice.Aniso4xSampler` | vendored `src/Veldrid/GraphicsDevice.cs:806-836` | `SamplerDescription.Aniso4x` verbatim: U/V/W `Wrap`, `Filter.Anisotropic`, `MaximumAnisotropy 4`, `LodBias 0`; throws unless `Features.SamplerAnisotropy` | One per device, lazily cached, never recreated |
| `GraphicsDevice.LinearSampler` / `PointSampler` | vendored `GraphicsDevice.cs` | `SamplerDescription.Linear` / `.Point`, `Wrap` | Same |

The settings-driven clamp sampler honours `GraphicsSettings.Anisotropy` and `MipLodBias` today,
but the device sampler most world draw paths bind is fixed at 4x with zero bias regardless of
settings. Making anisotropy configurable therefore means introducing a settings-driven
`Wrap`-addressed world sampler, not just editing `Aniso4xClampSampler`.

### Consumers

| Consumer | file:line | Sampler | Draw path |
|---|---|---|---|
| `FixedFunctionShaderResources` texture-stage sets | `Graphics/Shaders/FixedFunctionShaderResources.cs:151` | device `Aniso4xSampler` | W3D fixed-function object texturing (primary world path) |
| `TerrainShaderResources` array set | `Graphics/Shaders/TerrainShaderResources.cs:46` | device `Aniso4xSampler` | terrain blend array, the G7 milestone path |
| `RoadShaderResources` | `Graphics/Shaders/RoadShaderResources.cs:39` | device `Aniso4xSampler` | road decal texturing |
| `ShaderMaterialResourceSetBuilder` trailing slot | `Graphics/Shaders/ShaderMaterialResourceSetBuilder.cs:40` | device `Aniso4xSampler` | every NIF/ShaderMaterial family resource set (NormalMapped, Simple) |
| `WaterMapRenderer` water texture slot | `Graphics/Rendering/Water/WaterMapRenderer.cs:184` | device `Aniso4xSampler` | water time-of-day colour texture |
| `GlobalShaderResourceData` cloud slot | `Graphics/Shaders/GlobalShaderResourceData.cs:102` | device `Aniso4xSampler` | cloud attenuation data tiling in the forward pass set (data role, Wrap needed) |
| `GlobalShaderResourceData` decal slot | `GlobalShaderResourceData.cs:107` | `Aniso4xClampSampler` | radius-cursor decals in the forward pass set |
| `RadiusCursorDecalShaderResources` | `Graphics/Shaders/ShaderResourceManager.cs:68` | `Aniso4xClampSampler` | cursor decal draw |
| `RenderPipeline.Execute` 2D default | `Graphics/Rendering/RenderPipeline.cs:235` | `LinearClampSampler` | `DrawingContext2D` default after 3D |
| `PbrMetallicRoughnessShaderResources` neutral | `Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs:188` | `LinearClampSampler` | fallback when a PBR material binds no sampler |
| `CameraFadeOverlay` | `Scripting/CameraFadeOverlay.cs:92` | `LinearClampSampler` | fullscreen fade quad |
| `MappedImageUtility` | `Gui/Wnd/Images/MappedImageUtility.cs:104` | `PointClampSampler` | mapped-image UI stretch |
| `AptRenderingContext` | `Gui/Apt/AptRenderingContext.cs:82` | `LinearClampSampler` | Apt UI rendering |
| `WaterMapRenderer` refraction/reflection | `WaterMapRenderer.cs:186-188` | device `LinearSampler` | water map sampling |

The shadow comparison sampler (`GlobalShaderResources.ShadowSampler`), UI samplers and the
output-pass point sampler are separate roles outside this phase.

### Mip LOD bias expressibility

`SamplerDescription.LodBias` (vendored `src/Veldrid/SamplerDescription.cs:46`) answers the
question definitively: a bias is expressible on every backend call site, but the field is an
`int`. Vulkan consumes it as `mipLodBias` (`VkSampler.cs:41`, `samplerLodBias` feature enabled),
D3D11 as `MipLODBias` (`D3D11Sampler.cs`), OpenGL via `GL_TEXTURE_LOD_BIAS`. The Metal backend
ignores `LodBias` entirely (`MTLSampler.cs` never reads it); a sub-integer bias on Metal needs
a shader-side offset, which stays out of scope and must be recorded as a fallback when WI-G7-02
widens the field to `float`. `GraphicsSettingsController.Apply` passes `Anisotropy` and
`MipLodBias` through to `GraphicsSettings.Create` with no clamping today; no finite-bias or
range check exists anywhere.

### Anisotropy limits

`ResourceFactory.CreateSampler` throws when `Filter.Anisotropic` is requested without
`Features.SamplerAnisotropy`. Veldrid does not expose a device maximum-anisotropy limit:
`VkSampler` consumes `MaximumAnisotropy` raw, D3D11 is fixed at 16, `MTLSampler` clamps with
`Math.Max(1, n)`. The Vulkan specification guarantees `maxSamplerAnisotropy >= 16`, so a
`{1, 2, 4, 8, 16}` acceptance set needs no limit query; going above 16 would require
fork-exposing `VkPhysicalDeviceLimits.maxSamplerAnisotropy` and is unnecessary for this phase.

### Terrain mip route versus the general route

Terrain (`Terrain/Terrain.cs:441-545`, post-G6): per blend tile TGA decoded to RGBA8, optionally
resized to the largest tile square (`Legacy`: encoded ImageSharp `Resize` Lanczos3;
`Enhanced`: `LinearMipBuilder.ResizeLinear`), full CPU chain
(`MipmapHelper.GenerateMipmaps` Lanczos3 encoded / `GenerateLinearMipmaps`),
`CreateTextureViaStaging`, then every level is `CopyTexture`-d into the
`R8_G8_B8_A8(_SRgb)` texture array. No runtime generation; layer order follows blend-tile
enumeration. The general route (`MipmapHelper`, `LinearMipBuilder`, `CoverageMipBuilder`) is
documented in the G5 and G6 plans; each level is resampled directly from the base image, never
chained. Nothing in either route applies a mip bias or kernel switch beyond the G6 colour
policy; the resampler at every step is Lanczos3.

### What configurability requires

Samplers are created in constructors and captured by value into `ResourceSet`s; Veldrid offers
no sampler mutation. A settings change therefore means a new sampler plus rebuilt sets:
every `FixedFunctionShaderResources` material set, `TerrainShaderResources`' set,
`RoadShaderResources`' sets, every `ShaderMaterialResourceSetBuilder` set,
`WaterMapRenderer._resourceSet`, `GlobalShaderResourceData._forwardPassResourceSet`, and the
`RadiusCursorDecalShaderResources` set. The G2/G4 session-pinning precedent already governs
`SampleCount` (constructor-pinned, mid-session requests clamp back with a reported fallback);
anisotropy and bias belong on the same contract rather than a live-rebuild path.

## Validation and risks

Default bias remains zero. Pure tests cover bounded finite bias and fallback. Rebind global and
radius-cursor resources on switching. Legacy mips retain existing bytes. G6's linear filtering
policy composes when active; G7 may land independently. Record aligned temporal-region thresholds.

## Implementation (WI-G7-02, complete)

World sampler. `StandardGraphicsResources` owns a `WorldAnisoSampler` built from the applied
settings: the vendored `SamplerDescription.Aniso4x` clone (Wrap address modes, trilinear
filter) with `MaximumAnisotropy` clamped by `GraphicsSettingsController.ClampAnisotropy` into
the accepted set {1, 2, 4, 8, 16} (no `SamplerAnisotropy` feature collapses to 1) and
`LodBias` from the bounded bias. The Legacy preset reproduces the previous fixed device
sampler member for member, so Legacy rendering is byte-identical. Enhanced raises anisotropy
to 16; both presets start at zero bias.

Bias validation. `GraphicsSettingsController.Apply` rejects non-finite bias (fallback to 0)
and clamps the finite remainder into the initial public override range [-1, 1], reporting a
fallback string for each correction. The sampler carries the rounded integer bias; the deep
dive's Metal limitation (LodBias ignored) is recorded rather than worked around.

Live rebuild without restart. `Material.MaterialResourceSet` gained a public setter path
(`UpdateMaterialResourceSet`) so a rebuilt set swaps in without changing material identity or
render keys. `RenderPipeline.ApplySamplerSettings(effective)`, called from
`Game.ApplyGraphicsSettingsIfChanged`, recreates the sampler when `WorldSamplerNeedsRebuild`
reports a change and fans out:

- `FixedFunctionShaderResources.RebuildWorldSamplerSets` rebuilds every cached
  material-constants resource set (the set shared by materials) and rebinds each cached
  material.
- `ShaderMaterialShaderResources` caches `CachedMaterial` records (properties, load context,
  alpha-test flags) so `RebuildWorldSamplerSets` can rebuild each set through the same
  creation path, including G5 cutout textures and G6 colour decode.
- `RoadShaderResources` rebuilds its per-texture sets.
- `Terrain.ApplyWorldSampler` rebuilds the terrain set from stored
  `TerrainMaterialSetSources` (the seven buffers and textures captured at construction).
- `WaterMapRenderer.ApplyWorldSampler` rebuilds the water set from the stored time-of-day
  texture.
- `GlobalShaderResourceData.InvalidateForwardPassResourceSet` drops the forward-pass set; the
  existing lazy getter recreates it on next use with the new sampler.

Replaced sets are retired through `RemoveAndDispose` after their successors are bound; the
retired sampler is disposed only after the rebuild fan-out completes, so no draw observes a
destroyed object mid-frame. The audit's rebuild list is fully covered except the radius-cursor
decal set, which binds the Clamp sampler role and is out of the world-sampler scope.

Terrain policy. The audit found no defect in the Lanczos3 terrain chain, so the resampling
path is unchanged; this card adds the sources capture needed for set rebuilds only.

## Evidence (WI-G7-02)

Unit tests: preset diff extended (16 vs 4 anisotropy), `ClampAnisotropy` rounding cases
(32->16, 15->8, 3->2, 0->1), `Apply` fallback strings for clamped anisotropy, non-finite and
out-of-range bias, and `WorldSamplerRebuildTests` (three GraphicsFacts) proving the sampler
recreates, cached sets swap without material identity change, and invalid parameters report
fallbacks. Suite: 53 + 355 + 2116 passed, 7 skipped (`Category!=Interactive`).

Visual regression: gate run one proved Legacy byte-identical (faithful 7/7 golden Passed) with
Enhanced failing 7/7 as expected from the 4x->16x change; enhanced goldens regenerated and the
final gate passed 14/14 scenes. Grazing-angle evidence from terrain-grazing: supersampled
EdgeAliasing 0.0639 at 4x versus 0.0517 at 16x (19 percent lower aliasing). The harness has
no temporal metric; the card's shimmer check is covered by the aliasing delta plus fixed-camera
frame consistency, recorded as a harness limitation.
