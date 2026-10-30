# Deep Dive: Total War: SHOGUN 2 models (C12a, C12b, C12c)

> Expands phases C12a through C12c of [Track C: External Models & Animations](../track-c-external-assets.md).
>
> Audience: human design for reading Total War: SHOGUN 2 Complete Edition packs and importing
> static models, composite soldier parts, and battle animations through the C1-C3 contracts.

Purpose: add the Warscape-era Total War family next to the [C9](c9-total-war-3k-models.md)
THREE KINGDOMS family. SHOGUN 2 shares the pack, DB-table, and fragment concepts with C9 but
none of its binary model or clip layouts, so it is a separate phase group that reuses the C9 pack
seam. Its battles are formation-based like C9 and feed the same [Track M](../track-m-mass-battle.md)
and [B5](../track-b-scale.md) consumers.

Facts below were measured on 2026-10-29 against an operator-supplied SHOGUN 2 Complete Edition
install (`shogun2_complete_edition.exe`, 64-bit, DX11 shaders) with temporary Python parsers
outside the repository. Install root: `/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition`.

---

## 1. Scope

C12a supports:

- `PFHs` packs under `data/` with pack types `0x1`, `0x4`, `0x21`, `0x40`, and `0x41`;
- LZ4 block-compressed entries (pack flag `0x20`), exposed with uncompressed entries as a
  read-only, case-insensitive `FileSystem` across packs;
- `db\<table>_tables\<name>` DB tables for the unit-binding subset in section 6, with schemas
  ported from the RPFM SHOGUN 2 schema subset.

C12b supports:

- `RMV2` versions 0, 1, and 2 static models (buildings, ships, artillery, props);
- `VMPF` version 3 `.variant_part_mesh` soldier and horse parts in bind pose;
- the Complete Edition texture sets `texture_basecolor`, `texture_mra`, `texture_normal`, and
  `texture_colour_mask` on C3 metallic-roughness, from DX10 BC1 sRGB, BC3, BC7, and BC7 sRGB DDS,
  with legacy `texture_diffuse` parts as base colour only.

C12c supports:

- version 1 `.anim` clips with their embedded bone tables as runtime bone hierarchies;
- `VMPF` skin weights;
- `.unit_variant` composition: slots, per-slot mesh and texture choices, and a seeded per-soldier
  pick;
- animation tables and battle-configuration fragments binding slots such as `RIDER_COMBAT_READY`
  to weighted clips.

C12 rejects or defers:

- `.rigid_model` (`0x12345678` signature), `.animatable_rigid_model`, and
  `.rigid_model_animation` families: deferred, explicit failure with path;
- `VMPF` versions 0 and 2 (12 files) and `VRNT` version 0 (268 files): explicit failure until a
  sample is decoded;
- the 53 `.anim` files that start with a frame rate instead of a version word;
- terrain, vegetation (`.tree_mesh`, `.tree_list`), audio (`.bnk`), and Bink movies;
- rider and mount pairing on one clip clock, which C9d designs; C12c stops at independent rider
  and horse clips.

## 2. Packs

Header (32 bytes): `PFHs` (`50 46 48 73`), u32 pack type, u32 pack-reference count, u32
pack-reference size, u32 file count, u32 index size, u64 timestamp. Each index entry is u64 data
offset, u32 uncompressed size, a u32 stored size when type flag `0x20` is set, a u64 timestamp
when flag `0x40` is set, and a NUL-terminated backslash path. Entry data is 16-byte aligned. The
last entry ends exactly at end of file in every pack.

Compressed entries are raw LZ4 blocks with no frame header; every compressed entry sampled for
section 3 to 5 decoded to its recorded size. Entries whose stored size equals the uncompressed
size are raw.

| Pack | Type | Files | Compressed | Content |
|---|---|---|---|---|
| `data.pack` (3.86 GB) | `0x21` | 24,802 | 24,793 | 4,111 `.anim`, 1,228 DB tables, 296 animation `.txt`, 99 RMV2 |
| `models.pack` (5.28 GB) | `0x21` | 10,654 | 10,646 | 2,533 RMV2, 309 `.rigid_model`, 6,394 DDS |
| `models2.pack` (5.12 GB) | `0x21` | 4,731 | 4,731 | 1,242 `.unit_variant`, 562 `.variant_part_mesh`, 2,756 DDS |
| `terrain.pack` (8.04 GB) | `0x21` | 36,085 | 36,084 | 680 RMV2, terrain and vegetation |
| `data_fots.pack` | `0x1` | 22,024 | 0 | Fall of the Samurai campaign and battle data |
| `boot.pack`, `shaders.pack` | `0x40`, `0x41` | 59, 786 | 0 | boot textures, DX11 shaders |
| `audio.pack`, `movies*.pack` | `0x1`, `0x4` | 32,288, 697 | 0 | out of scope |

`PFHs` is not the C9 `PFH5` layout: the index carries a u64 offset per entry instead of packing
data back to back, and compression is per entry.

## 3. RMV2 models

| Version | Files | Example |
|---|---|---|
| 0 | 62 | `rigidmodels\buildings\castle_01_gate\castle_01_gate_piece09_destruct01_gate_closed.rigid_model_v2` |
| 1 | 561 | `rigidmodels\buildings\1meterblock\1meterblock_piece01_destruct01.rigid_model_v2` |
| 2 | 2,688 | `rigidmodels\artillery\armstrong_gun\armstrong_gun.rigid_model_v2` |

- Header: `RMV2`, u32 version, u32 LOD count (0-5, usually 1 or 3), then per-LOD records; strings
  are UTF-16 (mesh names, texture directories, skeleton names such as `armstrong_gun.anim`).
- The layout differs from the C9 version 7 and 8 header, so no C9 reader code is shared.
- 6 files over 3 MB were not sampled.

Mesh headers, vertex formats, and strides are not decoded yet; C12b's first card records them.

## 4. Soldier parts and textures

`.variant_part_mesh` files start `VMPF`, version 3 in 551 of 563 files. Parts live under
`variantmodels\unitparts\<culture>\<slot>\<part>\mesh.variant_part_mesh` with a sibling `tex\`
folder. Positions are half floats. Each part also has an empty `mesh_noclip4` entry.

| Map | Files | Encoding |
|---|---|---|
| `texture_basecolor` | 547 | DX10 BC7 sRGB (99) or BC7 (98) |
| `texture_mra` | 556 | DX10 BC7 (98) or BC3 (77) |
| `texture_normal` | 634 | DX10 BC3 (77), legacy DXT5 |
| `texture_colour_mask` | 637 | DXT1 or DX10 BC1 sRGB (72) |
| `texture_diffuse` | 235 | DXT5 or DXT1, legacy parts only |

Open: the `mra` channel order (metal, roughness, ambient occlusion is assumed from the name), the
normal-map swizzle, and the colour-mask tint semantics are confirmed empirically in C12b.

## 5. Animation

Version 1 clip header: u32 version, f32 frame rate, f32 duration, u32 bone count, then per bone a
u16 length, a UTF-16 name, and an i32 parent index. Each clip carries its own bone table; no
separate skeleton files exist.

- 3,756 clips are version 1; 3,666 run at 20 fps and 90 at 30 fps.
- 3,242 man clips have 48 bones (`Hips`, `Weapon1`-`Weapon3`, `Armor_Front`, `LeftUpLeg`, `Spine`, ...).
- Horse clips have 40 bones.
- Frame data after the bone table is not decoded yet; C12c's first card records it.

`animations\animation_tables\animation_tables.txt` (`version 1` text) names tables such as
`shogun_man_katana` with `skeleton_type man_shogun`, fragments, and a `mount_table`. Fragments in
`animations\battleconfiguration\*_fragment.txt` map slots to weighted clip paths with
`blend_in_time`. Unlike C9 these are a line grammar, not JSON.

## 6. Unit binding

- `.unit_variant` files start `VRNT`, version 2 in 974 of 1,242 files.
- Files are named `<unit key>.<role>.unit_variant`: roles include `soldier` (182), `officer`
  (170), and `standard_bearer` (66).
- A variant lists slots (`heads`, `hands`, `legs`, `torsos`, `shoulders`, `arms`, `helmets`), each
  with weighted `mesh` and `tex/texture` choices as UTF-16 paths.
- DB tables start `FD FE FC FF` plus a GUID. Unit-binding candidates: `units`,
  `battle_entities`, `mount_variants`, `unit_stats_land`. C12a confirms which columns lead from a
  unit key to its animation table.

## 7. Proof units

| Unit | Mesh | Clips |
|---|---|---|
| `ashigaru_inf_yari_ashigaru` soldier | `variantmodels\units\ashigaru_inf_yari_ashigaru.soldier.unit_variant`, part `variantmodels\unitparts\japan\heads\ashigaru\mesh.variant_part_mesh` | `yari_combat_idle1`, `yari_charge`, `yar_banzai_cheer01` |
| horse | `variantmodels\unitparts\horse\_horseparts\body\horsebody\mesh.variant_part_mesh` | `horse_combat_idle_01`, `horse_stand_idle_01` |
| static | castle gate piece (RMV2 v0) and Armstrong gun (RMV2 v2) | none |

## 8. References

- RPFM (github.com/Frodo45127/rpfm, MIT): pack layouts and DB schemas, including SHOGUN 2. The
  schema subset may be ported with credit.
- AssetEditor (github.com/donkeyProgramming/TheAssetEditor): factual reference only, as in C9.
- No Total War asset is committed.

## 9. Phase split

| Phase | Deliverable | Proof |
|---|---|---|
| C12a | `PFHs` variant of the C9 pack file system with LZ4 entries, DB reader for section 6 | All packs enumerate with section 2 counts; every compressed entry decodes to its size; proof unit rows resolve |
| C12b | RMV2 v0-v2 static models, `VMPF` v3 parts in bind pose, Complete Edition texture sets on C3 PBR | Castle gate piece, Armstrong gun, ashigaru head, and horse body render textured beside a Generals Ranger |
| C12c | Version 1 clips with embedded skeletons, `VMPF` skins, `.unit_variant` composition, tables and fragments | A seeded yari ashigaru squad renders with distinct variants and plays `yari_charge`; the horse plays an idle |

Shared code: C9a and C12a share one pack project with a header variant per magic. Whichever phase
lands first creates it; the other adds its variant. C12a also adds the LZ4 block decoder, shared
with [C10a](c10-aoe3de-granny-models.md) when C10a lands first.

## 10. Risks

- Unknown vertex formats: section 3 and 4 layouts are undecoded, so C12b starts with an
  investigation card before any import code.
- Composite cost: like C9, composition happens once per seed and hands B5 a merged result.
- LZ4 throughput: models packs are over 5 GB each; entries decode lazily per open, never per pack.

## 11. Architecture diagrams

`†` Placeholder. Class and sequence diagrams grounded in the C1 importer seam, the C3 materializer,
the shared pack reader, and the new RMV2, `VMPF`, and clip readers are required before the first
C12 source-edit card is marked `Ready`.
