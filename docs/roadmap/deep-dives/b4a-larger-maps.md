# Deep Dive: Larger Maps (via WorldBuilder)

> Expands phase B4a of [Track B: Scale](../track-b-scale.md). Part of the
> [Large-Scale RTS Roadmap](../README.md).
>
> Audience: human overview of the larger-maps design.

Purpose: scale WorldBuilder and map loading toward very large maps and large populations. The
design treats terrain streaming and large-object authoring as the core requirements.

> B4a assumes that WorldBuilder's basic author → save → load → play round-trip already
> works at normal map sizes. That baseline is established in [A5-6](a5-visualization.md), an
> early Track A sub-phase. B4a scales beyond those limits; it is not the first audit of basic
> WorldBuilder functionality.

> WorldBuilder is scenario-authoring tooling and must scale with the sim. It handles terrain and a
> scenario's initial object population: units, structures, and props. A sim-scale experiment needs a
> sim-scale authoring path that can place and edit thousands to millions of objects, not just a large
> heightmap. Terrain scaling (B4a-1…6) and large-population scenario authoring (B4a-7) are the two
> halves of that. B4a-7 is the WorldBuilder counterpart to ReplaySketch's
> [A5-5](a5-visualization.md) authoring-at-scale work.

## Decisions

- No size cap. Design for arbitrarily large maps. The goal is to scale map size over time to
  experiment with scaling techniques, so size must be bounded by disk, not RAM or the file format.
  Streaming/chunking (B4a-5) is therefore a core requirement, and every per-cell-resident
  assumption (C1, C5) must eventually go.
- Original-engine `.map` compatibility is not required. OpenSAGE may extend or replace the `.map`
  format for large maps from the start, consistent with the roadmap principle that scale and
  extensibility outrank fidelity. Reading existing original maps remains valuable, but writing must
  not be constrained by the original format's limits. This removes the compatibility half of C6 as a
  blocker.

## How maps are sized today

- Terrain is a heightmap grid. `HeightMapData`
  (`src/OpenSage.Game/Data/Map/HeightMapData.cs`) stores `Width`/`Height` (tiles) and a full
  `ushort[,] Elevations` array, plus `Area = Width  Height`. Generals maps store elevations as
  `uint8`; BFME and later as `uint16` (`ElevationsAre16Bit`, version ≥ 5).
- Companion per-tile data scales with area too. Blend/texture tiles (`BlendTileData`), passability,
  and similar data are all per-cell.
- Rendering splits terrain into fixed 17×17-vertex patches. `Terrain`
  (`src/OpenSage.Game/Terrain/Terrain.cs`, `PatchSize = 17`) builds a `List<TerrainPatch>` covering
  the whole map up front. There is no streaming or terrain LOD.
- Spatial systems scale with cell count. `PartitionCellManager`, the `Quadtree`
  (`src/OpenSage.Game/DataStructures/`), and the navigation graph
  (`src/OpenSage.Game/Navigation/`) are all sized from map dimensions.
- WorldBuilder authoring assumes the full grid is resident and cheap to copy. The New Map wizard
  (`NewMapWizardPage.xaml.cs`) only enforces minimums (width/height ≥ 10, border ≥ 2); there is no
  upper bound. Height-edit tooling clones the entire elevation array per stroke
  (`HeightBrushTool._strokeBefore = Elevations.Clone()`), so each edit is O(map area).

## Constraints to lift

| # | Constraint | Where | Why it blocks large maps |
|---|---|---|---|
| C1 | Whole heightmap + per-cell arrays resident in memory | `HeightMapData`, `BlendTileData` | Memory grows with map area; very large maps exhaust RAM. |
| C2 | All terrain patches built and kept up front; no LOD/streaming | `Terrain.cs` | Build time, GPU memory, and draw/cull cost grow with area. |
| C3 | Per-stroke full-array clone in the editor | `HeightBrushTool.cs` and other tools | Editing latency grows with map area, not brush size. |
| C4 | Undo/redo snapshots are full-grid | WorldBuilder edit/undo model | Memory + latency per edit scale with area. |
| C5 | Spatial/path/partition sized to full grid eagerly | `PartitionCellManager`, `Quadtree`, `Navigation` | Allocation and per-tick cost grow with area (ties into B2/B4). |
| C6 | No explicit, validated max size; `.map` format itself has size/precision limits | `NewMapWizardPage.xaml.cs`, `MapFile`, `HeightMapData` | Without an extended/replacement format, very large maps cannot be represented. The compatibility half is already resolved by the Decisions section. |

## Sub-phases

| Phase | Goal | Status |
|---|---|---|
| B4a-1. Baseline & limits audit | Measure where large maps break today: load time, memory, edit latency, render cost. Document the practical ceiling and which constraint (C1–C6) fails first to prioritize streaming/chunking work, not to pick a max size. See Decisions. | Not started |
| B4a-2. Region-based editing in WorldBuilder | Replace full-array clone/undo with dirty-region edits: tools capture only the affected rectangle, the brush already tracks stroke bounds in `HeightBrushTool` min/max X/Y, and undo/redo stores region deltas. Edit latency becomes proportional to brush size rather than map size. | Not started |
| B4a-3. New Map wizard for large maps | Surface large-size presets and validation in the New Map wizard: no hard cap, memory/perf guidance for the resident-grid path, and 16-bit elevation by default for large maps. | Not started |
| B4a-4. Terrain streaming & LOD | Build terrain patches on demand around the camera, free distant patches, and add terrain LOD so render cost scales sub-linearly with map area (ties into B5). | Not started |
| B4a-5. Chunked terrain storage & extended map format | Core requirement. Tile the heightmap/blend data into chunks so the whole grid need not be resident, and extend or replace the `.map` format to store chunked, arbitrarily large terrain. This resolves C1/C5/C6 and feeds B6 spatial sharding. | Not started |
| B4a-6. Round-trip & play validation | Validate large maps author → save → reload in WorldBuilder → play in the engine; confirm partition/pathfinding/rendering behave at increasing sizes. | Not started |
| B4a-7. Large-population scenario authoring | Scale WorldBuilder beyond terrain to placing and editing large object populations: bulk/procedural placement of units and structures, region-based multi-object edit/undo that extends B4a-2's dirty-region model from cells to objects, scalable selection/iteration, and instanced editor rendering that reuses [B5](../track-b-scale.md). This is the authoring counterpart to the engine-side [A5 scenario setup](a5-visualization.md) and ReplaySketch's [A5-5](a5-visualization.md). | Not started |

## Sequencing notes

- B4a-1 is a prerequisite for the rest. Optimize against measured limits, not guesses.
- B4a-2/3 are WorldBuilder-local and low-risk. They make large-map authoring usable before the
  engine-side streaming work lands.
- B4a-4/5 are engine-side and overlap with B2, B4, and B5. Coordinate so terrain chunking and the
  partition/quadtree work share a chunk/region concept.
- B4a-5 is required by the no-cap decision and is the natural bridge to B6 distributed simulation
  through spatial sharding. Coordinate the chunk/region concept with B6-1.
- B4a-7 reuses B4a-2's dirty-region edit/undo model, lifted from cells to objects, and B5's
  instanced rendering. It pairs with the engine-side [A5-5](a5-visualization.md) so the author and replay
  tools scale together. It can proceed in parallel with terrain streaming once B4a-2 exists.

## Architecture Diagrams

These diagrams show the current map and terrain types, and the types B4a adds on top of them.
Types that exist today carry no stereotype. Types B4a introduces carry a sub-phase stereotype
(`<<B4a-4>>`, `<<B4a-5>>`, `<<B4a-7>>`) matching the [sub-phase table](#sub-phases), and are drawn
with a colored accent via `classDef`. For the rationale behind streaming, chunking, and
large-population authoring, see the sections above; these diagrams cover only structure and flow.

### Current types and B4a additions

`MapFile` (`src/OpenSage.Game/Data/Map/MapFile.cs`) owns the resident map data. `HeightMapData`
holds the full `ushort[,] Elevations` grid and validates `Width * Height == Area`; `BlendTileData`
holds per-cell tile arrays; `ObjectsList` holds the flat `MapObject[]`. `Terrain`
(`src/OpenSage.Game/Terrain/Terrain.cs`) builds one `TerrainPatch` per `PatchSize` (17) block over
the whole `HeightMap` up front in `CreatePatches`. On the editor side, `EditorMap` wraps a `MapFile`
and an `UndoRedoStack~IMapEdit~`; tools push region-scoped `IMapEdit` records. B4a-5 replaces the
single resident grid with chunked storage, B4a-4 builds patches on demand, and B4a-7 lifts the
region-edit model from cells to objects.

```mermaid
classDiagram
  class MapFile {
    +HeightMapData HeightMapData
    +BlendTileData BlendTileData
    +ObjectsList ObjectsList
    +FromFileSystemEntry(entry) MapFile
    +WriteTo(stream)
  }
  class HeightMapData {
    +uint Width
    +uint Height
    +uint Area
    +uint BorderWidth
    +bool ElevationsAre16Bit
    +ushort[,] Elevations
  }
  class BlendTileData {
    +ushort[,] Tiles
    +uint[,] Blends
  }
  class ObjectsList {
    +MapObject[] Objects
    +AddObject(mapObject)
    +RemoveObject(mapObject)
  }
  class Terrain {
    +HeightMap HeightMap
    +int PatchSize$
    -List~TerrainPatch~ _patches
    +OnHeightMapChanged()
    -CreatePatches()
  }
  class TerrainPatch {
    +Rectangle Bounds
    +AxisAlignedBoundingBox BoundingBox
  }
  class HeightMap {
    +int HorizontalScale$
    +int Width
    +int Height
    +GetHeight(x, y) float
  }
  class EditorMap {
    +MapFile MapFile
    +UndoRedoStack~IMapEdit~ UndoStack
    +CreateNew(mapFile) EditorMap
    +Open(mapFile, path) EditorMap
    +Save()
  }
  class IMapEdit {
    <<interface>>
    +Apply()
    +Revert()
  }
  class HeightMapEdit {
    -int _startX
    -int _startY
    -ushort[,] _before
    -ushort[,] _after
    +Capture(data, startX, startY, w, h, apply, desc) HeightMapEdit
  }

  class ChunkedHeightMap:::b4a {
    <<B4a-5>>
    +int ChunkSize
    +IReadOnlyList~HeightMapChunk~ Chunks
    +LoadRegion(bounds) IEnumerable~HeightMapChunk~
  }
  class HeightMapChunk:::b4a {
    <<B4a-5>>
    +Rectangle WorldBounds
    +ushort[,] Elevations
    +bool IsResident
    +bool IsDirty
  }
  class TerrainPatchStreamer:::b4a {
    <<B4a-4>>
    +UpdateVisible(cameraBounds)
    +BuildPatch(bounds) TerrainPatch
    +FreePatch(bounds)
  }
  class ObjectPopulationEdit:::b4a {
    <<B4a-7>>
    +IReadOnlyList~MapObject~ Before
    +IReadOnlyList~MapObject~ After
    +Apply()
    +Revert()
  }

  MapFile *-- HeightMapData
  MapFile *-- BlendTileData
  MapFile *-- ObjectsList
  ObjectsList "1" --> "N" MapObject
  Terrain --> HeightMap
  Terrain "1" --> "N" TerrainPatch
  EditorMap --> MapFile
  EditorMap --> IMapEdit : UndoRedoStack
  HeightMapEdit ..|> IMapEdit
  ObjectPopulationEdit ..|> IMapEdit
  ChunkedHeightMap "1" --> "N" HeightMapChunk
  ChunkedHeightMap ..> HeightMapData : replaces resident grid
  TerrainPatchStreamer ..> ChunkedHeightMap : reads region
  TerrainPatchStreamer ..> TerrainPatch : builds on demand
  ObjectPopulationEdit ..> ObjectsList : bulk edit

  classDef b4a fill:#eef,stroke:#55f,stroke-width:2px
```

### Map load path for a large map with streaming

Today `Game.LoadMap` reads the whole `MapFile`, seeds `TerrainLogic.HeightMap`, then `Scene3D`
builds every `TerrainPatch` up front through `Terrain.CreatePatches`. B4a-5 makes the parse hydrate
`ChunkedHeightMap` instead of one resident grid, and B4a-4 has `TerrainPatchStreamer` build patches
only for chunks near the camera and free distant ones.

```mermaid
sequenceDiagram
  participant G as Game.LoadMap
  participant MF as MapFile
  participant HD as HeightMapData
  participant TL as TerrainLogic
  participant S as Scene3D
  participant T as Terrain
  participant STR as TerrainPatchStreamer
  participant CH as ChunkedHeightMap

  G->>MF: FromFileSystemEntry(entry)
  MF->>HD: Parse(reader)
  Note over HD,CH: B4a-5: parse into ChunkedHeightMap<br/>rather than one resident ushort[,]
  MF-->>G: MapFile
  G->>TL: SetHeightMapData(mapFile.HeightMapData)
  TL-->>TL: new HeightMap
  G->>S: new Scene3D(mapFile, ...)
  S->>T: new Terrain(mapFile, heightMap, ...)
  alt today
    T->>T: CreatePatches() over whole grid
    T->>T: new TerrainPatch per 17x17 block
  else B4a-4 streaming
    S->>STR: UpdateVisible(cameraBounds)
    STR->>CH: LoadRegion(cameraBounds)
    CH-->>STR: resident HeightMapChunks
    STR->>T: BuildPatch(bounds) for near chunks
    STR->>T: FreePatch(bounds) for distant chunks
  end
```

### WorldBuilder region edit and save

A height brush stroke captures only the affected rectangle. `HeightMapEdit.Capture` already records
a region `before`/`after` pair rather than the whole grid, and `UndoRedoStack.Push` applies it. The
remaining full-grid cost is in `HeightBrushTool`, which still clones the entire `Elevations` array
per stroke (`_strokeBefore = Elevations.Clone()`); B4a-2 removes that clone so stroke cost is
O(brush), not O(map area). `EditorMap.Save` writes the map back through `MapFile.WriteTo`.

```mermaid
sequenceDiagram
  participant U as User (pointer)
  participant HB as HeightBrushTool
  participant HD as HeightMapData
  participant HE as HeightMapEdit
  participant US as UndoRedoStack
  participant EM as EditorMap
  participant MF as MapFile

  U->>HB: OnPointerDown / OnPointerMove
  Note over HB: B4a-2: capture only the stroke rectangle,<br/>drop the full Elevations.Clone()
  HB->>HD: apply brush to cells in radius
  HB->>HB: track stroke min/max X and Y
  U->>HB: OnPointerUp
  HB->>HE: Capture(data, startX, startY, w, h, apply, desc)
  HE->>HD: read region before and after
  HB->>US: Push(edit)
  US->>HE: Apply()
  U->>EM: Save
  EM->>MF: WriteTo(stream)
```

## Related

- Track B overview: [`../track-b-scale.md`](../track-b-scale.md)
- Decision on the size ceiling & compatibility: [`../open-questions.md`](../open-questions.md)
- Architecture snapshot: [`../agent/current-state.md`](../agent/current-state.md)
