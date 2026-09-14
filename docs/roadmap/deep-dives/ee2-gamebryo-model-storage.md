# Deep Dive: Empire Earth 2 model storage survey

> Audience: human. A read-only survey of how Empire Earth II Gold stores, cooks, and binds unit
> models, animations, and textures. Evidence for [Track C](../track-c-external-assets.md): a
> complete second-engine asset corpus (Gamebryo) used to sanity-check that the
> [C1 import seam](c1-import-abstraction.md) is format-agnostic beyond W3D and glTF. Performed
> 2026-09-14 against a local install; no OpenSAGE source changes.

## 1. Scope and method

Analyzed the retail Empire Earth II Gold install (GOG, with the community Unofficial Patch stack)
sibling to this repository. Tools: `unzip` listings, hex dumps, and a Python NIF header parser
written against the niftools [nif.xml](https://github.com/niftools/nifxml) `Header` spec. The
parser walked every model and animation file in the corpus and produced a block-type census; it
did not decode block bodies. Reproduction commands are in the appendix.

## 2. Packaging

All game assets ship in standard PKZIP archives under `zips/`, layered by version-suffixed
overrides loaded in ascending order: `graphics.zip`, `graphics_110.zip`, `graphics_120.zip`,
`Graphics_160.zip`; the same pattern for textures and for the `db_*.zip` database stack. There is
no proprietary container. Contents by archive:

| Archive | Contents |
|---|---|
| `graphics.zip` | 1638 `cache/nifcache/*.nifcache` models, 1371 `Graphics/` `*.kf` + 56 `*.KF` animations, one nested zip |
| `Graphics_160.zip` | 132 `.nifcache` override models (Unofficial Patch 1.6) |
| `textures.zip`, `Textures_160.zip` | `cache/texcache/*.texcache` textures, `cache/pcpcache/*.pcpatch` player-color patches |
| `db.zip` | 228 `.ddf` definitions, 70 `.csv`, 66 `.utf8` text, 10 `.cfg` |
| `hdrs.zip` | 74 `.h` headers enumerating database fields |

## 3. Models

`.nifcache` files are unmodified Gamebryo NIF files: header string `Gamebryo File Format, Version
10.1.0.0` (version word `0x0A010000`, user version 0). All 1638 model files share this version.
nif.xml attributes version 10.1.0.0 to Empire Earth II among other titles. The extension marks
them as cooked by the art pipeline (section 4); the runtime resolves the database's source name
(`nwg1_galley.nif`) to `cache/nifcache/nwg1_galley.nifcache`.

Header layout (version 10.1.0.0), verified across the corpus:

| Field | Type | Notes |
|---|---|---|
| Header string | text | ends with `\n`, not NUL-terminated |
| Version | u32 LE | `0x0A010000` |
| User version | u32 LE | 0 for all models |
| Num blocks | u32 LE | |
| Num block types | u16 LE | |
| Block types | sized strings | u32 LE length + ASCII |
| Block type index | u16 x num blocks | maps each block to a type |
| Num groups | u32 | 0 everywhere |

Block census over the 2610 files this layout parses (all 1638 models plus the 10.1-flavor
animations):

| Blocks | Count | Role |
|---|---|---|
| `NiNode` | 23161 | transform hierarchy (skeleton bones, attachment points) |
| `NiTriShape` / `NiTriShapeData` | 5529 / 5522 | triangle meshes (one shape per sub-mesh with its own transform) |
| `NiSkinInstance` / `NiSkinData` | 1457 / 1457 | per-vertex skinning (up to 4 bone influences) |
| `NiLODNode` / `NiRangeLODData` | 948 / 948 | distance-based LOD switches |
| `NiMaterialProperty` | 4475 | ambient/diffuse/specular/emissive colors |
| `NiTexturingProperty` / `NiSourceTexture` | 4296 / 2345 | texture stages, by-name texture refs |
| `NiAlphaProperty`, `NiZBufferProperty`, `NiStencilProperty`, `NiVertexColorProperty`, `NiSpecularProperty` | 1459-2649 each | fixed-function render state |
| `NiStringExtraData`, `NiIntegerExtraData`, `NiBooleanExtraData` | 11511 / 1514 / 1514 | engine metadata (e.g. `HOUSECOLOR`-style flags) |
| `NiKeyframeController` / `NiKeyframeData` | 28560 / 28560 | animation tracks (in `.kf`) |
| `NiControllerSequence`, `NiTextKeyExtraData` | 1018 / 1131 | animation clips and event markers |
| `NiParticles` family | ~1300 total | effects (`fx_*` models only) |

## 4. Cooking configuration

`db/Visuals/db_model_convert.csv` configures the asset pipeline per source folder: max vertex
count, mesh LOD count, bone LOD count, texture resize target, per-axis scale, light removal, and
a modulate-4x flag. Example row: the root `graphics/` folder is capped at 600 vertices, 0 mesh
LODs, 2 bone LODs, 256x256 textures. `db_texture_convert.csv` does the same for textures
(compression, mipmaps, alpha generation). Damage states are separate cooked models
(`NWG1_Galley_damage01..03.nifcache`) swapped by the engine, not vertex-level damage.

## 5. Animations

Animations are standard Gamebryo `.kf` files (clip = `NiControllerSequence`, one
`NiKeyframeController` + `NiKeyframeData` per animated node, `NiTextKeyExtraData` event markers).
The corpus spans three NIF vintages:

| Flavor | Files | Notes |
|---|---|---|
| 10.1.0.0, user version 0 | 972 | same layout as the models |
| 10.0.1.0 (`0x0A000100`), user versions 8-170 | 452 | older header layout; per-file Mad Doc user versions; the 10.1 parser does not fit them |
| NetImmerse 4.2.2.0 (`0x04020200`) | 3 | legacy |

Gameplay binding lives in `db/Units/*.ddf`. A `UnitModel` block names the model and maps visual
states to animations, with sound/FX event keys inline:

```ini
UnitModel WarGalley_01
{
    parent      = BaseShip
    DefaultModel = nwg1_galley.nif
    States = [
        { StateName = Idle  AnimName = NWG1_Galley_idle.kf }
        { StateName = Walk  AnimName = NWG1_Galley_walk.kf
            TextKeys = [{ Name = START_SOUND_UNTIL_ANIM_CHANGE Data = fx_move_boat_2 KeyTime = 0}] }
        { StateName = Attack1
            TextKeys = [{ Name = PLAY_SOUND Data = fx_bow_shoot KeyTime = 0.5 }] }
        { StateName = Death }
    ]
    ChildNames = [ WarGalleySail TheBoatWake2 ExplSelectorNaval Galley_FlyingParts ]
}
```

One unit type has one `UnitModel` per epoch (`WarGalley_01`, `_03`, `_05` with different models),
which is the same shape as Generals' `ModelConditionState` model swapping.

## 6. Textures

`cache/texcache/*.texcache` are also NIF 10.1.0.0 containers holding a single `NiPixelData`
block. Models reference textures by source name (`NiSourceTexture` -> `nwg1_galley.tga`) and the
runtime resolves the name to `<name>.texcache`, mirroring Generals' by-name texture lookup.
Player colors are applied through `cache/pcpcache/<name><A..K>.pcpatch` pixel patches (e.g.
`nwg1_galley_sailA..K`), one per player slot, patched onto the base texture at runtime.

## 7. Mapping onto the C1 interchange model

| EE2 / Gamebryo concept | C1 interchange type | Fit |
|---|---|---|
| `NiNode` tree | `ImportedSkeleton` / `ImportedBone` | NIF bones are named nodes linked by reference; C1 uses parent indices. Conversion is a tree flatten, and bone identity is by name (helps C2 remapping). |
| `NiTriShape` + data + shape transform | `ImportedMesh` / `ImportedSubObject` | One NiTriShape per sub-mesh, each with its own transform and properties; maps to sub-object + mesh. |
| `NiSkinInstance` / `NiSkinData` | `ImportedVertex` skin fields | NIF carries up to 4 influences per vertex against the runtime's two-bone limit; the C1 rule is no silent truncation, so an importer must reject >2 nonzero influences until the runtime widens. |
| `NiMaterialProperty` + texture/alpha/z/stencil properties | `ImportedMaterial` + texture-name properties | Fixed-function state maps to the fixed-function shading fields and render-state enums; textures stay by-name. |
| `NiLODNode` / `NiRangeLODData` | none | Runtime has no LOD-node concept in the C1 milestone; a decision for a later phase (drop, or bake the highest LOD). |
| `.kf` `NiControllerSequence` | `ImportedAnimation` / `ImportedAnimationChannel` | Per-node named tracks must be resolved to bone indices against a skeleton; channels are translation/rotation/quaternion keyframes, within the C1 channel set. |
| `NiTextKeyExtraData` + ddf `TextKeys` | none (INI-side) | Event markers sit both in the file and in the ddf state block; the gameplay-facing contract matches `AnimationState`. |
| ddf `UnitModel` state binding | `AnimationState` / `W3dModelDraw` selection | Importer-neutral in both engines; no interchange impact. |
| `.texcache` / `.pcpatch` | texture by-name lookup | Same contract as `AssetStore.Textures`; the patch system is EE2-specific and out of C1 scope. |
| `NiParticles` (`fx_*`) | none | Effects models are out of C1 scope. |

## 8. Implications for Track C

- The survey validates the C1 seam's shape: separate model and animation importers, a CPU-only
  interchange graph, by-name texture references, and gameplay-side state binding all exist in a
  second commercial engine with no forcing function from OpenSAGE's design.
- NIF 10.1 is a viable second importer candidate after C4's glTF (open spec via nif.xml, mature
  tooling), and this corpus would serve as its parity test set. C4's committed scope is unchanged.
- Known risks if pursued: three animation header vintages in one corpus (including 452 files of
  an older 10.0.1.0 layout with per-file user versions), 4-bone influences versus the two-bone
  runtime limit, LOD nodes, and axis/scale conventions (not verified; nothing was rendered).

## Appendix: reproduction

Install root (sibling of this repository): `Empire Earth II Gold/`.

```sh
# Archive contents and corpus
unzip -l "Empire Earth II Gold/zips/graphics.zip" | grep -c nifcache
unzip -l "Empire Earth II Gold/zips/graphics.zip" | grep -oE '\.[a-zA-Z0-9]+$' | sort | uniq -c

# Header of one model: "Gamebryo File Format, Version 10.1.0.0", version word 0x0A010000
unzip -p "Empire Earth II Gold/zips/graphics.zip" cache/nifcache/nwg1_galley.nifcache | xxd | head -6

# Gameplay binding
unzip -p "Empire Earth II Gold/zips/db.zip" db/Units/war_galley.ddf | sed -n '160,190p'
```

Header parsing reference: niftools nif.xml `Header` struct (spec fields: header string, version,
user version, num blocks, num block types (u16), block types, block type index, num groups; see
`Header` and `FileVersion` in [nifxml](https://github.com/niftools/nifxml)). The census parser
(~60 lines, stdlib only) and extracted samples were kept out of the repository; the block-type
counts above are its output over the full corpus.
