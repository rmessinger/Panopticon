# Track R · R1-R2: Geospatial map generation (formerly B4b)

> Audience: human overview above the divider; agent-context detail below it.

Implements B4b-1 through B4b-6b, phases R1 and R2 of [Track R](../track-r-real-world-terrain.md) (formerly Track B phase B4b). Design source of truth:
[deep-dives/b4b-geospatial-map-generation.md](../deep-dives/b4b-geospatial-map-generation.md),
including its [architecture diagrams](../deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams).

## Scope

Build the CPU-only `Panopticon.Geospatial` pipeline from operator-supplied KML/KMZ and GeoTIFF
sources to a standalone chunked payload, plus a dev-only preview that converts a payload into a
playable legacy `MapFile`. B4b-7 (H2 interchange), B4b-8 (WorldBuilder import), and B4b-9
(operator proof) stay blocked on B4a and H.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| B4b-1 | [`WI-B4b-01`](../agent/cards/WI-B4b-01-scaffold-geospatial-project.md) | Projects, diagnostics, limits. |
| B4b-1 | [`WI-B4b-02`](../agent/cards/WI-B4b-02-import-manifest.md) | Manifest schema and loader. |
| B4b-2 | [`WI-B4b-03`](../agent/cards/WI-B4b-03-interchange-values.md) | Interchange values and interfaces. |
| B4b-4 | [`WI-B4b-04`](../agent/cards/WI-B4b-04-transverse-mercator.md) | Krüger Transverse Mercator. |
| B4b-4 | [`WI-B4b-05`](../agent/cards/WI-B4b-05-coordinate-transformer.md) | `ICoordinateTransformer` for supported CRS. |
| B4b-4 | [`WI-B4b-06`](../agent/cards/WI-B4b-06-local-map-grid.md) | `LocalMapGrid`, chunk rounding, scale-error check. |
| B4b-3 | [`WI-B4b-07`](../agent/cards/WI-B4b-07-kml-reader.md) | KML reader. |
| B4b-3 | [`WI-B4b-08`](../agent/cards/WI-B4b-08-kmz-archive.md) | KMZ archive reader. |
| B4b-3 | [`WI-B4b-09`](../agent/cards/WI-B4b-09-geotiff-fixtures.md) | Synthetic GeoTIFF fixtures. |
| B4b-3 | [`WI-B4b-10`](../agent/cards/WI-B4b-10-geotiff-reader.md) | GeoTIFF reader. |
| B4b-5 | [`WI-B4b-11`](../agent/cards/WI-B4b-11-raster-sampling.md) | Sampling kernels, no-data and edge policies. |
| B4b-5 | [`WI-B4b-12`](../agent/cards/WI-B4b-12-terrain-chunks.md) | Terrain chunks. |
| B4b-5 | [`WI-B4b-13`](../agent/cards/WI-B4b-13-imagery-chunks.md) | Imagery chunks. |
| B4b-5 | [`WI-B4b-14`](../agent/cards/WI-B4b-14-material-layer.md) | Material layer. |
| B4b-6 | [`WI-B4b-15`](../agent/cards/WI-B4b-15-feature-clipping.md) | Feature transform and clipping. |
| B4b-6 | [`WI-B4b-16`](../agent/cards/WI-B4b-16-feature-mapping.md) | Feature mapping rules. |
| B4b-6 | [`WI-B4b-17`](../agent/cards/WI-B4b-17-terrain-masks.md) | Passability and buildability masks. |
| B4b-6a | [`WI-B4b-18`](../agent/cards/WI-B4b-18-import-pipeline-payload.md) | Import pipeline and payload. |
| B4b-6a | [`WI-B4b-19`](../agent/cards/WI-B4b-19-geoimport-cli.md) | `Panopticon.Tools.GeoImport` CLI. |
| B4b-6a | [`WI-B4b-20`](../agent/cards/WI-B4b-20-control-point-validation.md) | Control-point validation. |
| B4b-6b | [`WI-B4b-21`](../agent/cards/WI-B4b-21-audit-playable-mapfile.md) | Minimum playable `MapFile` audit. |
| B4b-6b | [`WI-B4b-22`](../agent/cards/WI-B4b-22-mapfile-defaults.md) | Legacy `MapFile` default factories. |
| B4b-6b | [`WI-B4b-23`](../agent/cards/WI-B4b-23-payload-to-mapfile.md) | `Panopticon.Geospatial.Preview`. |
| B4b-6b | [`WI-B4b-24`](../agent/cards/WI-B4b-24-preview-evidence.md) | `preview` verb and game-backed evidence. |

Related design cards: [`WI-B4a-01`](../agent/cards/WI-B4a-01-chunk-contract.md) (B4a-5 chunk
contract), [`WI-H1-01`](../agent/cards/WI-H1-01-map-format-audit.md),
[`WI-H1-02`](../agent/cards/WI-H1-02-format-decision-options.md), and
[`WI-H2-01`](../agent/cards/WI-H2-01-map-interchange-design.md).

---

## Agent context

## Dependency order

| Card | Prerequisites |
|---|---|
| 01, 21 | none |
| 02, 03 | 01 |
| 04 | 01, 03 |
| 05 | 03, 04 |
| 06 | 02, 05 |
| 07, 09 | 03 |
| 11 | 02, 03 |
| 08 | 07 |
| 10 | 03, 09 |
| 12 | 06, 11 |
| 13 | 12 |
| 15 | 06, 07 |
| 14, 16 | 12, 15 |
| 17 | 12, 16 |
| 18 | 02, 08, 10, 13, 14, 16, 17 |
| 19, 20 | 18 |
| 22 | 21 |
| 23 | 18, 22 |
| 24 | 19, 20, 23 |

`WI-B4b-21` and `WI-B4b-22` touch only `Panopticon.Simulation` and can run in parallel with the
geospatial chain.

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Geospatial/` | diagnostics, limits |
| `.../Manifest/` | manifest records, `ManifestLoader` |
| `.../Interchange/` | CRS ids, affine, rasters, features, provenance, interfaces |
| `.../Projection/` | Transverse Mercator, transformer |
| `.../Grid/` | `ChunkLayout`, `LocalMapGrid` |
| `.../Readers/` | KML, KMZ, GeoTIFF |
| `.../Sampling/` | `RasterSampler` |
| `.../Generation/` | terrain, imagery, material, mask generators |
| `.../Features/` | clipping, mapping |
| `.../Pipeline/`, `.../Payload/`, `.../Validation/` | importer, payload I/O, control points |
| `src/Panopticon.Geospatial.Preview/` | payload to legacy `MapFile` |
| `src/Panopticon.Tools.GeoImport/` | CLI |
| `test/Panopticon.Geospatial.Tests/` | all B4b tests except `MapFile` factory tests |

## Invariants

- `Panopticon.Geospatial` references no Panopticon project; only `.Preview` references `Panopticon.Simulation`.
- No test downloads data; GeoTIFF fixtures are generated at test time.
- Every source raster is sampled exactly once per output sample (single inverse warp).
- Chunk traversal is row-major from the southwest; shared chunk edges are bit-identical.
- Failures surface as typed `GeoDiagnostic` codes, never parsed exception messages.

## Agent context: minimum playable map

`WI-B4b-21` audit at commit `3033e2f48`. File references below are relative to
`src/Panopticon.Simulation/` unless a project prefix is given:

- `MapFile.cs`, `HeightMapData.cs`, `BlendTileData.cs`, `WorldInfo.cs`, `GlobalLighting.cs`, `WaypointsList.cs`: `Data/Map/`
- `SidesList.cs`, `SidesListUtility.cs`: `Logic/Map/`
- `Game.cs`, `Scene3D.cs`: repo root of the project
- `Terrain.cs`, `HeightMap.cs`, `WaterAreaCollection.cs`: `Terrain/`
- `Navigation.cs`: `Navigation/`
- `Waypoints.cs`: `Scripting/`
- `PartitionCellManager.cs`: `Logic/`
- `PlayerSettings.cs`: `Logic/`
- `NewMapFactory.cs`: `src/Panopticon.Tools.WorldBuilder.Core/`

Load path: `StartSkirmishOrMultiPlayerGame` (`Game.cs:922-935`) calls `StartGame`
(`Game.cs:806-902`), which calls `LoadMap` (`Game.cs:756-784`): full parse via
`MapFile.FromFileSystemEntry`, `SidesListUtility.SetupGameSides`, `TerrainLogic.SetHeightMapData`,
then `new Scene3D` (`Scene3D.cs:128-175`, base constructor `Scene3D.cs:299-362`) and starting
buildings (`Game.cs:843-861`). `MapFile.ParseMapData` leaves absent sections null
(`MapFile.cs:215-353`), so the required set is exactly what `WriteTo` writes unconditionally.

### Section inventory

`WriteTo` (`MapFile.cs:355-534`) writes seven chunks unconditionally; every other chunk is
written only when present (`MapFile.cs:382-533`).

| Section | WriteTo | Load-path reads | Minimal value |
|---|---|---|---|
| `HeightMapData` | `MapFile.cs:394-395` | `Game.cs:773`, `Scene3D.cs:316,338-341`, `PartitionCellManager.cs:92` (`Borders[0]`), `HeightMap.cs:22-32,88,127` | Elevation grid `playable + 2*border + 1` per side, flat values accepted, `Width*Height == Area` (`HeightMapData.cs:85-89`), at least one `Borders` entry spanning the grid (`HeightMapData.cs:48-61`); `CreateEmpty` (`MapFile.cs:187-213`) |
| `BlendTileData` | `MapFile.cs:397-398` | `Terrain.cs:93-106,395-511`, `Navigation.cs:29-45` (`Scene3D.cs:318`) | `NumTiles == Width*Height` (`BlendTileData.cs:144-148`), zero-filled `Tiles`/`Blends`/`ThreeWayBlends`/`CliffTextures`, all-false passability arrays, one texture entry, empty blend/cliff tables, `MagicValue2 == 0`; `BlendTileData.Create` (`BlendTileData.cs:81-107`) |
| `WorldInfo` | `MapFile.cs:400-401` | none in the engine load path | empty `AssetPropertyCollection` (`WorldInfo.cs:12-16`) |
| `SidesList` | `MapFile.cs:409-410` | `SidesListUtility.cs:81-234`, `MapFile.cs:100-102` | skirmish sides per below; `SidesList.cs:54-84` |
| `ObjectsList` | `MapFile.cs:436-437` | `Scene3D.cs:149-157,177-277` | start waypoint map objects only; `ObjectsList` defaults to an empty list (`ObjectsList.cs:12`) |
| `GlobalLighting` | `MapFile.cs:493-494` | `Scene3D.cs:145-147` | exactly four configurations keyed by `TimeOfDay` plus a `Time` value (`GlobalLighting.cs:62-65,109-112`); `Create` (`GlobalLighting.cs:41-52`) |
| `WaypointsList` | `MapFile.cs:526-527` | `Scene3D.cs:275` | empty `WaypointPaths` (`WaypointsList.cs:12-16`) |

Optional sections with load-path reads, all null-tolerant:

- `EnvironmentData`: `Terrain.cs:117,121,155` use `?.` with fallbacks `tsnoiseurb.dds` and `tscloudmed.dds`.
- `NamedCameras`: `Scene3D.cs:153,273` tolerates null.
- `PolygonTriggers`, `StandingWaterAreas`, `StandingWaveAreas`: null-guarded in `WaterAreaCollection.cs:22,34,45` (`Scene3D.cs:317`).

Not read in the Generals load path, written only when present (`MapFile.cs:382-533`): `AssetList`,
`GlobalVersion`, `MPPositionList`, `LibraryMapLists`, `Teams`, `PlayerScriptsList`, `BuildLists`,
`TriggerAreas`, `GlobalWaterSettings`, `FogSettings`, `MissionHotSpots`, `MissionObjectives`,
`RiverAreas`, `PostEffectsChunk`, `CameraAnimationList`, `CastleTemplates`, `SkyboxSettings`.
These are BFME-and-later layouts; a Generals map omits them. `Teams` and `PlayerScriptsList`
in particular are the SidesList version >= 5 layout, see below.

### HeightMapData version rule

- `ElevationsAre16Bit` is `Version >= 5` (`HeightMapData.cs:11`); parse reads `UInt16` versus
  `Byte` elevations at `HeightMapData.cs:99-101`, write mirrors at `137-144`.
- `VerticalScale` is 0.0390625 at 16 bit and 0.625 at 8 bit; Generals stores 8-bit heights
  (`HeightMapData.cs:13-17`). Existing `CreateEmpty` leaves `Version` at the `Asset` default 0
  (`HeightMapData.cs:35-63`); the geospatial preview requires version 5 or later.
- `HeightMapBorder.Corner1X/Corner1Y` exist only at border version >= 6; Generals writes X and Y
  only (`HeightMapData.cs:169-207`).
- Retail map versions were not byte-verified (maps.big entries are RefPack-compressed); the 8-bit
  rule is taken from the scale comment above.

### BlendTileData version and value constraints

- Parse rejects version < 6 and requires `HeightMapData` before `BlendTileData`
  (`BlendTileData.cs:129-137`).
- `Impassability` is read only at version > 6 (`BlendTileData.cs:157-168`); the `Navigation`
  constructor dereferences it unconditionally (`Navigation.cs:31-42`), so version 6 is not
  playable. Version 7 rows do not round-trip: the writer emits unpadded rows while the reader
  clips to byte-aligned widths (`BlendTileData.cs:70-76,160-164`). Generated maps stamp version 8
  (`BlendTileData.cs:85`).
- `MagicValue2` must be 0 (`BlendTileData.cs:240-244`). Blend description and cliff mapping
  counts are written `+1` and read `-1`, so empty tables round-trip
  (`BlendTileData.cs:215-228,374,376`).
- Zero blend values fall back to the base texture (`Terrain.cs:456-479`); empty
  `CliffTextureMappings` produces a null buffer (`Terrain.cs:508-510`).
- `Tiles` values index `TextureIndices`; each texture contributes `(CellSize*2)^2` indices
  (`BlendTileData.cs:279-302`, `Terrain.cs:407`). An empty `Textures` list fails on the first
  tile (`BlendTileData.cs:77-79`). One entry with `CellSize` 4 covers tile values 0-63, so a
  zero-filled `Tiles` grid is valid.

### Terrain texture names

- A `BlendTileTexture.Name` must match a `TerrainTexture` block name. The engine resolves
  `AssetStore.TerrainTextures.GetByName(name)` and loads `Art\Terrain\<TerrainTexture.Texture>`
  (`Terrain.cs:533-535`).
- Verified against the local install: INI.big `Data\INI\Terrain.ini` defines 236 `Terrain
  <Name>` blocks. Families by count: GrassMedium 44, SandMedium 40, CliffMedium 37, CliffLarge 16,
  DirtMedium 15, SandLarge 12, Sand 11, Snow 10, Rocks 9, Concrete 7, ParkingLot 4, GrassLarge 3,
  Dirt 3, Asphalt 3, RocksMedium 3, Wood 2, Grass 2, ConcreteMedium 2, plus one-off transition,
  Crops, Cobblestone, DirtSnow, and SnowLarge entries.
- `GrassType1` (the `BlendTileData.Create` seed, `BlendTileData.cs:103`) maps to
  `Texture = TXGras01a.tga`, present as `Art\Terrain\TXGras01a.tga` in Terrain.big.
- INI.big `Data\INI\Default\Terrain.ini` defines only `DefaultTerrain` (`NoTexture.tga`).
- Generated maps pick names from `Terrain.ini` block names; arbitrary texture file names are
  invalid.

### Minimal skirmish configuration

- Sides: `SidesList.CreateSkirmish(factions)` produces `Players[0]` neutral (empty name), then a
  `FactionCivilian` player, then one player per faction template (`SidesList.cs:54-84`). The
  skirmish loader takes `Players[0]` as neutral, dereferences the civilian player, and matches
  faction players by `Faction == "Faction" + PlayerSetting.SideName`, throwing when no player
  matches (`SidesListUtility.cs:90-115`).
- Faction values are `PlayerTemplate` block names and must resolve in the installed
  `PlayerTemplate.ini` (`SidesListUtility.cs:332,390`). The verified two-slot configuration is
  `["FactionAmerica", "FactionGLA"]` (`NewMapFactory.cs:31`).
- Start waypoints: one `Player_{slot}_Start` waypoint object per slot with `waypointID = slot`,
  placed inside the playable area (`NewMapFactory.cs:55-71`). Encoding is map object type
  `*Waypoints/Waypoint` with `waypointID` and `waypointName` properties (`Waypoints.cs:90,100-103,127-133`);
  `LoadObjects` recognizes the type (`Scene3D.cs:205-208`).
- `CreateSkirmishPlayerStartingBuilding` resolves `Player_{StartPosition ?? 1}_Start`
  (`Scene3D.cs:470-476`, `Waypoints.cs:76-81`, `PlayerSettings.cs:21`), falls back to `(80, 80, 0)`
  when absent, then spawns `PlayerTemplate.StartingBuilding` and `StartingUnits[0]`
  (`Scene3D.cs:478-491`). Skipped under `Configuration.SkipStartingBuildings` (`Game.cs:843`).
- Complete example: `NewMapFactory.Create` (`NewMapFactory.cs:39-71`) builds exactly this:
  `CreateEmpty` terrain, `CreateSkirmish` sides, and one start waypoint per slot, driven at load
  by one `PlayerSetting` per slot with `SideName` `America`/`GLA` and `StartPosition` 1/2.

Install content consumed at skirmish start beyond the map: `Data\Scripts\SkirmishScripts.scb`
when the map carries no scripts (`SidesListUtility.cs:351-354`), `Data\Scripts\MultiplayerScripts.scb`
when more than one player and no map scripts (`SidesListUtility.cs:221-233`), and the
`PlayerTemplate` starting building and units above.

### Constraints for `WI-B4b-22`

- `SidesList` below version 5 nests teams and `PlayerScriptsList` inside the chunk
  (`SidesList.cs:116-153,187-203`); `Create` stamps version 3 so a new map round-trips
  (`SidesList.cs:54-65`).
- The civilian player is required: a missing civilian leaves `mapPlayers[1]` null and later
  dereferences crash (`SidesListUtility.cs:198-205,326,383`).
- Start waypoints are convention, not enforced: the fallback position stacks every player at one
  point, so a playable map needs one waypoint per slot by construction.
- Player-start positions use horizontal X/Y and terrain-relative Z: `Scene3D.cs:473-476`
  adds terrain height to the stored Z. Generated starts use Z zero. The existing
  `NewMapFactory.cs:65-69` swaps northing and elevation; it is not a coordinate-layout reference.
- `WorldInfo` has no engine-load read; an empty collection is sufficient.
- `SidesList.PrepareForMpOrSkirmish` (`SidesList.cs:449-512`) has no callers; the live skirmish
  path is `SidesListUtility`.
