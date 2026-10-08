# G2 Implementation: Graphics Settings

> Audience: human overview above the divider; agent-context detail below it.

Implements the G2 row of [Track G](../track-g-rendering-clarity.md). Design source of truth:
[deep-dives/g2-graphics-settings-presets.md](../deep-dives/g2-graphics-settings-presets.md), including its
[architecture diagrams](../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams).

## Scope

Settings ownership and frame-boundary application. Later phases own their visual algorithms.

## Card sequence

| Step | Card | Deliverable | Depends on |
|---|---|---|---|
| 1 | [`WI-G2-01`](../agent/cards/WI-G2-01-audit-render-state-knobs.md) | Knob/default/owner/rebuild/capability tables below | none |
| 2 | [`WI-G2-02`](../agent/cards/WI-G2-02-graphics-settings-type-and-presets.md) | Immutable settings, presets and game ownership | WI-G2-01 |
| 3 | [`WI-G2-03`](../agent/cards/WI-G2-03-wire-settings-into-render-construction.md) | Coherent application, resource invalidation and capture proof | WI-G2-02 |

## Audit summary (WI-G2-01)

Produced against the G1 stack tip (`c8a76ff2b`). Findings that shape G2-2 and G2-3:

- All six knobs are hardcoded engine constants; none reads game data at runtime. The parsed
  `StaticGameLod`, `DynamicGameLod` and `LodPreset` asset collections have zero runtime consumers,
  so no knob can be seeded from Generals INI data except sample count, whose INI names
  (`SampleCount2D`/`SampleCount3D`) exist in Generals-era schema.
- The shadow tier is already a live-switchable knob: `Scene3D.Shadows` is mutable per-scene state,
  the diagnostics `RenderSettingsView` mutates cascades, map size and type at runtime today, and
  `ShadowMapRenderer` lazily rebuilds `ShadowData` when size or cascade count changes. This is the
  safe runtime-switch demonstration G2-3 can build on; no other knob has an existing switch path.
- `RenderPipeline.GameOutputDescription` is static and shared by eight pipeline-construction
  sites. Sample count and colour-space mode both flow through it, so both require pipeline and
  framebuffer rebuilds; anisotropy and mip bias require sampler rebuilds plus resource-set
  recapture; the shadow tier needs no rebuild beyond the existing `ShadowData` recreation.
- Capability clamping has an existing primitive: `GraphicsDevice.GetSampleCountLimit(format,
  depthFormat)` plus `PixelFormatProperties.IsSampleCountSupported`, so requested-versus-effective
  reporting needs no new device query.

---

## Agent context

### Knob table

| Knob | Current value | Fixed at | Consumers that must read settings instead | Rebuild class | Capability limit |
|---|---|---|---|---|---|
| SampleCount | 1 (`TextureSampleCount.Count1`) | `RenderPipeline.cs:24-26` (static `GameOutputDescription`), `RenderPipeline.cs:94-98` (intermediate targets) | 8 `GameOutputDescription` pipeline sites (table below) + intermediate target creation | Pipeline + framebuffer + intermediate texture rebuild | `GraphicsDevice.GetSampleCountLimit`; depth format usually limits below colour |
| AlphaTestMode | Legacy cutoff, threshold `0x60/0xFF = 0.37647` | `Assets/Shaders/Common.h:29-33` (`FailsAlphaTest`) | `FixedFunction.frag:382`, `NormalMapped.frag:71` (both `discard`) | Pipeline rebuild (shader permutation/constant) | None |
| ColourSpaceMode | Legacy gamma: every format UNorm, no sRGB, no linear intermediate | `RenderPipeline.cs:24-26`, `GraphicsDeviceUtility.cs:28-35` (swapchain options), `Water/WaterData.cs:9-13`, `RenderTarget.cs:8-10,47`, `Gui/TextCache.cs:294` | Output descriptions (same 8 sites), texture uploads and font atlas (G6-owned), `FrameCapturer.cs:74-99` already accepts both formats | Pipeline + texture-content rebuild | Device sRGB format support |
| Anisotropy | Fixed 4x | `StandardGraphicsResources.cs:26-32` (`SamplerDescription.Aniso4x` + clamp) | `Shaders/GlobalShaderResourceData.cs:104` (resource sets), `Shaders/ShaderResourceManager.cs:36` (`RadiusCursor`) | Sampler rebuild + resource-set recapture | `device.Features` anisotropy limit |
| MipLodBias | 0 (implicit `SamplerDescription` default) | Veldrid `SamplerDescription.Aniso4x` default; no game-side assignment | Same two sampler consumers | Sampler rebuild + resource-set recapture | None (bounded by validation) |
| ShadowTier | OneCascade, 1024, Soft, Bias 0.002, NormalOffset 0.4, Distance 1200, stabilized | `Rendering/Shadows/ShadowSettings.cs:9-79` defaults; instantiated `Scene3D.cs:101` | `ShadowMapRenderer.cs:45-94`, `ShadowFrustumCalculator`, `GlobalShaderResources.ShadowConstantsPS.Set` (live constants) | Existing `ShadowData` recreation (`ShadowMapRenderer.cs:48-66`); bias/offset are live constants | Shadow map size bounded by device limits |

Runtime mutators found: `Diagnostics/RenderSettingsView.cs:61-150` mutates `Scene3D.Shadows`
(ShadowsType, ShadowMapSize, ShadowMapCascades, ShadowDistance, StabilizeShadowCascades,
VisualizeCascades, UpdateShadowMatrices, VisualizeShadowFrustums) and water settings. No other
site assigns any audited knob; every other consumer is read-only.

### LOD asset collection consumers

| Collection | Registration | Parse site | Runtime consumers |
|---|---|---|---|
| `StaticGameLods` | `Content/AssetStore.cs:161,335` | `Data/Ini/IniParser.BlockParsers.cs:155` | 0 |
| `DynamicGameLods` | `Content/AssetStore.cs:105,279` | `Data/Ini/IniParser.BlockParsers.cs:73` | 0 |
| `LodPresets` | `Content/AssetStore.cs:133,307` | `Data/Ini/IniParser.BlockParsers.cs:116` | 0 |

### Sample-count and output-description inventory

Every `TextureDescription` render-target site and every `OutputDescription` construction:

| Site | Kind | G4 must thread sample count |
|---|---|---|
| `RenderPipeline.cs:94-95` intermediate depth | Texture | Yes (3D path) |
| `RenderPipeline.cs:97-98` intermediate colour | Texture | Yes (3D path) |
| `Rendering/Shadows/ShadowData.cs:49-56` shadow map array | Texture | No (stays single-sample) |
| `Rendering/Water/WaterData.cs:34-49` reflection colour + depth | Texture | No (auxiliary; G4 classifies separately) |
| `Rendering/Water/WaterData.cs:55-70` refraction colour + depth | Texture | No (auxiliary) |
| `RenderTarget.cs:41-48` aux colour target | Texture | No (offscreen/2D) |
| `RenderPipeline.cs:24-26` `GameOutputDescription` | Output | Yes |
| `ShadowData.cs:11-12` `DepthPassDescription` | Output | No |
| `RenderTarget.cs:8-10` `OutputDescription` | Output | No |

`GameOutputDescription` consumers (all pipeline constructions): `Game.cs:573` (`ShaderSetStore`),
`Shaders/TerrainShaderResources.cs:25`, `Shaders/WaterShaderResources.cs:27`,
`Shaders/TracerShaderResources.cs:31`, `Shaders/ParticleShaderResources.cs:37`,
`Scripting/CameraFadeOverlay.cs:34`, `RenderPipeline.cs:73` (`DrawingContext2D`). Total: 6
multisample-relevant texture sites reduce to 2 (intermediate depth + colour), plus 1 output
description feeding 8 pipeline sites.

### INI representability

| Knob | Generals INI representation |
|---|---|
| SampleCount | Yes: `StaticGameLOD.SampleCount2D/SampleCount3D` (Generals-era fields) |
| Anisotropy | Schema only: `UseAnisotropic` is `AddedIn(Bfme)`, absent from Generals data |
| Shadow tier | Schema only: `UseShadowMapping`/`ShadowLOD` are `AddedIn(Bfme2)` |
| AlphaTestMode | None |
| ColourSpaceMode | None |
| MipLodBias | None |

Even where schema fields exist, they are boolean or level-based, not the tier/value shape G2
needs, and no collection has a runtime consumer. G2 settings are engine-owned; the LOD assets
stay parsed-but-unconsumed data and must not become the settings source.

## Application and recreation triggers (WI-G2-03)

Settings flow into construction through two carriers. `RenderPipeline.CreateGameOutputDescription(GraphicsSettings)`
derives the shared output description (sample count; colour format fixed until G6), and `StandardGraphicsResources`
reads anisotropy and mip bias in its constructor. `Game.Render` applies a changed settings instance at the frame
boundary: `GraphicsSettingsController` validates requested values against device capability and produces the
effective snapshot, and `Scene3D.Shadows.ApplyTier` maps the shadow tier onto the existing mutable
`ShadowSettings`, whose `ShadowMapRenderer` recreation path already handles size and cascade changes safely.

| Knob | Reads settings at | Runtime switch path | Recreation trigger |
|---|---|---|---|
| SampleCount | `Game` construction (`ShaderSetStore`, via `CreateGameOutputDescription`); `RenderPipeline` intermediate targets each `EnsureIntermediateFramebuffer` | None in G2; G4 owns | Pipelines via new `ShaderSetStore`; intermediates rebuild on size or sample-count change |
| Anisotropy | `StandardGraphicsResources` constructor | None in G2; G7 owns | Sampler plus dependent resource sets (`GlobalShaderResourceData`, `RadiusCursor`); partial rebuild unsafe |
| MipLodBias | `StandardGraphicsResources` constructor | None in G2; G7 owns | Same as anisotropy; vendored Veldrid stores `LodBias` as `int`, G7 must extend it if sub-integer bias is needed |
| ShadowTier | `Scene3D` construction and `Game.Render` frame boundary | Live: `ShadowSettings.ApplyTier` | Existing `ShadowData` recreation; bias/offset stay live constants |
| AlphaTestMode | Carried only | None; G5 owns the shader path | G5 records it |
| ColourSpaceMode | Carried only | None; G6 owns the format path | G6 records it |

Requested-versus-effective reporting: `GraphicsSettingsController` clamps `SampleCount` to
`GetSampleCountLimit(D32_Float_S8_UInt)` and records fallback reasons; `RenderSettingsView` shows the requested
values with effective sample count where they differ.

Two consumer sites read the description through the load context instead of constructor injection
(`CameraFadeOverlay`, `RenderPipeline.DrawingContext`); the remaining pipeline sites read
`store.OutputDescription`, which is the injected description carrier for shader resource types.

Two audit literals stay intentionally, both owned by later phases: the shader-side alpha-test threshold (G5) and
the colour format literals in `CreateGameOutputDescription` plus swapchain, water and font surfaces (G6). Both
presets carry identical values for those knobs in G2, so no behaviour can differ.

## Validation and risks

Pure default/capability tests; repeat preset switches and resizes; post-G1 Legacy capture.
Resource sets retain sampler/texture references. Partial rebuilds are not safe. Requested and
effective settings must both appear in diagnostics. No simulation or experiment-seam changes.
