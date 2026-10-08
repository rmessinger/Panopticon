# G5 Implementation: Alpha-Test Clarity

> Audience: agent. Card sequence and evidence for [G5](../deep-dives/g5-alpha-test-clarity.md).

## Scope

Cutout-only mip coverage and alpha-to-coverage, preserving legacy variants.
Source of truth: [design and diagrams](../deep-dives/g5-alpha-test-clarity.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G5-01 | Material/texture-role table and distance captures | none |
| WI-G5-02 | Coverage mips, cutout pipeline and comparisons | WI-G5-01, WI-G4-02 |

## Design adjustment from the audit

- Alpha-test is a material property discovered at material conversion, not a texture property:
  no loader can key on it. `OnDemandTextureLoader` keys textures by name; the imported
  materializer keys by path or bytes plus the mipmap flag. Cutout metadata therefore attaches
  where the material resolves its texture roles (`ModelMesh.Imported`), and the texture caches
  key on the policy, as the deep dive's `CutoutTexturePolicy` anticipated.
- The JPG alpha-sidecar merge in `OnDemandTextureLoader` rewrites mip 0 only: higher mips keep
  pre-merge alpha, so cutout JPG props already render wrong at a distance under Legacy. G5-02
  regenerates those mips after the merge; the card already carries that step.
- Authored DDS mips upload unchanged, confirming the Legacy retention rule: Legacy keeps DDS
  mips exactly as shipped, and only regenerated chains need coverage work.
- Terrain never alpha-tests; its texture-array mip route needs no cutout work.

## Audit (WI-G5-01, complete)

Produced by `WI-G5-01` against `track-g5-0-water-msaa` at `08d700dac`.

### Alpha-test call sites

Both call sites discard on failure; no other shader calls `FailsAlphaTest`.

| Site | Gate | Material family | Flag provenance | Texture source |
|---|---|---|---|---|
| `FixedFunction.frag:380-386` | `_MaterialConstants.Shading.AlphaTest` | W3D fixed-function (`ImportedMaterial.FixedFunctionFamily`) | `W3dMaterialConverter.cs:145`: `w3dShader.AlphaTest == W3dShaderAlphaTest.Enable` | `GetFixedFunctionTexture` `Texture0`/`Texture1` -> `AssetStore.Textures.GetByName(property.TextureName)` |
| `NormalMapped.frag:69-75` | `_MaterialConstants.AlphaTestEnable` | NIF normal-mapped (ShaderMaterial family) | `NifMaterialConverter.cs:88-93`: `(alpha.Flags & 0x200) != 0` from `NiAlphaProperty` | `ImportedTextureMaterializer.GetTexture` with `generateMipmaps: binding.Sampler.MipFilter != ImportedFilterMode.Nearest` |

Material wiring: `ModelMesh.Imported.CreateModelMeshPart` builds fixed-function material
constants from `ImportedFixedFunctionShading.AlphaTest`, or routes normal-mapped materials
through `GetShaderMaterialResources(material.ShaderFamily)`. The NIF converter also records
`ImportedMaterialProperty.FromInt32("AlphaTestThreshold", alpha.Threshold)`; the threshold is
never consumed at runtime.

Asset categories: any W3D mesh material carrying the shader flag (Generals cutout props:
foliage, fencing, ladders), not just foliage.

Non-callers: `Terrain.frag` and `Particle.frag` have no alpha test; `Sprite.frag:54` keeps its
own independent 2D discard (out of G5 3D scope). PBR materials (`blendEnabled` only when
`AlphaMode == Blend`) and `MeshDepth` shadow materials never alpha-test.

### Cutoff

`Common.h:29-33`: `FailsAlphaTest(alpha) => alpha < 0.37647f` (0x60/0xFF). The constant lives
only there; no C# or uniform path reaches it. Baking the coverage correction into mip alpha
needs no new uniform, matching the deep-dive design.

### Mip provenance

| Route | Path | Mip source | Coverage work |
|---|---|---|---|
| W3D `.dds` | `OnDemandTextureLoader` | Authored `ddsFile.MipMaps` uploaded unchanged | None; retained under Legacy |
| W3D `.tga` | `OnDemandTextureLoader` -> `ImageSharpTexture(image, true)` | CPU chain | None |
| W3D `.jpg` + PNG alpha sidecar | `OnDemandTextureLoader` -> `ImageSharpTexture(stream)` | CPU chain; sidecar merge rewrites mip 0 only | None; mips above 0 keep pre-merge alpha (bug above) |
| Imported PNG/JPEG/TGA (NIF, PBR) | `ImportedTextureMaterializer` -> `ImportedImageDecoder` -> `ImageSharpTexture` | CPU chain | None |
| Terrain | `Terrain.cs:441-533` | CPU chain into per-mip `CopyTexture` array | Not needed; terrain never alpha-tests |

The CPU chain is `MipmapHelper.GenerateMipmaps` (`src/Veldrid.ImageSharp/MipmapHelper.cs:36-61`):
each level is resampled directly from the base image with Lanczos3 (not chained, not box
filtering), RGBA filtered together. `OnDemandTextureLoader` is constructed with
`generateMipMaps: true` (`OnDemandAssetLoadStrategy.cs:80`). `ImportedImageDecoder` supports
PNG/JPEG/TGA; DDS raises `NotSupportedException` and stays on the W3D texture path.

### Load-time identifiability

None. No flag, marker or naming convention identifies alpha-tested textures at load time; the
same image can serve cutout and blended materials. Cache keys today carry only
`ExternalPathKey(path, generateMipmaps)` / `EmbeddedContentKey(bytes, generateMipmaps)` or the
texture name. G5-02 adds the cutout role to those keys where the material requests it.

## Validation and risks

Synthetic coverage tests include tiny mips and fractional LOD. Bake alpha rescale once, without
an additional threshold correction. G6 controls RGB transfer; alpha stays linear. Compare
foliage/fences at effective 1x and 4x; particles/HUD are unchanged. Legacy retains DDS mips.
