# Deep Dive: Larger Maps (via WorldBuilder)

> Expands phase B4a of [Track B: Scale](../track-b-scale.md). Part of the
> [Large-Scale RTS Roadmap](../README.md).
>
> Audience: human overview of the larger-maps design.

Purpose: scale WorldBuilder and map loading toward very large maps and large populations. The
design treats terrain streaming, chunk residency, scalable edit/undo, and large-object authoring as
core requirements.

B4a assumes that WorldBuilder's basic author, save, load, and play round-trip works at normal map
sizes. That baseline is tracked by A5-6 in [implementation-checklist.md](../implementation-checklist.md).
The only current A5-6 card found is [`WI-A5-06`](https://projects.reid-corp.com/work_packages/302),
which is `Ready` and grounds A5 architecture, not the full WorldBuilder baseline acceptance item.
B4a implementation cards therefore call out the A5-6 checklist item where it affects execution.

## Decisions

- No size cap. Design for arbitrarily large maps. Map size is bounded by disk and authored data, not
  by a hard format or RAM ceiling.
- Original-engine `.map` write compatibility is not required for large maps. Reading legacy maps remains
  useful, but large-map authoring targets the H2/H3 path.
- B4a-2 undo uses region deltas: a rectangle plus before and after arrays for each edited layer.
- B4a-4 terrain LOD uses CDLOD: quadtree selection with continuous morph between levels.
- B4a-5 owns the runtime chunk store: chunk residency, paging, dirty tracking, and runtime access.
  H3 owns on-disk chunk encoding.
- The proposed runtime chunk layout matches B4b §5.1a: 128 lattice intervals per chunk, 129 lattice
  points, one-sample apron on every side, and raw little-endian `float32` height payloads in game units.

## How maps are sized today

- `HeightMapData` stores `Width`, `Height`, `BorderWidth`, `Area`, and a resident `ushort[,] Elevations`
  (`src/Panopticon.Simulation/Data/Map/HeightMapData.cs:19-30`). `Parse` allocates the full array and
  rejects `Width * Height != Area` (`HeightMapData.cs:137-146`). `Create` wraps a caller-supplied array
  (`HeightMapData.cs:35-46`). `MapFile.CreateEmpty` allocates new maps at `playable + 2 * border + 1`
  per axis (`src/Panopticon.Simulation/Data/Map/MapFile.cs:342-345,356`).
- `BlendTileData` stores tile, blend, passability, buildability, visibility, and related per-cell arrays
  (`src/Panopticon.Simulation/Data/Map/BlendTileData.cs:18-53`). `Parse` sizes them from `HeightMapData`
  (`BlendTileData.cs:184-195`). `CreateUniform` allocates each array at `width x height`
  (`BlendTileData.cs:88-110`).
- `HeightMap.HorizontalScale` is 10 world units per sample (`src/Panopticon.Simulation/Terrain/HeightMap.cs:12`).
  `GetHeight(int, int)` indexes the whole `Elevations` array (`HeightMap.cs:32`). `Width` and `Height` are
  the data dimensions minus one (`HeightMap.cs:176-177`). The constructor allocates
  `Normals = new Vector3[Width, Height]` and fills every entry through `RefreshNormals`
  (`HeightMap.cs:168-192`).
- `Terrain.PatchSize` is 17 vertices, or 16 intervals (`src/Panopticon.Simulation/Terrain/Terrain.cs:62,354`).
  `CreatePatches` builds every patch for the map (`Terrain.cs:352-396`). It runs from the constructor
  (`Terrain.cs:163`) and from the full rebuild `OnHeightMapChanged()` (`Terrain.cs:336-350`). The bounded
  `OnHeightMapChanged(bounds)` (`Terrain.cs:313-334`) rebuilds only intersecting patches.
- `Terrain.CreateTileDataTexture` allocates `uint[Width * Height * 4]` for the whole map and uploads one
  texture (`Terrain.cs:398-452`, allocation at `403`).
- `TerrainPatch` reads `heightMap.GetPosition` and `heightMap.Normals[x, y]` over its own bounds
  (`src/Panopticon.Simulation/Terrain/TerrainPatch.cs:72-86`, reads at `77` and `82`). It is a
  patch-local consumer of the whole-grid arrays.
- `PartitionCellManager.OnNewGame` takes the terrain boundary from `Borders[0]`
  (`src/Panopticon.Simulation/Logic/PartitionCellManager.cs:92-101`) and allocates `_cells` for the full
  boundary (`PartitionCellManager.cs:103`).
- `Navigation` has two constructors, one from `BlendTileData.Impassability` and one from a `bool[,]` feed.
  Both build a whole-map `Graph` (`src/Panopticon.Simulation/Navigation/Navigation.cs:29-45,52-68`, graph
  construction at `33` and `56`).
- `EditorMap.Save` (`src/Panopticon.Tools.WorldBuilder.Core/EditorMap.cs:99-124`) calls `MapFile.WriteTo`
  at line 115 for legacy maps and `MapPackageConverter.SavePackage` at line 108 for packages.
- `HeightMapEdit` records a rectangular before and after patch
  (`src/Panopticon.Tools.WorldBuilder.Core/HeightMapEdit.cs:8-27,33-64`). `Apply` and `Revert` copy the
  patch back (`HeightMapEdit.cs:66-93`).
- `HeightBrushTool` clones the whole `Elevations` array at pointer down
  (`src/Panopticon.Tools.WorldBuilder.Core/HeightBrushTool.cs:43`) and again at pointer up
  (`HeightBrushTool.cs:87`), then slices the affected rectangle (`HeightBrushTool.cs:94-111`).
- `ObjectsList` stores a flat `Objects` array (`src/Panopticon.Simulation/Data/Map/ObjectsList.cs:12`).
  `AddObject` copies the whole array (`ObjectsList.cs:17-20`), and `RemoveObject` rebuilds it
  (`ObjectsList.cs:26`).

## Constraints to lift

| # | Constraint | Where | Why it blocks large maps |
|---|---|---|---|
| C1 | Whole heightmap and per-cell arrays resident in memory | `HeightMapData`, `BlendTileData` | Memory grows with total area. |
| C2 | All terrain patches and tile-data texture built up front | `Terrain` | Build time, GPU memory, and draw cost grow with total area. |
| C3 | Brush strokes clone the full elevation grid | `HeightBrushTool` | Edit latency and memory scale with map area, not brush area. |
| C4 | Undo model needs layer-general region deltas | WorldBuilder edit model | Height already has a patch edit; blend, passability, and object edits need the same contract. |
| C5 | Spatial and navigation structures size eagerly to full grid | `PartitionCellManager`, `Navigation` | Allocation and per-tick cost grow with map area. |
| C6 | Legacy `.map` is the only save path in WorldBuilder | `EditorMap.Save`, `MapFile.WriteTo` | Chunked storage needs H2/H3 package output. |

## Baseline audit (WI-B4a-02)

Run context: Intel Core Ultra 7 265K, 20 cores, 125 GiB RAM, Linux. .NET SDK 10.0.109, runtime 10.0.9.
Measured 2026-11-11 on commit `99f382642`, Release build.

Probe method: a temporary xUnit fact, deleted after measurement and not committed, runs one playable size
per `dotnet test` process. Each run calls `NewMapFactory.Create`, times one `HeightBrushTool` Mound stroke
at the map centre (radius 10, strength 1, delta 100), calls `UndoStack.Undo`, then saves and reloads through
the legacy and package paths. Phases use `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread` deltas.
Stop rules: 300 s process timeout, any phase above 60 s, an error, or no result file. Raw JSON and logs are
kept outside the repository.

Lattice is playable + 13 per axis (`MapFile.CreateEmpty`, `MapFile.cs:342-343`). Times are one sample per size.

| Playable | Lattice | createMs | heightMapCtorMs | brushStrokeMs | undoMs | saveLegacyMs | loadLegacyMs | savePackageMs | loadPackageMs |
|---|---|---|---|---|---|---|---|---|---|
| 128 | 141 | 15.5 | 1.2 | 1.5 | 0.16 | 14.3 | 15.7 | 73.2 | 15.7 |
| 256 | 269 | 14.7 | 3.9 | 1.5 | 0.14 | 16.4 | 22.5 | 74.6 | 30.9 |
| 512 | 525 | 26.0 | 14.3 | 1.8 | 0.15 | 29.5 | 21.0 | 95.8 | 58.6 |
| 1024 | 1037 | 36.8 | 24.5 | 3.0 | 0.13 | 132.5 | 50.7 | 219.8 | 104.7 |
| 2048 | 2061 | 142.0 | 94.2 | 8.0 | 0.14 | 508.1 | 193.0 | 560.3 | 356.8 |
| 4096 | 4109 | 469.4 | 375.5 | 21.3 | 0.14 | 2024.4 | 663.9 | 1975.7 | 1432.3 |

| Playable | Working set MiB | createMiB | heightMapCtorMiB | brushMiB | legacy file MiB | package MiB | Render patches | Tile texture MiB |
|---|---|---|---|---|---|---|---|---|
| 128 | 97.4 | 0.80 | 0.22 | 0.08 | 0.18 | 0.24 | 81 | 0.30 |
| 256 | 98.2 | 2.90 | 0.82 | 0.28 | 0.63 | 0.86 | 289 | 1.10 |
| 512 | 100.4 | 11.04 | 3.14 | 1.06 | 2.40 | 3.26 | 1,089 | 4.19 |
| 1024 | 111.0 | 43.07 | 12.28 | 4.11 | 9.36 | 12.67 | 4,225 | 16.38 |
| 2048 | 153.3 | 170.10 | 48.56 | 16.21 | 36.97 | 50.00 | 16,641 | 64.75 |
| 4096 | 323.0 | 676.19 | 193.13 | 64.41 | 146.93 | 198.67 | 66,049 | 257.50 |

Working set is the test host process `WorkingSet64` after create. Render patches and tile texture are
arithmetic, not GPU measurements. Patches use `ceil((lattice - 2) / 16)` per axis because `CreatePatches`
subtracts one from `HeightMap.Width` (`Terrain.cs:356-357`, `HeightMap.cs:176`). The texture uses
`(lattice - 1)^2 x 16` bytes (`Terrain.cs:403`).

Growth from 2048 to 4096 (area x3.97): createMs x3.31, heightMapCtorMs x3.99, brushStrokeMs x2.65,
saveLegacyMs x3.98, loadLegacyMs x3.44, savePackageMs x3.53, loadPackageMs x4.01. Allocation series grow
x3.97 to x3.98. At 4096, create allocates 42.0 bytes per lattice sample, the `HeightMap` constructor
12.0 (`Normals`, `HeightMap.cs:179`), a stroke 4.0 (two full `ushort` clones), the legacy file 9.1, and the
package 12.3.

### First failure

All six sizes completed with no error, no timeout, no phase above 60 s, and no process death. No size
failure was observed, so no size or memory limit is claimed. The first failing constraint is C3, on the edit
path, because undo does not restore the stroke at any size. The centre sample reads 10 before the stroke,
110 after it, and 110 after `Undo` at every size from 128 to 4096. The legacy and package reloads also hold
110, so the saved files contain the unrestored value.

Cause: `OnPointerDown` applies the stroke in place (`HeightBrushTool.cs:49`). `OnPointerUp` then calls
`HeightMapEdit.Capture` (`HeightBrushTool.cs:115-122`). Its before-snapshot is read after the grid has been
mutated (`HeightMapEdit.cs:38-48`), and its apply action is a no-op (`HeightBrushTool.cs:118-121`). `Revert`
therefore restores post-stroke values (`HeightMapEdit.cs:71-74`, `UndoRedoStack.cs:32`). The patchBefore and
patchAfter arrays built at `HeightBrushTool.cs:102-111` are never read. The full-grid clones at
`HeightBrushTool.cs:43` and `:87` make the stroke cost scale with map area: 64.41 MiB at 4096 for a
radius-10 stroke. No source test in the tree undoes a `HeightBrushTool` stroke.

| Constraint | Status at 4096 | Evidence |
|---|---|---|
| C1 | Measured growth, no failure | createMiB 676.19, heightMapCtorMiB 193.13 |
| C2 | Not measured, arithmetic only | 66,049 patches and 257.50 MiB tile texture, built in the `Terrain` constructor (`Terrain.cs:163`, `352-396`, `398-452`) |
| C3 | Fails: undo does not restore; cost scales with area | Two full clones per stroke (`HeightBrushTool.cs:43`, `:87`); the undo error at `:115-122` |
| C4 | Open: the existing patch edit is defeated by capture order | `HeightMapEdit.cs:38-48`; the region-delta contract is WI-B4a-03 |
| C5 | Not measured by the probe | `PartitionCellManager` and `Navigation` are not constructed |
| C6 | Stale wording, package path exists | Package save and reload round-trip (`EditorMap.cs:106-111`, `148-153`); new maps default to legacy (`EditorMap.cs:44-50`); the package write result is discarded (`EditorMap.cs:108`) |

### Resident renderer and chunk store

Resident renderer: `Terrain` builds every patch and one whole-map tile texture in its constructor (C2).
The editor's 3D preview loads only maps inside the game file system (`GameViewportRenderer.cs:276-287`), so
a new map outside the installation has no preview at any size. The probe does not construct `Terrain`.

Chunk store: this stack contains `RuntimeTerrainChunkStore` (`src/Panopticon.Simulation/Terrain/RuntimeTerrainChunkStore.cs:14`),
which bounds resident chunks and never evicts dirty data. `LargeMapRoundTripTests` covers the bounded read
path (`test/Panopticon.Simulation.Tests/Terrain/LargeMapRoundTripTests.cs:42`). The probe does not exercise
the store, so its paging limits are not measured here. The statement under Chunk contract (proposed) that no
store exists is stale for this stack.

### A5-6 status

The A5-6 checkbox is checked (`implementation-checklist.md:728`). The normal-size leg holds: at 128 and 256
the author, save, and reload legs complete. `WorldBuilderRoundTripTests.SavedNewMap_StartsInEngine`
(32 x 32 map, `CNC_GENERALS_PATH` set) passed: 1 passed, 0 failed, 0 skipped. The checked clause that undo/redo
is proven by test does not hold for height strokes, because undo does not restore the centre at any size. This
audit documents that gap for the functional-gap clause at `implementation-checklist.md:732-733`. The checkbox
is not changed here. The fix belongs to WI-B4a-04 with the region-delta contract from WI-B4a-03.

### Limitations

- One run per size on a shared host. Timings are single wall-clock samples, not CI-grade.
- Release build. Working set is the whole test host process, not an editor process.
- One stroke at the map centre, radius 10. Undo runs before the saves, so the saved files hold the post-stroke state.
- Not measured: `Terrain` construction, GPU memory, render cost, C5 structures, chunk-store paging, and engine load of any map larger than 32 x 32.
- Growth beyond 4096 is not measured. No OOM or process death was observed.

## Chunk contract (proposed)

B4a adopts the B4b §5.1a layout as the proposed B4a-5 runtime chunk contract. The rationale is in
[§5.1a](r-real-world-terrain.md#51a-chunk-layout). No `RuntimeTerrainChunkStore` exists in the tree; the
name in the diagrams is the proposed B4a-5 type. The layout is already encoded in the interchange model
`MapInterchange` (`src/Panopticon.Simulation/Content/Importing/Maps/MapInterchange.cs:10`) through the
constants in `MapChunkGeometry` (`src/Panopticon.Simulation/Content/Importing/Maps/MapChunkGeometry.cs:8-18`).

| Property | Value |
|---|---|
| Lattice spacing | 10 game units |
| Chunk extent | 128 sample intervals per axis |
| Lattice points per chunk | 129 per axis; edge points duplicated in neighbours and equal |
| Apron | one read-only sample on each side for normals |
| Stored chunk sample count | 131 x 131 with apron |
| Height payload | little-endian `float32` game units (`MapPackageChunkCodec.cs:22`) |
| Chunk key | `(cx, cy)` from southwest origin; +X east, +Y north |
| Runtime owner | B4a-5 chunk store |
| On-disk owner | H3 package encoding: writer `MapPackageDirectoryWriter.Write` (`src/Panopticon.Simulation/Content/Importing/MapSources/Package/MapPackageDirectoryWriter.cs:33`), codec `MapPackageChunkCodec` (`MapPackageChunkCodec.cs:15`), reader `MapPackageChunkFileReader` (`MapPackageChunkFileReader.cs:16`) |

### Lattice and patch mapping

| Quantity | Value | Source |
|---|---|---|
| Chunk extent | 128 intervals x 10 units = 1280 game units per axis | `MapChunkGeometry.cs:10`, `HeightMap.cs:12` |
| Core points | 129 per axis: 128 intervals plus the shared end point | `MapChunkGeometry.cs:8` |
| Stored points | 131 per axis: 129 core plus one apron per side | `MapChunkGeometry.cs:14` |
| Chunk origin | whole-map lattice index `(128 cx, 128 cy)` | `MapInterchangeValidator.cs:21` |
| Shared edge | lattice column `128 (cx + 1)` is core column 128 of chunk `cx` and core column 0 of chunk `cx + 1` | `MapInterchangeValidator.cs:21-23` |
| East apron | lattice column `128 (cx + 1) + 1`, core column 1 of chunk `cx + 1` | `MapInterchangeValidator.cs:33-34` |
| Render patch | 17 vertices with stride 16, so adjacent patches share one edge | `Terrain.cs:62,354,377-381` |
| Patches per chunk | `128 / 16 = 8` per axis; `8 x 8 = 64` at full detail | arithmetic |
| Patch `(px, py)` | core-local vertices `16 px` through `16 px + 16` inclusive | arithmetic on `Terrain.cs:374-381` |

Chunk world origin: `(128 cx - BorderWidth) x 10` per axis, from `GetPosition` (`HeightMap.cs:126-129`).

The current patch count formula does not fit a 129-point chunk. `CreatePatches` derives its extent from
`HeightMap.Width - 1` (`Terrain.cs:356-368`), and `HeightMap.Width` is the data width minus one
(`HeightMap.cs:176`). For a 129-point grid, that yields a last patch of 16 vertices (`Terrain.cs:380`).
Chunk-local patch bounds must use the 129-point core extent.

### Border and apron interactions

- `BorderWidth` is map-edge padding inside the lattice. New maps size `Width` and `Height` to include both
  borders (`MapFile.cs:342-343`). World-to-lattice conversions add it (`HeightMap.cs:88`, `104-105`,
  `148-149`, `164`) and lattice-to-world conversions subtract it (`HeightMap.cs:127-128`). Chunk keys index
  the padded lattice, so chunk origins in world units subtract `BorderWidth`.
- The apron is a one-sample copy of the neighbour's core sample. It is not `BorderWidth`. A chunk on the
  map edge has no neighbour on that side. Its apron follows the adapter clamp policy
  (`src/Panopticon.Simulation/Content/Importing/MapSources/LegacyMapReader.cs:165-166`), and the validator
  does not check map-edge aprons (`MapInterchangeValidator.cs:36-37`).
- Normals at core edges read the apron. `CalculateNormal` reads quads `m = x - 1..x` and
  `n = y - 1..y` (`HeightMap.cs:240-242`), and `CalculateQuadNormal` reads `x + 1` and `y + 1`
  (`HeightMap.cs:215-218`). With the apron, a chunk-local normal matches the whole-grid normal everywhere
  except the final global lattice column and row, where the whole-grid path substitutes `UnitZ`
  (`HeightMap.cs:248`) or stores no normal (`HeightMap.cs:176-177`).
- Shared edge points must be equal in both chunks because both chunks' normals and apron samples read
  them (`MapInterchangeValidator.cs:33-37`).

### Memory estimates

These are estimates from element sizes, not measurements. Bytes are per chunk.

| Layer | Basis | Bytes | KiB |
|---|---|---|---|
| Heights | 131 x 131 x 4, float32, with apron | 68,644 | 67.04 |
| Normals | 129 x 129 x 12, `Vector3`, held in `HeightMap.Normals` today (`HeightMap.cs:168`) | 199,692 | 195.01 |
| Material index, in memory | 129 x 129 x 2, `ushort` (`BlendTileData.cs:18`) | 33,282 | 32.50 |
| Material index, palette bytes | 129 x 129 x 1, uint8 | 16,641 | 16.25 |
| Impassability mask, in memory | 129 x 129 x 1, `bool` (`BlendTileData.cs:23`) | 16,641 | 16.25 |
| Buildability mask, in memory | 129 x 129 x 1, `bool` (`BlendTileData.cs:47`) | 16,641 | 16.25 |
| Two masks, bit-packed | 129 x 129 / 8 per mask, about 2,080 bytes each | 4,160 | 4.06 |
| Subtotal, in memory | heights, normals, material `ushort`, two `bool` masks | 334,900 | 327.05 |
| Current tile-data texture, state basis | 128 x 128 x 16, `R32_G32_B32_A32_UInt` (`Terrain.cs:451`) | 262,144 | 256.00 |
| Current tile-data texture, 129 basis | 129 x 129 x 16 | 266,256 | 260.02 |

The bit-packed row is a packing scenario. The H3 codec writes masks as raw uint8 flag bytes today
(`MapPackageChunkCodec.cs:83-87`), so the in-memory `bool` and on-disk byte figures apply to the
current encoding. The subtotal omits the other `BlendTileData` arrays, so the resident total is higher.

Somme extent: about 78 x 52 chunks, carried from the settled extent above, gives 4,056 chunks.

| Layer | Total bytes | MiB |
|---|---|---|
| Heights | 278,420,064 | 265.52 |
| Normals | 809,950,752 | 772.43 |
| Material index, in memory | 134,991,792 | 128.74 |
| Impassability mask, in memory | 67,495,896 | 64.37 |
| Buildability mask, in memory | 67,495,896 | 64.37 |
| Combined in-memory subtotal | 1,358,354,400 | 1,295.43 |
| Current tile-data texture, state basis | 1,063,256,064 | 1,014.00 |
| Current resident `Elevations`, `ushort`, 9,985 x 6,657 lattice without border | 132,940,290 | 126.78 |
| Full-detail render patches, 64 per chunk | 259,584 patches | n/a |

Stored heights hold 4,056 x 17,161 = 69,605,016 samples. The unique lattice for the same extent is
9,985 x 6,657 = 66,470,145 points, so apron and shared-edge duplication add about 4.7 percent. The whole
Western Front is a later capacity problem and must page by region rather than relying on full residency.

## Sub-phases

| Phase | Goal | Status |
|---|---|---|
| B4a-1. Baseline and limits audit | Measure normal and large map load time, memory, edit latency, render cost, save time, and the first failing constraint. | Measured to 4096 playable; see [Baseline audit](#baseline-audit-wi-b4a-02) |
| B4a-2. Region-delta editing in WorldBuilder | Replace full-grid clones with rectangle plus before and after arrays per edited layer. Height, blend/material, passability/buildability, and object edits use one region-delta pattern. | Not started |
| B4a-3. New Map wizard for large maps | Add large-map presets, 16-bit elevation defaults, and memory guidance without a hard cap. | Not started |
| B4a-4. Terrain streaming and CDLOD | Select terrain by quadtree, page visible chunks, build render meshes on demand, and morph continuously between LOD levels. | Not started |
| B4a-5. Runtime chunk store | Provide chunk residency, paging, dirty tracking, and region access over B4b's 128-interval chunk layout. H3 serializes the chunks. | Not started |
| B4a-6. Round-trip and play validation | Validate large maps through WorldBuilder save, reload, engine load, and play. | Not started |
| B4a-7. Large-population scenario authoring | Scale object placement, selection, multi-object edit/undo, and editor rendering for large populations. | Not started |

## Cards

| Card | Title | Status | Prerequisites |
|---|---|---|---|
| [`WI-B4a-01`](https://projects.reid-corp.com/work_packages/351) | Ground the B4a-5 chunk contract | Ready | none |
| [`WI-B4a-02`](https://projects.reid-corp.com/work_packages/352) | Audit WorldBuilder large-map limits | Ready | A5-6 checklist item |
| [`WI-B4a-03`](https://projects.reid-corp.com/work_packages/353) | Add the region-delta edit contract | Ready | A5-6 checklist item |
| [`WI-B4a-04`](https://projects.reid-corp.com/work_packages/354) | Remove full-grid height brush clones | Draft | `WI-B4a-03` |
| [`WI-B4a-05`](https://projects.reid-corp.com/work_packages/355) | Apply region deltas to map layers | Draft | `WI-B4a-03` |
| [`WI-B4a-06`](https://projects.reid-corp.com/work_packages/356) | Add large-map wizard presets | Ready | A5-6 checklist item |
| [`WI-B4a-07`](https://projects.reid-corp.com/work_packages/357) | Design CDLOD and streaming seams | Draft | `WI-B4a-01` |
| [`WI-B4a-08`](https://projects.reid-corp.com/work_packages/358) | Implement CDLOD quadtree selection | Draft | `WI-B4a-07` |
| [`WI-B4a-09`](https://projects.reid-corp.com/work_packages/359) | Stream visible terrain patches | Draft | `WI-B4a-07` |
| [`WI-B4a-10`](https://projects.reid-corp.com/work_packages/360) | Implement runtime chunk store | Draft | `WI-B4a-01`, `WI-H3-01`, `WI-H3-02` |
| [`WI-B4a-11`](https://projects.reid-corp.com/work_packages/361) | Validate large-map round-trip and play | Draft | `WI-B4a-10`, H6 |

## Sequencing notes

- B4a-1 is an investigation card and can run before code changes.
- B4a-2 and B4a-3 are WorldBuilder-local and can proceed before H3 package encoding.
- B4a-4 and B4a-5 depend on the chunk contract from `WI-B4a-01`.
- B4a-5 must coordinate with H3. B4a owns runtime residency and dirty state; H3 owns bytes on disk.
- B4a-7 can begin after B4a-2 establishes region-object edit patterns and B5 provides rendering scale hooks.

## Architecture Diagrams

### Current types and B4a additions

```mermaid
classDiagram
  class MapFile {
    +HeightMapData HeightMapData
    +BlendTileData BlendTileData
    +ObjectsList ObjectsList
    +WriteTo(stream)
  }
  class HeightMapData {
    +uint Width
    +uint Height
    +uint Area
    +ushort[,] Elevations
  }
  class BlendTileData {
    +ushort[,] Tiles
    +uint[,] Blends
    +bool[,] Impassability
  }
  class ObjectsList {
    +MapObject[] Objects
    +AddObject(mapObject)
    +RemoveObject(mapObject)
  }
  class Terrain {
    +HeightMap HeightMap
    +int PatchSize
    -List~TerrainPatch~ _patches
    -CreatePatches()
  }
  class HeightMap {
    +int HorizontalScale
    +int Width
    +int Height
    +Vector3[,] Normals
    +GetHeight(x, y) float
  }
  class EditorMap {
    +MapFile MapFile
    +UndoRedoStack~IMapEdit~ UndoStack
    +Save()
  }
  class HeightMapEdit {
    +Capture(data, startX, startY, width, height, apply, desc) HeightMapEdit
  }
  class RegionDeltaEdit {
    <<B4a-2>>
    +Rectangle Bounds
    +LayerDeltas
    +Apply()
    +Revert()
  }
  class RuntimeTerrainChunkStore {
    <<B4a-5>>
    +GetChunk(cx, cy) TerrainChunk
    +LoadRegion(bounds)
    +EvictNonResident()
  }
  class TerrainChunk {
    <<B4a-5>>
    +ChunkKey Key
    +Rectangle Bounds
    +bool IsResident
    +bool IsDirty
  }
  class CdlodTerrainStreamer {
    <<B4a-4>>
    +Select(camera) CdlodSelection
    +BuildVisibleMeshes()
  }
  class MapPackage {
    <<H3>>
    +manifest.json
    +chunk layer files
  }

  MapFile --> HeightMapData
  MapFile --> BlendTileData
  MapFile --> ObjectsList
  Terrain --> HeightMap
  Terrain --> TerrainPatch
  EditorMap --> MapFile
  EditorMap --> HeightMapEdit
  RegionDeltaEdit --> HeightMapData
  RegionDeltaEdit --> BlendTileData
  RegionDeltaEdit --> ObjectsList
  RuntimeTerrainChunkStore --> TerrainChunk
  CdlodTerrainStreamer --> RuntimeTerrainChunkStore
  CdlodTerrainStreamer --> TerrainPatch
  MapPackage ..> RuntimeTerrainChunkStore : serialized by H3
```

### Large map runtime flow

```mermaid
sequenceDiagram
  participant E as Engine load
  participant H2 as Map interchange
  participant Store as RuntimeTerrainChunkStore
  participant Streamer as CdlodTerrainStreamer
  participant Terrain as Terrain
  participant Camera as Camera

  E->>H2: Load map package through H2 seam
  H2-->>Store: Create runtime chunk store
  E->>Terrain: Create terrain renderer shell
  loop Each camera update
    Camera->>Streamer: camera position and view range
    Streamer->>Store: LoadRegion(visible chunk bounds)
    Store-->>Streamer: resident chunks
    Streamer->>Streamer: quadtree CDLOD selection and morph factors
    Streamer->>Terrain: build, update, or free visible meshes
  end
```

### WorldBuilder region edit and save

```mermaid
sequenceDiagram
  participant User as User pointer
  participant Tool as Editor tool
  participant Delta as RegionDeltaEdit
  participant Stack as UndoRedoStack
  participant Map as EditorMap
  participant H3 as H3 package writer

  User->>Tool: pointer down, move, up
  Tool->>Tool: track affected rectangle
  Tool->>Delta: capture before and after arrays per layer
  Tool->>Stack: Push(delta)
  Stack->>Delta: Apply()
  Map->>Map: mark dirty
  User->>Map: Save
  alt legacy normal map
    Map->>Map: MapFile.WriteTo(stream)
  else large map
    Map->>H3: write package through H2 seam
  end
```

## Related

- Track B overview: [`../track-b-scale.md`](../track-b-scale.md)
- B4b chunk layout: [`r-real-world-terrain.md#51a-chunk-layout`](r-real-world-terrain.md#51a-chunk-layout)
- Track H storage formats: [`../track-h-storage-formats.md`](../track-h-storage-formats.md)
- Architecture snapshot: [`../agent/current-state.md`](../agent/current-state.md)
