# Deep Dive: Age of Empires III: Definitive Edition models (C10a, C10b, C10c, C10d)

> Expands phases C10a through C10d of [Track C: External Models & Animations](../track-c-external-assets.md).
>
> Audience: human design for reading Age of Empires III: DE `.bar` archives and importing Granny
> 3D `.gr2` unit models and animations through the C1-C3 contracts.

Purpose: add a gunpowder-era external format whose units bind tech- and age-dependent model
variants to shared Biped animation clips. The archive, texture, and material layers are simple; the
risk concentrates in the proprietary Granny 3D container and its optional section compression.

Facts below were measured on 2026-10-14 against an operator-supplied install with temporary Python
parsers outside the repository.

---

## 1. Scope

C10a supports:

- `.bar` version 6 archives under `Game/`, exposed as a read-only, case-insensitive `FileSystem`;
- entry compression flag 0 (stored) and 1 (`alz4`: LZ4 block);
- `.XMB` binary XML (`X1`, `XR` version 8) decoded to an XML tree;
- `.ddt` (`RTS3`) textures passed through as BC1, BC2, BC3, BGRA, or R8 images.

C10b supports:

- uncompressed Granny `.gr2` file format version 7 (64-bit little-endian magic), read generically
  from the embedded type trees, with pointer fixups and mixed marshalling;
- static meshes, vertex data, topology groups, and the skeleton bind pose;
- `.material.XMB` with `materialdef` inputs BaseColor, Normals, Masks, and Details mapped onto C3
  metallic-roughness;
- Granny `ArtToolInfo` units and axes normalized to engine space.

C10c supports:

- `BoneBindings` and per-vertex weights as C1 skins of up to four influences;
- per-clip `.gr2` animations with `DaK32fC32f` transform tracks (Position, Orientation,
  ScaleShear) resampled to C1 TRS channels;
- animfile XML: `GrannyModel` and `GrannyAnim` asset references, tech `logic` variants, and
  `frombone`/`tobone` attachments.

C10d (deferred) supports Oodle1 (section type 2) and BitKnit2 (type 4) Granny section
decompression and any non-float curve codecs, only after the review in section 6.

C10 rejects:

- compressed Granny sections before C10d, with file, section index, and compression type;
- encrypted sound entries (flag 2, `Sound.bar` only), which models never need;
- `.gr2.precomp` and `.xml.precomp` engine caches, which are not read.

## 2. Archives

42 `.bar` files hold about 46 GB stored and 75 GB decompressed; no model files are loose.
`Art/ArtUnits.bar` (968 MB, 13,640 entries) holds unit models; `Art/Art.bar` (759 MB) holds the
shared `animation_library` (2,512 clips).

- Header: `ESPN`, u32 version 6, u32 `0x44332211`, zero padding to `0x114`, u32 checksum, u32 file
  count, u64 file-table offset at `0x120`.
- File table: UTF-16LE root path, entry count, then per entry u64 offset, u32 decompressed size,
  u32 stored size, u32 padded size (2 KB alignment), UTF-16 relative path, u32 compression flag.
- Names are plaintext paths, for example `Art\units\artillery\falconet\falconet.gr2`. The parser
  consumed every table in all 42 archives exactly to end of file.
- `alz4`: magic, u32 decompressed size, u32 compressed size, u32 version 1, raw LZ4 block.

## 3. Granny models and animations

16,459 `.gr2` files exist, 4,942 in `ArtUnits.bar`. Exporter: Granny Standard Exporter 2.11.9.1
from Maya scenes. The engine links Granny statically.

| Sample | Uncompressed | Oodle1 section | BitKnit2 sections |
|---|---|---|---|
| 400 unit models | 340 | 60 | 14 |
| 80 library clips | 79 | 1 | 0 |

Type trees name their fields, so files are self-describing: `Skeletons/Bones/ParentIndex/
Transform/InverseWorldTransform`, `Meshes/PrimaryVertexData/TriTopologies/Groups/BoneBindings`,
and the vertex layout `Position, Normal, Tangent, Binormal, DiffuseColor0, TextureCoordinates0,
BoneWeights, BoneIndices`. Granny material and texture records reference source PNGs and are
ignored in favor of `.material.XMB`.

Skeletons are Biped style (`Bip01_Pelvis` through `Bip01_Prop1/2`). LODs are separate files
(`lp_*.gr2`, `*_low.gr2`). Each clip is its own `.gr2`; all sampled curves use
`CurveDataHeader_DaK32fC32f` (uncompressed float knots and controls).

## 4. Textures and materials

`.ddt` header: `RTS3`, u8 usage, u8 alpha, u8 format, u8 mip count, u32 width, u32 height, then
mip offset and size pairs. Format codes: 1 BGRA, 4 BC1, 5 BC1 with alpha, 7 R8, 8 BC2, 9 BC3.
44,740 files, all `alz4` in the archive.

`Render\materialdefs\default.materialdef.XMB` declares BaseColor (sRGB), Normals, Masks, and
Details plus AO, roughness, and metallic range sliders. Open: the Masks channel layout, confirmed
empirically in C10b.

## 5. Unit binding and proof units

`Data\protoy.xml.XMB` gives each unit an `<animfile>`. The animfile holds `<component>` blocks with
tech `logic` variants pointing to `GrannyModel` files, `<anim>` blocks pointing to `GrannyAnim`
files, attachments, and `definebone` entries. Infantry and cavalry reuse library clips on the
shared Bip01 skeleton.

| Unit | Model | Clips | Note |
|---|---|---|---|
| Musketeer (286) | `Art\units\infantry_ranged\musketeer\musketeer_4age.gr2` | `animation_library\range\march_rifleman`, `range\volley\volley_standing_fire` | uncompressed |
| Hussar (308) | `Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2` | `animation_library\cavalry\horse_idle_A` | uncompressed |
| Falconet (310) | `Art\units\artillery\falconet\falconet.gr2` | `falconet_walk`, `falconet_limber`, `falconet_reload` | Oodle1, C10d proof |
| Frigate (441) | `Art\units\naval\frigate\frigate.gr2` | `frigate_idle`, `frigate_deathanim` | naval alternative |

Textures live in `ArtUnitsTextures*.bar`, for example
`...\musketeer\textures\musketeer_4age_matA_{BaseColor,Normals,Masks,Details}.ddt`.

## 6. References and licensing

- Resource Manager (github.com/eBaeza/Resource-Manager, MIT): BAR v6, `alz4`, XMB, DDT. Usable
  with credit.
- LSLib (github.com/Norbyte/lslib, MIT): type-tree-driven Granny v6/v7 reader. Its decompressor is
  a native C++/CLI component; C10d reviews its provenance before any port, and C10b does not
  depend on it.
- The Granny SDK is proprietary with no public format specification.
- No AoE3 asset is committed. Assets are read from the operator's install only.

## 7. Phase split

| Phase | Deliverable | Proof |
|---|---|---|
| C10a | `BarFileSystem`, LZ4, XMB, DDT passthrough | All 42 archives enumerate; musketeer textures decode with recorded sizes |
| C10b | Generic uncompressed GR2 reader, static meshes, materials on C3 PBR | Musketeer and hussar horse render textured beside a Generals Ranger |
| C10c | Skins, float-curve clips, animfile resolution with variants and attachments | Musketeer march and volley clips and hussar idles bind and capture deterministically |
| C10d | Oodle1 and BitKnit2 section decompression after license review | Falconet imports and plays `falconet_limber` |

## 8. Risks

- The generic Granny reader (64-bit pointer layout, fixup tables, type-tree marshalling) is the
  largest single piece of work in C10.
- About 15% of unit models stay unloadable until C10d; C10b reports them explicitly.
- Tech and age variants mean one unit maps to several models; C10c selects one variant per request.

## 9. Architecture diagrams

`†` Placeholder. Class and sequence diagrams grounded in the C1 importer seam, the C3 materializer,
and the new BAR, XMB, and GR2 readers are required before the first C10 source-edit card is marked
`Ready`.
