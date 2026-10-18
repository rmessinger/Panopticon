# Track A · A5-6: WorldBuilder functional baseline

> Audience: human overview above the divider; agent-context detail below it.

Audit half of the A5-6 row of [Track A](../track-a-foundations.md), card
[`WI-A5-01`](../agent/cards/WI-A5-01-worldbuilder-functional-audit.md). Read-only: classified
from code, no behavior fixed, nothing run interactively. The front-end is
`Panopticon.Tools.WorldBuilder.Avalonia` (no `.Maui` project exists;
`docs/worldbuilder-requirements.md` still carries MAUI-era prose).

Status after the audit (WI-A5-02/27/28/34/35, all landed): the table below is the point-in-time
audit evidence and is kept verbatim. Every round-trip defect it lists has since been repaired:
new maps initialize all writable chunks with engine-valid defaults (including skirmish sides,
start waypoints, one terrain texture, and the terrain border), edit application has a single
owner in `UndoRedoStack.Push`, companions persist beside the `.map`, and the author → save →
load → play round-trip is proven by `WorldBuilderRoundTripTests` (engine play leg included).
The UI-exposure gaps (unreachable tools, dead windows) remain open for B4a.

## Summary

WorldBuilder edits loaded maps adequately for terrain and object placement, but it cannot take
a map from creation to play. Of the six round-trip steps (create, save, companion files, open,
engine play, preview), one is implemented (open/parse) and the rest are partial or missing:

| Step | Verdict | Blocking defect |
|---|---|---|
| Create new map | partial | `MapFile.CreateEmpty` populates only `HeightMapData`, with `Borders` null |
| Save | partial | new maps NRE at `HeightMapData.WriteTo`; any placed object NREs `MapObject.WriteTo` (`Properties` null) |
| `map.ini` / `map.str` companions | missing | `EditorMap.Save` writes only the `.map` stream; the editors mutate strings nothing persists |
| Open / parse | implemented | `MapFile.FromStream` handles CkMp, RefPack, zlib |
| Engine play | missing | no `SidesList`, no player-start waypoints, wrong waypoint `TypeName`, engine requires the map inside the install file system |
| 3D preview | partial | `GameViewportRenderer` refuses maps without `SidesList`/`GlobalLighting` and maps outside the install |

Ten Core tools exist; four of them plus every tool-settings surface are unreachable from the UI,
and two UI windows (Scripts, Map Settings Apply) have no Core implementation. Undo exists for
all edit-producing tools but height-brush undo is a no-op (it discards the captured
before-patch) and placement double-adds objects.

Missing round-trip steps: 3 fully missing rows above (companions, engine play) plus the partial
rows' gaps; follow-up cards WI-A5-02, 27, 28 and the new WI-A5-34..36 cover them in round-trip
order (list below the divider).

B4a consumption: the size-dependent implementations are described in the last section below;
none were measured.

---

## Capability table

Ten tools, three edit types, all of `WorldBuilder.Core`:

| Capability | UI exposure | Undo | Persisted by `MapFile.WriteTo` | Notes |
|---|---|---|---|---|
| `SelectTool` | toolbar + `A` | no (selection only) | n/a | O(n) nearest-object scan; `SelectionChanged` has no subscriber |
| `HeightBrushTool` (Mound, Dig, Smooth) | toolbar + `H`/`D` | broken | yes (`HeightMapData`) | pointer-up builds before/after patches then discards them; `Capture` re-reads post-stroke, so undo is a no-op; two full-map `Elevations.Clone()` per stroke |
| `MeshMoldTool` | none | yes | yes | one edit per pointer-move (undo entry per mouse event); applies 3x, idempotent |
| `PaintSingleTileTool` | toolbar + `T` | yes | yes (`BlendTileData`) | `SelectedTileIndex` defaults 0, no UI setter; early-returns when `BlendTileData` is null (no-op on new maps) |
| `PaintLargeTileTool` | none | yes | yes | `TileSize^2` separate 1x1 edits per click; no batch edit type |
| `SpillPaintTool` | none | yes | yes | BFS capped at 10,000 tiles (silent truncation); one edit + one `StateChanged` per tile |
| `EyeDropperTool` | toolbar | n/a | n/a | `TileSampled` event has no subscriber |
| `PlaceObjectTool` | toolbar + `P` | yes | yes (`ObjectsList`) | `SelectedObjectType` null with no UI setter: tool inert from the UI; early-returns when `ObjectsList` is null (no-op on new maps); `MapObject.Properties` left null |
| `WaypointTool` | toolbar | yes | yes | writes `TypeName` `"Waypoints/Waypoint"`; engine expects `"*Waypoints/Waypoint"` plus `waypointID`/`waypointName` properties; `Properties` null |
| `DeleteTool` | menu + Delete key | yes | yes | removes `SelectTool.SelectedObject` via deletion edit |
| `HeightMapEdit` | (edit type) | yes | via height map | rectangular before/after patch pair |
| `BlendTileEdit` | (edit type) | yes | via blend tiles | 1x1 patch; no multi-tile variant |
| `ObjectPlacementEdit` | (edit type) | yes | via objects list | `UndoRedoStack.Push` applies, and the factory pre-applies: placements add the same `MapObject` reference twice (`ObjectsList.AddObject` appends) |

No edit types exist for sides/players, world info, lighting, blend properties, or waypoint data;
those windows are read-only or no-ops.

## Core without UI

`MeshMoldTool`, `PaintLargeTileTool`, `SpillPaintTool` (instantiated in `EditorViewModel` but no
toolbar, menu, or shortcut), and every settings surface: `BrushSettings`
(radius/strength/delta), `MeshMoldTool.TargetElevation`, `PaintLargeTileTool.TileSize`,
`SelectedTileIndex`, `PlaceObjectTool.SelectedObjectType`, `WaypointTool.WaypointName`,
`SelectTool.SelectedObject` display. The left settings panel is an empty `StackPanel`;
`IEditorTool.GetPropertiesViewModel()` is never called.

## UI without Core

Scripts window ("not yet implemented" message box), Map Settings Apply (TODO no-op; weather and
time of day never reach `WorldInfo`), Player List and Teams windows (read-only, placeholder rows
for maps without players/teams), the seven View toggles (set `GameViewportRenderer` properties
nothing reads), and the Map INI / String Table editors (strings never persisted).

## Round-trip detail

- Create: `EditorMap.CreateNew(MapFile)` wraps a caller-built map; `MapFile.CreateEmpty`
  populates `HeightMapData` only. Null: `Borders` (NREs `HeightMapData.WriteTo`),
  `MinZ`/`MaxZ`/`Version`, and every other chunk (`BlendTileData`, `WorldInfo`, `SidesList`,
  `ObjectsList`, `GlobalLighting`, `WaypointsList`, teams, scripts, triggers).
- Save: `EditorMap.Save` uses `MapFile.WriteTo` (uncompressed CkMp). New maps throw at
  `Borders`, then at the null chunks. `PlaceObjectTool`/`WaypointTool` objects carry null
  `Properties`, so a loaded-then-edited map also fails to save after any placement.
- Companions: `MapIniContent`/`MapStrContent` exist as properties, are editable in two windows,
  and are neither written on save nor read on open.
- Open: `MainWindow.OnMenuOpen` -> `MapFile.FromStream` handles CkMp, RefPack `EAR\0`, zlib
  `ZL5\0`.
- Engine play: `Game.LoadMap` resolves maps only through the `ContentManager` (the user-data
  root when configured, else the install file system), so editor-saved maps outside those roots
  cannot be started; `SidesListUtility.SetupSkirmishGameSides` throws without faction sides;
  `Scene3D` dereferences `WaypointsList`; player starts are waypoints named `Player_{i}_Start`
  (absent ones fall back to a fixed position) and none exist; WorldBuilder waypoints have the
  wrong `TypeName` and lack required properties, so the engine treats them as unknown objects.
- Preview: `GameViewportRenderer.LoadMapCore` refuses maps without `SidesList`/`GlobalLighting`
  and maps outside the install file system; editing still works against a blank viewport.

## What a new map lacks before the engine can play it

Players and factions (`SidesList`, including neutral and civilian slots), player start positions
(`Player_{i}_Start` waypoints), the waypoint list with engine-correct encoding, teams and
scripts, global lighting, world info (name, description), and blend tile data. All null or
absent from `MapFile.CreateEmpty`.

## Size-dependence for B4a

- `HeightBrushTool`: two full-map `Elevations.Clone()` per stroke plus an unused patch copy.
- `SpillPaintTool`: 10,000-tile hard cap; one edit and one event per filled tile.
- `PaintLargeTileTool`: `TileSize^2` independent edits per click; no batch edit.
- `MeshMoldTool`: one undo entry per pointer-move event.
- `SelectTool`: linear scan of all objects per click; no spatial index.
- `ObjectsList.AddObject`/`RemoveObject`: full-array copy per placement or deletion.
- `UndoRedoStack`: unbounded; memory scales with fill operations.
- `GameViewportRenderer.RenderFrame`: full height-map re-upload after every stroke;
  `WorldBuilderViewport.PresentFrame`: full GPU-to-CPU readback and per-pixel copy per frame.
- `MapFile.WriteTo`: full serialization executed twice (asset-name pre-pass through
  `BinaryWriter.Null`), one `ushort` per elevation cell, full-map blend and bit-packed grids.
- Full resident `ushort[,]` elevation and per-cell blend arrays in create/parse paths.
- `HorizontalScale = 10` re-declared privately in six tool files instead of sharing
  `Panopticon.Terrain.HeightMap.HorizontalScale`.
- New-map wizard: no upper bound on dimensions.

## Tests

None exist. No WorldBuilder project under `test/`, no test references WorldBuilder, and the
solution contains only the two production projects. `WI-A5-02` is the planned round-trip test.
The only non-interactive execution path is `Program.ScreenshotHeadless`, which exercises
`NewMapFactory.Create` + `LoadMap` and asserts nothing.
