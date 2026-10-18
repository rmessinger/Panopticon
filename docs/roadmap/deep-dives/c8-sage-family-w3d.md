# Deep Dive: SAGE-family W3D models (C8a, C8b, C8c)

> Expands phases C8a, C8b, and C8c of [Track C: External Models & Animations](../track-c-external-assets.md).
>
> Audience: human design for loading Battle for Middle-earth, Battle for Middle-earth II, and Rise
> of the Witch-king W3D models alongside Generals and Zero Hour through the existing W3D path.

Purpose: make the BFME-family model corpus usable as art for Panopticon units. These games share
the Generals W3D container, so the work extends the native W3D reader and its C1 adapters instead
of adding a fourth external importer. Gameplay INI import (hordes, `ChildObject`, `.inc` includes)
is out of scope; it belongs to [Track D](../track-d-gameplay-expansion.md) D3 and
[ModCollector](../plans/mod-collector.md).

Facts below were measured on 2026-10-14 against operator-supplied BFME, BFME2, RotWK, and Zero
Hour installs, using the repository's own `Panopticon.FileFormats.Big` and
`Panopticon.FileFormats.W3d` readers from a throwaway console app outside the repository.

---

## 1. Scope

C8a supports:

- a headless parse of every `.w3d` in BFME, BFME2, RotWK, and Zero Hour through the per-game path
  resolvers, with RotWK layered over BFME2;
- unknown chunks and malformed chunk sizes reported as per-file diagnostics instead of aborting the
  load;
- a checked-in golden list of the known-bad corpus files (names only, no asset bytes).

C8b supports:

- static bind-pose meshes at mesh version `0x40002` (BFME) and `0x50000` (BFME2, RotWK);
- `0x50000` shader materials (`0x50`, `0x51`, `0x3F`) mapped onto the C3 metallic-roughness
  payload: diffuse, normal map from tangent chunks (`0x60`, `0x61`), and the team-color mask;
- the `SHDMESH` family (`0x0B00`-`0x0B4B`) as an optional final step, or an explicit diagnostic.

C8c supports:

- dual-bone vertices (`VERTICES_2` / `NORMALS_2`, `0xC00`) as two-influence skins;
- timecoded compressed animation (`0x280`, flavor 0), adaptive-delta flavor 1, and motion channels
  (`0x284`);
- a decision on channel type 15 (`UnknownBfme`), which `W3dAnimationImporter` currently skips.

C8 rejects or defers:

- BFME INI gameplay semantics; Panopticon object definitions reference the models as art only;
- `.fx` shader parity; shader materials render through C3 PBR, not the original effect files;
- EA asset streams (`.manifest`, `.relo`, `.imp`, `.w3x`); none exist in these three games.

## 2. Install and packaging

| Game | BIG magic | `.w3d` | `.dds` | `.tga` |
|---|---|---|---|---|
| BFME | `BIG4` root, `BIGF` under `apt/` | 5914 | 1713 | 1391 |
| BFME2 | `BIG4` root, `BIGF` under `apt/` | 12747 | 3760 | 2485 |
| RotWK | `BIG4` root, `BIGF` under `apt/` | 1867 (delta over BFME2) | 638 | 417 |
| Zero Hour | `BIGF` | 4429 + 7 + 2 | 3496 | 50 |

`BigArchive` already reads both magics. Game definitions `Bfme`, `Bfme2`, and `Bfme2Rotwk` exist in
`Panopticon.Mods.BuiltIn/GameDefinition.cs`, with asset strategies over the BFME path resolvers.
Only `Mods.BuiltIn` references the BFME projects; ReplaySketch, WorldBuilder, and the benchmarks
reference `Mods.Generals` only. No document records a BFME boot.

Zero Hour already boots (`GeneralsZeroHourDefinition`, H2a records zero import diagnostics). Its
`Probe` checks lowercase `generals.exe`, while the install ships `Generals.exe`, so the Launcher's
directory probe fails on Linux. The environment locator skips `Probe` and is unaffected. C8a fixes
the probe to match case-insensitively.

An operator-made extraction of Generals and Zero Hour (per-model folders with copied textures,
plus a ray-grid occlusion report over 7728 models and an older open-geometry report) is an
inspection aid only. Importers read the `.big` archives directly.

## 3. Corpus content

| Game | Mesh / HLod | Animation | Skeleton only | Compressed anim | Mesh version |
|---|---|---|---|---|---|
| BFME | 3065 | 2662 | 177 | 2541 | `0x40002` |
| BFME2 | 5987 | 6156 | 591 | 5413 | 13,711 at `0x50000`, 10,031 at `0x40002` |
| RotWK | 784 | 925 | 156 | 314 | `0x50000` |
| Zero Hour | 2522 | 1883 | 22 | 4 | `0x40002` |

Features Generals does not use:

- shader materials: 6101 meshes in BFME2, 2479 in RotWK;
- tangent and bitangent chunks: 5980 and 2436;
- dual-bone `VERTICES_2`: 401 (BFME), 1113 (BFME2), 707 (RotWK);
- motion channels: 238,102 in BFME2;
- adaptive-delta compressed animation (flavor 1): 5 files in BFME2.

## 4. Parse results

| Archive | OK | Fail |
|---|---|---|
| Generals `W3D.big` | 4556 | 0 |
| Zero Hour `W3DZH.big` | 4424 | 5 |
| BFME `W3D.big` | 5910 | 4 |
| BFME2 `W3D.big` | 12734 | 13 |
| RotWK `W3D.big` | 1866 | 1 |

Failure causes:

- `SHDMESH` family `0x0B00`-`0x0B4B` in `gbmtwalld`, `gbmtwalldramp`, and `gbmtwalle` (BFME and
  BFME2): the only real format gap. Identification as the late-W3D shader mesh is inferred from
  the chunk layout, not documented.
- Corrupt or misaligned animation chunk sizes: `cusheep_grza` (BFME); `guhbtshfb_cinb`,
  `guhbtshfb_cinc`, `lwbanhfllbst`, `lwbanhnazgul`, `lwbanhwtchkng`, `cuwyrm_cld_skl`,
  `cuwyrm_cld_skn`, `mbtrollpit_v1` (BFME2); five `UISabotr_*` animations (Zero Hour).
- `gugandalfcrstl`: unknown `W3dVertexMappingType` 67. `mbtrollpit_sknm`: decode error.
- RotWK `lwbanhwitchk.w3d` under `readonly-0-rev-*`: an `IFX` file, not W3D.

`W3dChunk` and `W3dFile` throw on any unknown chunk id at any depth today.

## 5. Proof assets

All paths are inside each game's `W3D.big`.

| Game | Role | INI object | Model | Animation examples |
|---|---|---|---|---|
| BFME | infantry | `GondorFighter` | `art\w3d\gumaarms_skn.w3d` (skeleton `gumaarms_skl`) | `gumaarms_flla`, `gumaarms_flya` (55 total) |
| BFME | monster | `MordorMumakil` | `art\w3d\mumumakil_skn.w3d` | `mumumakil_idla`, `mumumakil_runa` (38) |
| BFME | siege | `GondorTrebuchet` | `art\w3d\gusiegtreb_skn.w3d` | `gusiegtreb_idla`, `gusiegtreb_blda` (9) |
| BFME2 | infantry | `GondorFighter` | `art\w3d\gu\gumaarms_skn.w3d` | `art\w3d\gu\gumaarms_flla` (54) |
| BFME2 | ship | `EvilMenCorsairShip` | `art\w3d\au\aucorship.w3d`, `aucrshp_s1_skn.w3d` | `aucrshp_s1_idla` (11) |
| RotWK | cavalry | `AngmarWolfRider` | `art\w3d\ku\kuorcrider_skn.w3d` (skeleton `kuorcrider_skl`) | `kuorcrider_gbda` (31) |
| RotWK | siege | `AngmarTrollSling` | `art\w3d\ku\kutsling_skn.w3d` | `kutsling_idla` (14) |

The BFME2 corsair ship is also the reference hull for [Track E](../track-e-naval-warfare.md).

## 6. Decisions

- Native W3D variant, not a C1 importer. The existing reader parses 99.9% of the corpus and the
  W3D path is already one C1 implementation, so a second W3D importer would duplicate `W3dFile`.
  Animation-semantics changes live in `W3dAnimationImporter`.
- Fail soft per file. A bad corpus file produces one diagnostic and a null asset, matching the C1
  missing-asset rule; Generals output stays byte-identical because Generals has no failures.
- Shader materials map to C3 PBR. `.fx` names select a mapping profile; unmapped profiles fall back
  to diffuse-only with a diagnostic.

## 7. Phase split

| Phase | Deliverable | Proof |
|---|---|---|
| C8a | Corpus smoke test, fail-soft unknown chunks, golden bad-file list, case-insensitive ZH probe | Every archive parses with exactly the section 4 failures; Generals and ZH output unchanged |
| C8b | `0x50000` shader-material meshes on C3 PBR, normal maps, team-color mask; optional `SHDMESH` | Static captures of the section 5 models beside a Generals Ranger |
| C8c | Dual-bone skins, compressed and adaptive-delta animation, motion channels, channel 15 decision | Gondor soldier, Mumakil, and wolf rider cycles bind with no missing targets and capture deterministically |

## 8. Risks

- RotWK-over-BFME2 layering and the BFME texture resolvers are untested on a real install.
- Motion channels may encode root motion; C8c records whether they move the root or only bones.
- Dual-bone vertices need the four-influence runtime from C4d.

## 9. Architecture diagrams

`†` Placeholder. Class and sequence diagrams grounded in `W3dFile`, `W3dChunk`,
`W3dAnimationImporter`, the BFME path resolvers, and the C3 materializer are required before the
first C8 source-edit card is marked `Ready`.
