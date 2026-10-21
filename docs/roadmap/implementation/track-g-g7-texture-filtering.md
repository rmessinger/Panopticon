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

## Temporal validation follow-up

`TerrainZoomProof` adds a fixed zoom-out/back sequence, single-sample 4x/16x anisotropy controls,
an Enhanced terrain-filtering candidate, and a matching supersampled reference sequence.
Per-pair temporal sampling-error and sharpness scores replace fixed-camera consistency as the
motion evidence. Capture recipe, metric definition, tolerance and interpretation limits:
[terrain zoom temporal comparison](../../screenshots.md#recipe-terrain-zoom-temporal-comparison).

Fractional world-sampler mip bias remains unsupported by the integer sampler field.

2026-10-20 evidence: Direct3D11, NVIDIA GeForce RTX 5090, 1024x768 native and 2048x1536 reference,
4x MSAA throughout. All 33 measurement crops were reviewed: grass and cliff terrain only, without
roads, water or foliage. Mean temporal error was 0.008896 at 4x and 0.008366 at 16x, a 5.96 percent
reduction; the 0.009350 regression limit passed. Mean sharpness was 0.002271/0.002248 versus
0.002104 for the reference. Fine texture grain remains visible; the result is a measured relative
improvement, not a rendering fix or evidence of zero shimmer. Evidence directory:
`20261020_125402_4de85c2fad844e0aa8c8d6c92d443880` beneath the configured output root.

## Superseded runtime experiments

The runtime four-subpixel filter was removed in favor of the offline data policy below. Its
captures remain comparison evidence, not the current renderer's behavior.

A rejected +0.5 terrain mip-bias experiment scored 0.009936 versus the 0.008366 baseline and
softened detail. Its artifacts remain in `20261020_130639_24880d2366494b868d3aa7f259bbfded`.
Runtime subpixel-filter validation, on the same D3D11 host as the earlier capture:

| View | Baseline temporal error | Filtered temporal error | Reduction | Sharpness: baseline / filtered / reference | Evidence directory |
|---|---|---|---|---|---|
| Grazing | 0.008366 | 0.005303 | 36.62 percent | 0.002248 / 0.002040 / 0.002104 | `20261020_131403_2a99e35013894db2911930f76cd4f052` |
| Default tactical pitch | 0.018427 | 0.005516 | 70.06 percent | 0.004647 / 0.003201 / 0.003327 | `20261020_131735_5effd67558ff46cfad256a9fb64e54a1` |

Both historical gates passed; their measurement crops contain only grass and cliff terrain.

## Offline terrain texture packs

`GraphicsSettings.PrefilteredTerrainTextures` defaults to true for Enhanced and false for Legacy.
`WithPrefilteredTerrainTextures(false)` selects the original mip path for comparison. The flag is
session-pinned; changing it requires a restart, reported by capability validation. Proportion
copies preserve the flag. The terrain shader, material constants and per-frame render path retain
their original sampling behavior.

`TerrainTextureBake` parses `Data/INI/Terrain.ini` through a data-only asset store and resolves its
declared images through the installation filesystem, including loose overrides. Missing declared
images are listed in the bake report. Only square terrain sources participate. Every source is
baked for each declared array size at least as large as that source, preserving map layer order
and cell-size metadata at load time. Map-scoped baking prepares only that map's sources and array
size.

`PrefilteredTerrainMipBuilder` constructs each level directly from linear-light source pixels:
area integration for reduction, periodic bilinear interpolation for enlargement, then a separable
`[0.125, 0.75, 0.125]` periodic filter. The final chain mixes 20 percent of this low-pass result
with 80 percent of the original linear Lanczos chain, in linear light. The conservative mix limits
grazing-angle detail loss. Alpha is made opaque as in the original terrain path.
Non-power-of-two square sizes are supported.
`LinearMipBuilder` encoding accepts segmented ImageSharp buffers, including larger terrain sizes.

`TerrainTexturePack` keys immutable mip packs by builder version, SHA-256 of the resolved source
bytes, and target array size. PNG levels store sRGB-encoded pixels; a manifest records their
checksums and dimensions. A new pack is published by directory rename only after all levels are
written. Valid packs are reused; corrupt existing packs and incomplete staging directories are
preserved rather than overwritten. Derived licensed content remains outside the repository and
original installation. Root selection and bake commands:
[offline terrain texture bake](../../screenshots.md#recipe-offline-terrain-texture-bake).

During map load, Enhanced verifies and uploads matching prebuilt levels without resizing or mip
generation. Missing, stale or corrupt packs fall back to the original generation path with a
warning. `Terrain.PrefilteredTextureCount` and `MissingPrefilteredTextureCount` expose coverage;
`TerrainZoomProof` requires complete coverage before passing. Legacy never reads the pack.
Ordinary runtime texturing has no additional samples or supersampling render targets.

Offline-pack validation on the same D3D11 host:

| View | Original-data temporal error | Baked-data temporal error | Reduction | Sharpness: original / baked / reference | Evidence directory |
|---|---|---|---|---|---|
| Grazing | 0.008366 | 0.008266 | 1.19 percent | 0.002248 / 0.001952 / 0.002104 | `20261020_134952_778eb87182e44be599f20c0131b3fcf8` |
| Default tactical pitch | 0.018427 | 0.013804 | 25.09 percent | 0.004647 / 0.003976 / 0.003327 | `20261020_134952_f8463775fb414927941b8d3826454891` |

Both gates passed with 14 prebuilt textures and zero generation fallbacks per scene. Every
measurement crop, frames 0 through 32, was reviewed for terrain-only content. The conservative
data filter reduces normal-view grain less than the removed runtime filter, but avoids its extra
per-pixel work and does not regress the grazing-angle gate. The initial fully filtered pack
was rejected: grazing error 0.016437 versus 0.008366 original, despite normal-view improvement.
Those artifacts remain in `20261020_134151_ffdbd2c276444b2aa3c4a8bb3e4bf1c2` and
`20261020_134152_a2a4971c54ba4863b63173c3e9c9c6a9`.

Validation: 78 focused tests passed, covering linear-light filtering, periodic edges, non-power-of-two
and segmented images, pack reuse, source invalidation, missing/corrupt levels, integrity metadata,
session pinning, capture profiles and temporal scoring. Enhanced visual goldens were not updated.

The full local bake prepared 225 available source images across 695 array-size variants, totaling
437 MiB for the active builder version. Seven unavailable INI declarations are listed in
`bake-report-20261020_135617-564e1360b519422faf25f5c835ac7fdd.json`. Prior experimental pack versions
remain preserved but are not loaded.
