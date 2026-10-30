# Deep Dive: Total War: THREE KINGDOMS models (C9a, C9b, C9c, C9d)

> Expands phases C9a through C9d of [Track C: External Models & Animations](../track-c-external-assets.md).
>
> Audience: human design for reading Total War: THREE KINGDOMS packs and importing composite
> soldier models, skeletons, and battle animations through the C1-C3 contracts.

Purpose: add a fourth external format family whose units are many-soldier, composite-mesh
formations, the closest commercial reference for [Track M](../track-m-mass-battle.md) mass battles.
Rendering thousands of soldiers (merged variants, imposters, instanced skinning) is
[B5](../track-b-scale.md) scope; C9 only imports what B5 would consume.

Facts below were measured on 2026-10-14 against an operator-supplied install with temporary Python
parsers outside the repository.

---

## 1. Scope

C9a supports:

- `PFH5` packs under `data/`, release type `0x1`, boot type `0x40` (per-entry timestamp), and
  shader type `0x41`;
- uncompressed entries only, exposed as a read-only, case-insensitive `FileSystem` across packs;
- `database.pack` DB tables for the unit-binding subset in section 6, with schemas ported from the
  RPFM `schema_3k.ron` subset.

C9b supports:

- `RMV2` version 7 and 8 static parts: LOD headers, mesh headers, vertex formats 0, 3, and 4;
- `.wsmodel` XML wrappers and `.xml.material` files mapped onto C3 metallic-roughness:
  `base_colour`, `normal`, `material_map`, and `mask`;
- DX10 and legacy DDS textures in BC1, BC3, BC3 sRGB, and BC7.

C9c supports:

- skeleton `.anim` files (`male01`, `horse01_3k`) as runtime bone hierarchies;
- version 7 clips at 20, 30, or 60 fps with static and animated track remaps, float3
  translations, and int16x4 quaternions, expanded to C1 TRS channels;
- animation tables and fragments (JSON `.txt` under `animations/database/battle/`) binding slots
  such as `CHARGE` to weighted clips.

C9d supports:

- `.variantmeshdefinition` composition: slots, `VARIANT_MESH_REFERENCE` includes, and
  `attach_point` weapons on `weapon_1`-`weapon_6`, with a seeded per-soldier variant pick;
- the unit-binding chain `main_units` to `unit_variants` to `variants` to `mens` and
  `battle_animations_table`;
- cavalry as a rider and mount pair with separate skeletons and synchronized clips.

C9 rejects or defers:

- compressed or encrypted pack entries (none observed): explicit failure with pack and path;
- clip versions other than 7, `.anm.meta` events, faction tint LUT tables, dismemberment `stump_*`
  slots, and hero equipment from `ceos_to_equipment_variants`;
- imposters (`low_poly_filename`), merged-variant baking, and LOD selection policy: B5.

## 2. Packs

Header: `PFH5` (`50 46 48 35`), then type and flags, pack-reference count and size, file count,
index size, and timestamp. Each index entry is u32 size, an optional u32 timestamp (flag `0x40`),
a u8 compression flag, and a NUL-terminated backslash path. Data follows the index back to back.
No pack sets the encrypted-index or encrypted-data flags (`0x80`, `0x10`), and every parsed entry
has compression flag 0.

| Pack | Files | Content |
|---|---|---|
| `data.pack` (6.15 GB) | 44,200 | 4,351 `.anim` under `animations\`, tables, fragments, 2 skeletons |
| `fast.pack` | 9,749 | 2,131 RMV2, 1,283 `.wsmodel`, 221 VMDs, 7 skeletons |
| `variants.pack` | 16,357 | 1,244 RMV2, 2,530 `.wsmodel`, 4,570 `.material`, 1,266 VMDs |
| `variants_dds.pack` (4.4 GB) | 5,473 | DDS for variant meshes |
| `models.pack`, `models2.pack` | 2,912, 7,027 | buildings and props |
| `database.pack` | 1,503 | `db\<table>_tables\data__` |

## 3. RMV2 models

- Header: `RMV2`, version, LOD count (0-6, usually 4), and a 128-byte skeleton name (empty for
  static props).
- LOD header (28 bytes): mesh count, vertex and index data sizes, offset, visibility distance,
  authored LOD index, quality level.
- Mesh header: material type 65 (`weighted`) or 68 (`default`), counts, bounds, shader name,
  vertex format at `+0x50`, texture directory, attachment points, bone count.

| Vertex format | Stride | Influences | Use |
|---|---|---|---|
| 0 | 32 B | 0 | static |
| 3 | 32 B | 2 | weighted |
| 4 | 36 B | 4 | most character parts |

Positions are half floats; normals and tangents are packed 8-bit. Four influences fit the C4d
runtime limit.

## 4. Materials and textures

Materials name a shader (for example `weighted2_character.xml.shader`) and the texture slots
`s_xml_base_colour`, `s_xml_normal`, `s_xml_surface_map` (`*_material_map.dds`), `s_xml_mask`,
dirt, and dither. `dirt_metalness` and `dirt_roughness` parameters confirm a metal/rough model.

| Map | Encoding |
|---|---|
| base colour | DX10 BC1 sRGB (DXGI 72) |
| normal | DXT5, likely swizzled |
| material map | BC3 (77) or BC7 (98) |
| mask | BC3 sRGB (78) |

Open: the material-map channel layout and the normal-map swizzle are confirmed empirically in C9b.

## 5. Skeletons and animation

`male01.anim` is version 7 at 20 fps with 121 bones as (name, parent) pairs, from `animroot` and
`root` through `weapon_1`-`weapon_6` and the spine. Skeletons ship with `.bone_inv_trans_mats`,
which C9c recomputes from the bind pose instead of reading.

Clip layout, verified byte-exact on `inf_1h095dun_default_01.anim`: header with skeleton name and
end time, bone table, per-bone translation and rotation remaps (`>= 10000` static, `>= 0` animated
index, `-1` absent), one static frame, then animated frames. In a 597-file sample all clips but one
are version 7.

Animation tables name a skeleton, mount and dismount tables, and fragments. Fragments map slots to
weighted clips with blend times. Compiled `.bin` forms duplicate the JSON and are not read.

## 6. Unit binding

`main_units.land_unit` leads to `land_units` (stats). `unit_variants` maps a unit and faction to
`man_variant`, `mount_variant`, and `composed_entity`. `variants` maps a variant name to its
`.variantmeshdefinition`, low-poly imposter, scale, and scale variation. `composed_entities` gives
`num_men` and `num_mounts`. `mens` maps to the animation table (for example `cavalry_2h250`), and
`battle_skeletons` names hip, hand, and weapon nodes. DB tables start `FD FE FC FF` plus GUID and
`FC FD FE FF` plus version.

Heroes are empty shells whose look comes from equipped items through
`ceos_to_equipment_variants`; C9d defers them.

## 7. Proof units

| Unit | Mesh | Skeleton | Clips |
|---|---|---|---|
| `3k_main_unit_wood_spear_warriors` | `variantmeshdefinitions\unit_wood_spear_warriors.variantmeshdefinition`, part `components\helmets\hat_felt\hat_felt_02` | `animations\skeletons\male01.anim` | `inf_2h250_idle_active_01`, `inf_2h250_atk_stb_high_03`, `inf_2h250_charge_03` |
| `cavalry_2h250` | horse `_VariantModels/mount/horse/horse_body_bay_dark_socks_01.wsmodel` plus mane and saddle | `horse01_3k.anim` and `male01.anim` | `cav_2h250_charge_attack_01`, `hr1_1h095_attack_02` |
| `hero_earth_liu_bei` | deferred hero proof | `male01.anim` | `cavalry_1h095_hero` table |

The exact lancer-cavalry `man_variant` row is confirmed in C9d.

## 8. References

- RPFM (github.com/Frodo45127/rpfm, MIT): packs, DB schemas, animation tables and fragments. The
  `schema_3k.ron` subset may be ported with credit.
- AssetEditor (github.com/donkeyProgramming/TheAssetEditor): RMV2 v6-8 and animation readers. No
  license is published, so it is a factual reference only and no code is copied.
- No Total War asset is committed.

## 9. Phase split

| Phase | Deliverable | Proof |
|---|---|---|
| C9a | `PackFileSystem` over `PFH5`, DB reader for the section 6 tables | All packs enumerate with section 2 counts; proof unit rows resolve |
| C9b | RMV2 static parts, `.wsmodel`, materials on C3 PBR | `hat_felt_02` and the horse body render textured beside a Generals Ranger |
| C9c | Skeleton import, version 7 clips, tables and fragments | Spearman and horse clips bind with no missing targets and capture deterministically |
| C9d | Variant composer, unit-binding chain, rider and mount pairing | A seeded spearman squad and one cavalry rider render with distinct variants and play `CHARGE` |

The pack project is shared with [C12a](c12-total-war-shogun2-models.md#9-phase-split), which adds the
SHOGUN 2 `PFHs` header variant.

## 10. Risks

- Composite soldiers: a soldier is about ten parts, so per-soldier assembly cost must stay out of
  the per-frame path; C9d composes once per seed and hands B5 a merged result.
- Rider and mount clips must stay in sync; C9d drives both from one clip clock.
- Faction tint semantics behind `s_xml_mask` are unconfirmed and render untinted until settled.

## 11. Architecture diagrams

`†` Placeholder. Class and sequence diagrams grounded in the C1 importer seam, the C3 materializer,
and the new pack and RMV2 readers are required before the first C9 source-edit card is marked
`Ready`.
