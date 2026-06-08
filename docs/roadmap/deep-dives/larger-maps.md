# Deep Dive: Larger Maps (via WorldBuilder)

> Expands phase B4a of [Track B — Scale](../track-b-scale.md). Part of the
> [Large-Scale RTS Roadmap](../README.md).

The first concrete scale milestone is bigger maps, pursued through the WorldBuilder
editor (`OpenSage.Tools.WorldBuilder.`) since it is where maps are authored and is the
fastest path to exercising large terrain end-to-end (author → save `.map` → play).

> B4a assumes that WorldBuilder's basic author → save → load → play round-trip already
> works at normal map sizes. That baseline is established in [A5-6](visualization.md) (an
> early Track A sub-phase). B4a then scales beyond those limits; it must not be the first
> time basic WorldBuilder functionality is audited.

> WorldBuilder is scenario-authoring tooling, and must scale with the sim. Beyond terrain,
> WorldBuilder is where a scenario's initial object population (units, structures, props) is
> laid out. A sim-scale experiment needs a sim-scale authoring path — placing and editing
> thousands–millions of objects, not just a big heightmap. Terrain scaling (B4a-1…6) and
> large-population scenario authoring (B4a-7) are the two halves of that, and B4a-7 is the
> WorldBuilder counterpart to ReplaySketch's [A5-5](visualization.md) authoring-at-scale work.

## Decisions

- No size cap — design for arbitrarily large maps. The goal is to gradually scale map size
  up over time to experiment with scaling techniques, so size must be bounded by disk, not
  RAM or the file format. This makes streaming/chunking (B4a-5) a core requirement, not
  an optional one, and means every per-cell-resident assumption (C1, C5) must eventually go.
- Original-engine `.map` compatibility is not required. OpenSAGE is free to extend or
  replace the `.map` format for large maps from the start (consistent with the roadmap
  principle that scale/extensibility win over fidelity). Reading existing original maps remains
  valuable, but writing must not be constrained to the original format's limits. This removes
  constraint C6's compatibility half as a blocker.

## How maps are sized today

- Terrain is a heightmap grid. `HeightMapData`
  (`src/OpenSage.Game/Data/Map/HeightMapData.cs`) stores `Width`/`Height` (tiles) and a full
  `ushort[,] Elevations` array, plus `Area = Width  Height`. Generals maps store elevations
  as `uint8`; BFME and later as `uint16` (`ElevationsAre16Bit`, version ≥ 5).
- Companion per-tile data scales with area too — blend/texture tiles
  (`BlendTileData`), passability, etc. are all per-cell.
- Rendering splits terrain into fixed 17×17-vertex patches. `Terrain`
  (`src/OpenSage.Game/Terrain/Terrain.cs`, `PatchSize = 17`) builds a `List<TerrainPatch>`
  covering the whole map up front; there is no streaming or terrain LOD.
- Spatial systems scale with cell count. `PartitionCellManager`, the `Quadtree`
  (`src/OpenSage.Game/DataStructures/`), and the navigation graph
  (`src/OpenSage.Game/Navigation/`) are all sized from map dimensions.
- WorldBuilder authoring assumes the full grid is resident and cheap to copy. The New Map
  wizard (`NewMapWizardPage.xaml.cs`) only enforces minimums (width/height ≥ 10, border ≥ 2)
  — there is no upper bound. Height-edit tooling clones the entire elevation array per stroke
  (`HeightBrushTool._strokeBefore = Elevations.Clone()`), which is O(map area) per edit.

## Constraints to lift

| # | Constraint | Where | Why it blocks large maps |
|---|---|---|---|
| C1 | Whole heightmap + per-cell arrays resident in memory | `HeightMapData`, `BlendTileData` | Memory grows with map area; very large maps exhaust RAM. |
| C2 | All terrain patches built and kept up front; no LOD/streaming | `Terrain.cs` | Build time, GPU memory, and draw/cull cost grow with area. |
| C3 | Per-stroke full-array clone in the editor | `HeightBrushTool.cs` and other tools | Editing latency grows with map area, not brush size. |
| C4 | Undo/redo snapshots are full-grid | WorldBuilder edit/undo model | Memory + latency per edit scale with area. |
| C5 | Spatial/path/partition sized to full grid eagerly | `PartitionCellManager`, `Quadtree`, `Navigation` | Allocation and per-tick cost grow with area (ties into B2/B4). |
| C6 | No explicit, validated max size; `.map` format itself has size/precision limits | `NewMapWizardPage.xaml.cs`, `MapFile`, `HeightMapData` | Without an extended/replacement format, very large maps can't even be represented. (Compatibility half resolved — see Decisions: we may extend/replace the format.) |

## Sub-phases

| Phase | Goal | Status |
|---|---|---|
| B4a-1. Baseline & limits audit | Measure where large maps break today (load time, memory, edit latency, render cost) across representative sizes. Document the practical ceiling and which constraint (C1–C6) bites first, to prioritize the streaming/chunking work — not to pick a max size (there is none; see Decisions). | Not started |
| B4a-2. Region-based editing in WorldBuilder | Replace full-array clone/undo with dirty-region edits: tools capture only the affected rectangle (the brush already tracks stroke bounds — `HeightBrushTool` min/max X/Y), and undo/redo stores region deltas. Makes edit latency proportional to brush size, not map size. | Not started |
| B4a-3. New Map wizard for large maps | Surface large-size presets and validation in the New Map wizard: no hard cap, memory/perf guidance for the resident-grid path, and 16-bit elevation default for large maps. | Not started |
| B4a-4. Terrain streaming & LOD | Build terrain patches on demand around the camera, free distant patches, and add terrain LOD so render cost scales sub-linearly with map area (ties into B5). | Not started |
| B4a-5. Chunked terrain storage & extended map format | Core requirement. Tile the heightmap/blend data into chunks so the whole grid need not be resident, and extend/replace the `.map` format to store chunked, arbitrarily large terrain. Enables editing and play of maps larger than memory (resolves C1/C5/C6) and feeds B6 spatial sharding. | Not started |
| B4a-6. Round-trip & play validation | Validate large maps author → save → reload in WorldBuilder → play in the engine; confirm partition/pathfinding/rendering behave at increasing sizes. | Not started |
| B4a-7. Large-population scenario authoring | Scale WorldBuilder beyond terrain to placing and editing large object populations: bulk/procedural placement of units and structures, region-based multi-object edit/undo (extend B4a-2's dirty-region model from cells to objects), scalable selection/iteration, and instanced editor rendering (reuse [B5](../track-b-scale.md)) so authoring a sim-scale scenario stays interactive. The authoring counterpart to the engine's [A5 scenario setup](visualization.md) and to ReplaySketch's [A5-5](visualization.md). | Not started |

## Sequencing notes

- B4a-1 is a prerequisite for the rest — we optimize against measured limits, not guesses.
- B4a-2/3 are WorldBuilder-local and low-risk; they make authoring large maps usable
  before the engine-side streaming work lands.
- B4a-4/5 are engine-side and overlap with B2 (allocation cleanup), B4 (spatial
  scaling), and B5 (rendering at scale) — coordinate so terrain chunking and the
  partition/quadtree work share a chunk/region concept.
- B4a-5 (chunked storage & extended format) is a core requirement given the no-cap
  decision, and is the natural bridge to B6 (distributed simulation via spatial sharding) —
  the chunk is the shard's unit of terrain. Coordinate the chunk/region concept with B6-1.
- B4a-7 (large-population authoring) reuses B4a-2's dirty-region edit/undo model (lifted
  from cells to objects) and B5's instanced rendering, and pairs with the engine-side
  [A5-5](visualization.md) so the author and replay tools scale together. It can proceed in
  parallel with terrain streaming once region-based editing (B4a-2) exists.

## Related

- Track B overview: [`../track-b-scale.md`](../track-b-scale.md)
- Decision on the size ceiling & compatibility: [`../open-questions.md`](../open-questions.md)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
