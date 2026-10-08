# G4 Implementation: MSAA

> Audience: agent. Card sequence and evidence for [G4](../deep-dives/g4-msaa.md).

## Scope

Main 3D sample count and resolve; 2D draws on the single-sample resolved target.
Source of truth: [design and diagrams](../deep-dives/g4-msaa.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G4-01 | Pipeline/attachment/sample-count inventory | none |
| WI-G4-02 | Effective descriptions, targets, resolve and captures | WI-G4-01, WI-G2-03, WI-G3-03 |

## Audit (`WI-G4-01`, complete)

State after G2/G3: `GraphicsSettings.SampleCount` flows through
`RenderPipeline.CreateGameOutputDescription(GraphicsSettings)` into `ShaderSetStore.OutputDescription`
and the intermediate framebuffer already sizes by
`GraphicsSettingsController.ToTextureSampleCount(settings.SampleCount)`; the controller clamps the
requested count against `GraphicsDevice.GetSampleCountLimit(D32_Float_S8_UInt, depthFormat: true)`
and reports requested/effective (G2). This audit inventories every consumer that must change when
the count becomes greater than 1.

### GraphicsPipelineDescription sites (12)

The card's earlier "nine sites, one ImGui" count predates G2; the tree now has 11 game-owned sites
plus one vendored ImGui pipeline.

Main scene pass, all reading the store description (`store.OutputDescription` / `Store.OutputDescription`),
which `Game.cs:575` builds once from `CreateGameOutputDescription(GraphicsSettings)` (the store is
not rebuilt on a runtime preset swap):

| # | Site | Pass |
|---|---|---|
| 1 | `TerrainShaderResources.cs:17` | terrain tiles |
| 2 | `WaterShaderResources.cs:19` | water surface |
| 3 | `RoadShaderResources.cs:20` | roads |
| 4 | `TracerShaderResources.cs:23` | tracers |
| 5 | `ParticleShaderResources.cs:29` | particles |
| 6 | `ProjectileStreamShaderResources.cs:26` | projectile streams |
| 7 | `ShaderMaterialShaderResources.cs:34` | fixed-function model pass |
| 8 | `FixedFunctionShaderResources.cs:96` | fixed-function pipeline cache (key `PipelineKey`, output from store at `:103`) |
| 9 | `PbrMetallicRoughnessShaderResources.cs:142` | PBR pipeline cache (same pattern, `:149`) |
| 10 | `SpriteShaderResources.cs:63` | sprites/UI via `GetCachedPipeline(blend, outputDescription)`; per-caller output, see below |
| 11 | `MeshDepthShaderResources.cs:25` | shadow depth pass, output is `ShadowData.DepthPassDescription` |

Site 12 is `src/Veldrid.ImGui/ImGuiRenderer.cs:149`, vendored, swapchain output, diagnostics only:
out of game scope.

Sprite callers (site 10) pass their own descriptions: `DrawingContext2D.cs:43` receives
`RenderPipeline.CreateGameOutputDescription(game.GraphicsSettings)` (2D/UI path);
`TextureCopier.cs:18-21` uses `game.Panel.OutputDescription` (swapchain); `MappedImageUtility.cs:92`
uses its own single-sample image-processing framebuffer.

### Framebuffer and target structure

`RenderPipeline.EnsureIntermediateFramebuffer` creates the intermediate depth (`D32_Float_S8_UInt`)
and colour (format of `target.ColorTargets[0].Target.Format`, `RenderTarget | Sampled`) at the
settings sample count and rebuilds on size or count change. `Execute` order: shadow cascades
(`:214` per-cascade `SetFramebuffer`), water refraction (`:295`) and reflection (`:316`) into
`WaterData` offscreen textures, main framebuffer bind (`:344`; no-scene clear path binds at `:150`),
then the 2D block draws into the same bound framebuffer, then `TextureCopier.Execute` blits
`_intermediateTexture` to `context.RenderTarget`. `_intermediateTexture` has no consumers outside
`RenderPipeline.cs`. `DrawingContext2D` and `SpriteBatch` never call `SetFramebuffer`; they draw
into whatever is bound.

### Single-sample keeps

Shadow depth (`ShadowData.cs:50/62`), water reflection/refraction maps and framebuffers
(`WaterData.cs:35-72`), image processing (`MappedImageUtility.cs:41-91`), terrain tile and macro
textures (`Terrain.cs:150/483/556`), radius cursor decals (`RadiusCursorDecals.cs:63`), text cache
(`TextCache.cs:289`), the null texture (`StandardGraphicsResources.cs:55`), capture staging
(`FrameCapturer.cs:25`) and `RenderTarget` (`:42/51`) stay single-sample: they are either sampled
data textures, auxiliary passes with their own descriptions, or CPU-facing staging. No auxiliary
output inherits the main count.

### Output-description sharing and the 2D split

`ShaderSetStore.OutputDescription` feeds all 11 main-pass sites including the 2D/UI description
passed to `DrawingContext2D`. With a count above 1 the 3D pass renders into the multisampled
intermediate while 2D must stay single-sample (card step 4), so the 2D/UI path needs a `Count1`
description and the render loop needs one split point: resolve the multisampled intermediate into
a single-sample composition target after the 3D block, bind the composition framebuffer for the 2D
block, and blit the composition texture. At count 1 the composition target is the intermediate
itself with no resolve, so the Legacy path stays byte-identical. The shadow depth pass, water
maps and ImGui are already separate. `FrameCapturer` captures the swapchain target after the final
blit, so capture is unaffected. The one resolve site is `TextureCopier`'s source texture
(`RenderPipeline.cs:183`), which becomes the resolved/composition texture.

## Implementation (`WI-G4-02`, complete)

Authoritative sample count: `ShaderSetStore.OutputDescription.SampleCount`. Pipelines are built
once with it, so `EnsureIntermediateFramebuffer` sizes the intermediate attachments from the store
description rather than the live settings; a mid-session preset swap can never desynchronize
framebuffer from pipelines. `GraphicsSettingsController.Apply` takes the session sample count
(after device clamp) and clamps later requests back to it with the fallback note
"SampleCount N -> M (session was constructed at N; restart to change)"; `Game` passes the store
count as that ceiling, so requested/effective reporting stays honest.

Composition and resolve: at counts above 1 `EnsureIntermediateFramebuffer` also creates a
single-sample composition colour texture plus an inert `D32_Float_S8_UInt` depth attachment. The
depth attachment is never written by 2D, but the composition framebuffer must declare it: 2D
pipelines' output description carries the depth format, and a colour-only framebuffer fails
Veldrid's pipeline/framebuffer compatibility check (found on the Enhanced HUD scene during
bring-up). `Execute` resolves `_intermediateTexture` into the composition texture immediately
after the 3D pass (`CommandList.ResolveTexture`), binds the composition framebuffer for the 2D
block, and `TextureCopier` blits the composition texture. At count 1 the composition objects are
null: the intermediate is used directly, no resolve is recorded, and the command stream is
byte-for-byte the pre-G4 shape.

2D description split: `RenderPipeline.CreateGameOutputDescription` gained a
`(GraphicsSettings, TextureSampleCount)` overload; `DrawingContext2D` is constructed with an
explicit `Count1` while the 3D pass keeps the settings-derived count.

Preset selection: `GraphicsSettings.Enhanced()` sets `SampleCount` 4 (plus the existing shadow
tier) and is the game default; `Game` renders Enhanced 4x in sessions that do not pass an
explicit preset. The preset reaches construction through a new optional `Game` constructor
parameter `graphicsSettings`, applied before graphics-resource construction; `ScenarioCaptureRunner`
passes `CaptureRunSettings.GraphicsSettings` there (the post-construction assignment was removed).
Mid-session count changes report the session fallback instead of applying. The pre-rename
`Faithful` preset is `Legacy`, unchanged at 1x.

## Validation and risks

Capability tests; supported 1/2/4/8 captures; repeated switches/resizes. Resolve precedes 2D and
the final sampled blit. Legacy 1x is exact; 4x reduces aliasing in G3 silhouette regions.
Target reuse checks count and format as well as dimensions. G5 owns cutout coverage state.

### Evidence (`WI-G4-02`)

- Gate run before golden update: all six `faithful` scenes golden Passed with zero differing
  pixels (Legacy is byte-identical to pre-G4); all six `enhanced` scenes regressed
  (fractions 0.008-0.028), confirming 4x reached every pass.
- Silhouette metric (`unit-silhouettes`): EdgeAliasing 0.0725 at 1x vs 0.0707 at 4x;
  supersampled references score 0.0641/0.0627; `SilhouetteImproved` true for both presets.
- Enhanced goldens regenerated on the reference host via `TESTHARNESS_VR_UPDATE_GOLDENS=1`;
  only the six enhanced manifest entries changed (hash/scores), faithful hashes unchanged.
  Post-update gate exits 0 with 12 goldens passing.
- 2x and 8x exercised by temporarily editing the preset sample count and running the full
  harness each time: all scenes captured, no `VeldridException`, no pipeline/framebuffer
  compatibility errors.
- Session clamp: requesting a higher count than construction reports the session fallback rather
  than applying it.
