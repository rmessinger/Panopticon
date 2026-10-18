# Deep Dive: Rise of Legends models (C11a, C11b, C11c)

> Expands phases C11a, C11b, and C11c of [Track C: External Models & Animations](../track-c-external-assets.md).
>
> Audience: human design for reading Rise of Nations: Rise of Legends `.big` archives and importing
> `.bh3` models and `.bha` animations through the C1-C3 contracts.

Purpose: add a fantasy and steampunk external format family. Rise of Legends is not Gamebryo: no
file carries a NIF header. It uses Big Huge Games' RTS2 engine with its own chunked model format,
descended from Rise of Nations. The C4a NIF reader does not apply; the C1 seam, skin and clip data
model, DDS decoding, and test harness do.

Facts below were measured on 2026-10-14 against operator-supplied game files with temporary Python
parsers outside the repository.

---

## 1. Scope

C11a supports:

- an installed game directory located by its `BIGS/` folder;
- `.big` WAR-BUILDER archives (header u32 `11`, u8 `1`, UTF-16 `WAR-BUILDER`, entry count),
  per-entry zlib payloads, exposed as a read-only, case-insensitive `FileSystem`;
- explicit failures for the nine config archives with the variant header, none of which hold
  models.

C11b supports:

- the chunk container (u32 size including the 8-byte header, u16 id, u16 child count);
- `.bh3` static meshes: positions (2), normals (3), tangents (8), UVs (4) with their own count, u16
  indices (5), bone tree (6, 7) with per-bone vertex ranges, bounds and attach points (13-18);
- rigid per-bone skinning from the bone vertex ranges;
- DDS textures (stored with a `.tga` extension) and `data/unit_materials.xml` mapped onto C3
  metallic-roughness: `diffusemap`, `normalmap`, and `teamcolormap`.

C11c supports:

- blended skins from chunks 51 and 50 (per-vertex u32 reference and float weight), after their
  semantics are confirmed;
- `.bha` per-bone key tracks (chunk 8 nodes, chunk 7 keys, chunk 130 flags) resampled to C1 TRS
  channels;
- `data/unit_graphics.xml` LODs, skeleton attachments (`EXISTREF`), and
  `data/unit_skeletons.xml` stance animations with weighted clip lists.

C11 rejects or defers:

- unknown chunk ids (70, 90, 110-114 are seen but undecoded): skipped with a diagnostic listing the
  id and file;
- `.pfb` prefabs, SpeedTree `.spt`, `.fx` effect parity, and timed `<EVENT>` entries;
- extraction from installer repacks (section 2).

## 2. Install and packaging

Game data ships as 92 `BIGS/*.big` archives (616 MB). Each entry is a UTF-16 path, u64 zero, u32
offset, u32 size, u32 zero, u32 time, and a UTF-16 extension; each payload is a u32 compressed
length plus a zlib stream. 83 of 92 archives parse with this layout.

The operator's copy is an installer repack (Inno Setup 5.5.0 over AES-encrypted FreeArc archives
with SREP preprocessing). It extracts on Linux with patched third-party tools, but that pipeline
is too fragile to depend on, and a repack is not a retail source. C11 reads an installed game
directory only.

| Extension | Count | Content |
|---|---|---|
| `.bh3` | 783 | meshes and skeletons |
| `.bha` | 1927 | bone-track animations |
| `.tga` | 3771 | DDS payloads: 3073 DXT5, 324 DXT1, 374 ARGB32, no mipmaps |
| `.pfb` | 1321 | prefabs in the same chunk container |
| `.xml` | 3359 | unit bindings, 1483 tagged `bxml` |

All 783 `.bh3` and 1927 `.bha` files parse cleanly as chunk trees.

## 3. Unit binding

1. `data/unitrules.xml`: each `<UNIT>` has a name, stats, and `<GRAPH>` type.
2. `data/unit_graphics.xml` (355 units): `<MODEL file="..._LOD_0.BH3">`, nested `<LOD level>`,
   `<SKELETON name>` with `EXISTREF` attachments, `<MATERIAL name="x.fx">`.
3. `data/unit_skeletons.xml` (436 skeletons): `<STANCEANIM anim="Idle|Walk|...">` with weighted
   `<ANIM file="...bha">` entries.
4. `data/unit_materials.xml`: material name, base material (for example `UnitDot3teamcolor.fx`),
   and texture roles.

## 4. Proof units

| Unit | Model (archive) | Stats | Clips and textures |
|---|---|---|---|
| `ASSASSIN` (Vinci) | `art/units/vinci/assassin_lod_0.bh3` (`vinci_units.big`) | 1442 verts, 1393 tris, 83 bones, weighted | `Imp_Musk_*.bha`; `assassin_{diffuse,dot3,tc}_512.tga` |
| `ZEKE` (Vinci vehicle) | `art/units/vinci/zeke_lod_0.bh3` | 463 verts, 248 tris, 33 bones | scout and siege forms share the mesh |
| `JAGUARKNIGHT` (Cuotl) | `art/units/cuotl/jaguarknight_lod_0.bh3` (`cuotl_units.big`) | 1267 verts, 748 tris, 38 bones, weighted | stance clips from `unit_skeletons.xml` |

Backup: `AIR_DESTROYER` (`air_destroyer_lod_0.bh3`) with a propeller-spin clip.

## 5. References

- github.com/ptasev/Rise-of-Nations: C# BH3/BHA reader for Rise of Nations (chunks 0-7, `float4`
  positions, rigid skinning), 010 Editor templates, and a Blender add-on. No license is published,
  so it is a factual reference only and no code is copied. It does not cover the Rise of Legends
  chunks.
- No public specification exists for the WAR-BUILDER `.big` container or the later chunks.
- No Rise of Legends asset is committed.

## 6. Phase split

| Phase | Deliverable | Proof |
|---|---|---|
| C11a | Game-directory locator, `WarBuilderBigFileSystem` | 83 archives enumerate; the proof models resolve with recorded sizes |
| C11b | Chunk reader, BH3 static mesh, rigid skin, DDS materials on C3 PBR | Assassin, Zeke, and Jaguar Knight render textured with team color beside a Generals Ranger |
| C11c | Blended weights, BHA clips, stance and LOD binding, attachments | Assassin and Jaguar Knight idle and walk stances bind with no missing targets and capture deterministically |

## 7. Risks

- Chunk 51 and 50 semantics are inferred as a two-influence blend; C11c verifies against rigid
  output before enabling it.
- The `.bha` key time unit (first key at 0.0667 s suggests 15 fps) and key encoding need
  confirmation.
- Coordinates are 3ds Max Z-up with a per-unit XML `scale`.

## 8. Architecture diagrams

`†` Placeholder. Class and sequence diagrams grounded in the C1 importer seam, the C3 materializer,
and the new BIG and chunk readers are required before the first C11 source-edit card is marked
`Ready`.
