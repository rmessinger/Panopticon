# RecoilEngine Rendering Port Analysis

> Feasibility study (2026-08) for adopting graphics subsystems from RecoilEngine
> (`/home/rmessinger/RecoilEngine`, a fork of the Spring RTS engine used by Beyond All Reason)
> into OpenSAGE. Companion analysis to the roadmap: relevant to Track G (rendering clarity,
> including the open terrain mid-height void) and Track B phase B5 (instancing and batching).

## Summary

Verdict: legally clean, technically a rewrite rather than a port. What translates is algorithms
and shaders, not code.

- RecoilEngine's renderer is roughly 67k LOC of C++ (311 files under `rts/Rendering`, plus
  `rts/Map` terrain at ~10k LOC) written against a raw OpenGL compatibility profile: fixed-function
  `glMatrixMode`/`glColor` calls mixed with GL4 SSBOs and `glMultiDrawElementsIndirect`.
- OpenSAGE renders through Veldrid 4.9 (Vulkan/D3D11/Metal backends) with SPIR-V shaders
  cross-compiled at load time and immutable descriptor sets. Almost no line-to-line translation
  exists between the two models.
- License is compatible: RecoilEngine is GPL v2 or later, OpenSAGE is GPL v3. Code may flow
  (Recoil `LICENSE`; OpenSAGE `LICENSE.md`).

## What does not map

| RecoilEngine element | Why it is not portable |
|---|---|
| GL compat-profile backend (`GlobalRendering.cpp`, glad loader, SDL2 window) | OpenSAGE has no GL backend in practice: the Veldrid.SPIRV 1.0.15 emitter bug makes OpenGL unusable for terrain/water shaders (`src/OpenSage.Rendering/Shaders/ShaderCrossCompiler.cs`). Immediate-mode GL state has no Veldrid equivalent; per-frame `glUniform` caching (`Shaders/ShaderStates.h`) maps to descriptor sets and `ConstantBuffer<T>` with different lifetime rules. |
| Dual model render paths (legacy GLSL vs GL4, `Common/ModelDrawer.h`) | Exists to support 2005-era hardware. OpenSAGE needs exactly one path. |
| Lua extensibility layer (`LuaObjectDrawer`, Lua materials, Lua shaders, deferred composition, post-processing) | RecoilEngine's engine side has no bloom/tonemap/HDR; Beyond All Reason implements those in game-side Lua. There is nothing to take except the hook pattern. |
| Shadow handler (`Rendering/ShadowHandler.cpp`) | Single orthographic shadow map, no cascades. OpenSAGE's CSM implementation is already more capable. |
| Model formats (3DO/S3O/assimp/gltf parsers under `rts/Rendering/Models`) | OpenSAGE consumes W3D. Not applicable. |
| Legacy ARB assembly shader path, `VertexArray` immediate emulation | Dead weight. |

## Porting candidates, ranked by value

| Rank | Subsystem | RecoilEngine source | Value to OpenSAGE | Est. size |
|---|---|---|---|---|
| 1 | Terrain LOD + streaming | `CRoamMeshDrawer` (`rts/Map/SMF/ROAM/`, ROAM tessellation reacting to camera and map damage); `CSMFGroundTextures` (per-square PBO texture streaming with mip-level bias) | OpenSAGE has fixed 17x17 patches, no LOD, and a whole-map texture array built up front (`src/OpenSage.Game/Terrain/Terrain.cs`). Highest pain-to-value ratio; also the natural vehicle for killing the terrain mid-height void bug class by replacing the fragile shadow-map/ResourceSet interaction (`GlobalShaderResourceData.cs`, `ShadowMapRenderer.cs`). Serves G7 and B4a. | 4-6k LOC |
| 2 | Instanced model drawing | `S3DModelVAO` (`rts/Rendering/Models/3DModelVAO.cpp`): shared vertex/index VBOs, per-frame instance buffer, one `glMultiDrawElementsIndirect` submission; team colors via palette SSBO (`ModelsDataUploader.cpp`) | OpenSAGE has no instancing; every mesh part is its own draw. This is Track B5. Maps to Veldrid indirect draws plus device buffers. | 2-4k LOC |
| 3 | BumpWater | `Env/BumpWater.cpp` (~1k LOC) + `BumpWater*.glsl` (coast blur, shore waves/foam, wakes) | OpenSAGE water has reflection/refraction but stubbed bump texture, waves, and depth colors (`WaterMapRenderer.cs`). | 2-3k LOC |
| 4 | Sky and ambient | `ModernSky`, `CubeMapHandler` (per-face specular cube updates), `SkyLight`/`SunLighting` | OpenSAGE lighting is static; serves G6 (gamma/linear shading) groundwork. | 1-2k LOC |
| 5 | Distance unit icons and model LOD | `Units/UnitDrawerData.cpp` icon fade distances; Lua material LOD orders | OpenSAGE has neither; required at Track B entity counts. | 1-2k LOC |
| 6 | Info textures | `Map/InfoTexture/Modern/*`: LOS/radar/path overlays rendered into a texture, combined by one shader, sampled by the world pass | The pattern (render overlays offscreen, sample in terrain shader) is how SAGE shroud/LOS should be built. | 1k LOC |
| 7 | Glyph-atlas text | `Rendering/Fonts/CFontTexture.cpp` (FreeType glyph atlas) | OpenSAGE `TextCache` re-rasterizes whole strings to CPU textures per change. | 0.5-1k LOC |
| 8 | Grass, ground decals | `Env/GrassDrawer`, `Env/Decals/` | Cosmetic, low priority. | 1-2k LOC |

Total for ranks 1-7: roughly 15-25k LOC of C# plus adapted GLSL, against the current rendering
code's ~3k LOC of hand-written engine shaders and pipeline code.

## Risks

1. Terrain void first. The open bug (terrain draws nothing at camera heights roughly 900-1500
   world units, nondeterministic at zone edges; workaround `MinimumSafeCameraHeight` in
   `src/OpenSage.Tools.ReplaySketch.Core/Services/ValidationGroundCamera.cs`, regression guard in
   `src/OpenSage.Game.Tests/Tools/ReplaySketch/ValidationGroundCameraTests.cs`) sits in the
   terrain/shadow/ResourceSet interaction. Porting terrain over an unexplained failure mode risks
   carrying the bug into the new code. Root-cause before rank 1 starts.
2. Resource-binding redesign per subsystem. Veldrid's immutable descriptor sets, ResourceSet
   recreation, and update-after-use rules differ fundamentally from immediate GL state. Every
   ported subsystem needs a binding-layer design, not transliteration.
3. No visual-regression infrastructure. Track G phase G3 (golden-frame harness) does not exist
   yet; porting terrain and water blind is how voids ship. G3 is a prerequisite.
4. Async texture streaming redesign. PBO streaming maps to Veldrid staging textures and staged
   copies; the threading model differs (GL context confined to main thread in Recoil).
5. Shader adaptation. Recoil GLSL targets GL 3.0-4.5 desktop; OpenSAGE authors GLSL 450 compiled
   to SPIR-V and cross-compiled per backend. Terrain shaders in particular must keep OpenSAGE's
   texelFetch-based 3-texture directional blending semantics for existing maps.

## Recommended sequence

1. Root-cause the terrain mid-height void (Track G correctness item; prime suspects:
   `ShadowMapRenderer.cs` ShadowData recreation, `GlobalShaderResourceData.GetForwardPassResourceSet`
   disposal of in-use sets, mid-frame camera mutation in `RenderPipeline.cs` water and shadow
   passes, and the `in_ViewSpaceDepth`/`CascadeSplits` unit mismatch in `Terrain.vert`/`Shadows.h`).
2. Land G3 visual-regression harness (golden frames plus metrics, headless).
3. Phase one port: terrain. ROAM-style dynamic tessellation plus per-square texture streaming,
   designed against Veldrid staging uploads, replacing the fixed-patch mesh. Validate with G3
   goldens and the existing camera-framing test.
4. Phase two: instanced model drawing (B5), then water (BumpWater port), then sky/ambient, icons,
   info textures, text, in that order.

Each phase is independently shippable and reversible behind the G2 settings presets if needed.
