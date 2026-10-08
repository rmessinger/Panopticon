# G6 Implementation: Gamma-Correct Rendering

> Audience: agent. Card sequence and evidence for [G6](../deep-dives/g6-gamma-correct-rendering.md).

## Scope

Role-aware texture transfer, linear RGB filtering and one SDR output encoding.
Source of truth: [design and diagrams](../deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G6-01 | Colour/data role and transfer-boundary inventory | none |
| WI-G6-02 | Linear texture/render variants and dithered output | WI-G6-01, WI-G2-03, WI-G3-03 |

## Audit (WI-G6-01, complete)

Produced by `WI-G6-01` against `track-g5-2-a2c-mips` at `8843b8dde`. Read-only; no source touched.

### Texture role classification

| Category | Data means | Role | Load site | Format today |
|---|---|---|---|---|
| W3D diffuse/detail maps (`Texture0`/`Texture1`) | Authored sRGB colour | Colour | `OnDemandTextureLoader` (`Content/Loaders/OnDemandTextureLoader.cs`): DDS via `CreateStaticTexture2D`, TGA/JPG via `ImageSharpTexture` (srgb default false) | `BC1/BC2/BC3_UNorm`, `R8_G8_B8_A8_UNorm` |
| NIF diffuse maps (`DiffuseTexture`) | Authored sRGB colour | Colour | `ImportedTextureMaterializer`/`ImportedImageDecoder` -> `ImageSharpTexture` | `R8_G8_B8_A8_UNorm` |
| NIF normal maps (`NormalMap`) | Tangent-space xyz | Data | Same materializer route | `R8_G8_B8_A8_UNorm` |
| PBR base colour + emissive | Authored sRGB colour | Colour | Same materializer route (`ResolveRole`) | `R8_G8_B8_A8_UNorm` |
| PBR metallicRoughness, occlusion | Physical parameters | Data | Same | `R8_G8_B8_A8_UNorm` |
| W3D bump DuDv | Tangent-space delta | Data | `OnDemandTextureLoader` DDS fourcc `BumpDuDv` | `R8_G8_SNorm` |
| Terrain blend tiles + macro texture | Authored sRGB colour | Colour | `Terrain.CreateTextures` (`Terrain.cs:441-533`): TGA -> Lanczos3 resize -> `ImageSharpTexture` chain -> array | `R8_G8_B8_A8_UNorm` array |
| Water per-time-of-day textures | Authored sRGB colour | Colour | `WaterMapRenderer` binding 1 (`waterSet.WaterTexture`) | `R8_G8_B8_A8_UNorm` |
| Cloud texture (`Global_CloudTexture`) | Light-attenuation mask | Data | `GlobalShaderResourceData` from `AssetStore` | `R8_G8_B8_A8_UNorm` |
| Particle textures (`ParticleTexture`) | Colour + alpha | Colour | `ParticleShaderResources` via `AssetStore.Textures` | `R8_G8_B8_A8_UNorm` |
| UI/sprite art (`GuiTextureAsset`, ImGui atlas) | Authored sRGB colour | Colour (G9 surface) | `OnDemandGuiTextureLoader` (separate loader; dedupe TODO) | `R8_G8_B8_A8_UNorm` |
| Placeholder/solid (`Null`/`SolidWhite`/`SolidBlack`/`Placeholder`) | Transfer-invariant constants | Data | `StandardGraphicsResources.cs:62-86` | `R8_G8_B8_A8_UNorm` |
| Shadow map | Depth | Data | `ShadowMapRenderer` | `D32_Float_S8_UInt` |
| Water reflection/refraction depth | Depth | Data | `WaterData.cs:10,13` | `D32_Float_S8_UInt` |
| Water reflection/refraction colour | Rendered scene colour | Colour RT | `WaterData.cs:9,12` | `B8_G8_R8_A8_UNorm` |
| Intermediate/composition targets | Rendered scene colour | Colour RT | `RenderPipeline.EnsureIntermediateFramebuffer` | `B8_G8_R8_A8_UNorm` |

Six data-map categories must stay linear: NIF normals, W3D bump DuDv, PBR metallicRoughness/occlusion, cloud attenuation, shadow depth, water auxiliary depths.

### sRGB usage today

No runtime code selects an sRGB pixel format anywhere. `ImageSharpTexture` has srgb ctor
variants but every call passes false or omits the flag. `GraphicsDeviceOptions`
(`GraphicsDeviceUtility.cs:28`) never sets `SwapchainSrgbFormat`, so the main swapchain uses the
backend default (`B8_G8_R8_A8_UNorm`). `PixelFormatExtensions`, `FrameCapturer` and
`ImageQualityMetrics` merely accept SRgb formats as valid classifications.

### Lighting operates on gamma-space inputs (confirmed)

`DoLighting` is defined in `Assets/Shaders/Lighting.h:48` and called from `FixedFunction.frag:299`,
`NormalMapped.frag` and `Terrain.frag:256`. Every input is an encoded 8-bit value or an authored
gamma constant; the multiplications and sums happen in encoded space:

- `FixedFunction.frag`: `objectColor = texture.xyz * saturate(diffuse + ambient + emissive) [+ specular] * cloudColor`.
- `NormalMapped.frag`: `objectColor = texture.xyz * diffuseColor + specular`, then `* cloudColor`.
- `Terrain.frag`: `diffuseColor * textureColor * cloudColor * macroTextureColor + decalColor`.

PBR is a self-contained linear island: `PbrMetallicRoughness.frag` decodes with `SrgbToLinear`
(base colour `:159`, emissive `:245`), shades linear, and encodes with `LinearToSrgb` at `:249`.
When the intermediate becomes linear, this in-shader output encode must move to the output pass
to avoid double encoding.

Fog is not implemented in any shader (`Simple.frag:37` carries a `TODO: fog`;
`GameData` `FogAlpha`/`FogOfWarOn` and `Weather` `HardwareFogColor` are parsed but unconsumed by
the renderer). The sky is a flat clear colour (`RenderPipeline.ClearColor`, 105/255 grey); no sky
gradient exists. G6-02 validation therefore rests on light-falloff gradients, cloud modulation
and mip-minification scenes rather than fog/sky banding.

### Mip resample colour space

`MipmapHelper.GenerateMipmaps` (`src/Veldrid.ImageSharp/MipmapHelper.cs:36-61`) computes each
level as `baseImage.Clone(ctx.Resize(newW, newH, KnownResamplers.Lanczos3))`: a Lanczos3
resample applied directly to encoded `Rgba32`, sourced from the base image per level, RGBA
filtered together, no linear-light conversion. `CoverageMipBuilder` (G5) resamples the same
encoded space by design. Terrain tiles are resized with `LanczosResampler.Lanczos3` in encoded
space before the chain build. Authored DDS mips upload byte-identical and are never re-filtered
(`OnDemandTextureLoader.CreateTextureFromDds`).

### Output chain and dither

3D pipelines write `_intermediateFramebuffer` (`B8_G8_R8_A8_UNorm`, store sample count), resolve
into `_compositionFramebuffer` (same format, Count1), 2D/UI composites on top, then
`TextureCopier` blits composition to the swapchain with a SpriteBatch draw (SingleDisabled blend,
point sampler, no colour math). No tonemap or format conversion exists at any stage, and no
dither exists anywhere before the 8-bit output (repo-wide search for `dither` returns nothing).

## Validation and risks

Black/white colour mip is approximately sRGB 188; data mip is 128. Alpha is unchanged. Test
transfer endpoints and double encoding. Dither is bounded, zero-mean and screen-coordinate
stable. Legacy uses the unchanged legacy path. Coordinate texture cache policy with G5/G7.
