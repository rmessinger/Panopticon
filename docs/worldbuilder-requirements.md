# WorldBuilder Map/Scenario Editor — Requirements

This document specifies the requirements for implementing a WorldBuilder-compatible map and scenario editor in OpenSAGE, based on the tooling described in the [CNC Labs WorldBuilder tutorials](https://www.cnclabs.com/maps/generals/worldbuilder/).

WorldBuilder is the official scenario editor shipped with *Command & Conquer: Generals* and *Zero Hour*. It produces `.map` files (already parsed by `OpenSage.FileFormats.Map`) and companion files (`map.ini`, `Map.str`). The editor should round-trip with those formats so that maps authored in OpenSAGE can be played in the original engine and vice versa.

---

## UI Framework and Architecture

The editor is implemented as a **.NET MAUI** desktop application following the same split-project pattern as `OpenSage.Tools.ReplaySketch`:

| Project | Purpose |
|---|---|
| `OpenSage.Tools.WorldBuilder.Core` | Platform-agnostic editor logic: `EditorMap`, tools, undo/redo, viewport renderer |
| `OpenSage.Tools.WorldBuilder.Maui` | MAUI application: XAML views, ViewModels, platform-specific 3-D viewport handler |

### UI Guidelines

- **Light and dark mode** are both supported via `AppThemeBinding` (consistent with ReplaySketch.Maui).
- Controls use standard MAUI components (`Entry`, `Picker`, `Slider`, `CheckBox`, `CollectionView`, `ListView`) rather than custom-drawn widgets, giving a platform-native look and feel.
- Dialogs are MAUI `ContentPage` modals pushed onto the navigation stack or displayed via `DisplayAlert` / `DisplayPromptAsync` where appropriate.
- Panels that must show long lists (object browser, script list, team list) use `CollectionView` with `DataTemplate` for efficient virtualised rendering.
- The layout is **MVVM**: each view binds to a corresponding ViewModel in `OpenSage.Tools.WorldBuilder.Maui/ViewModels/`.

### 3-D Viewport Embedding

- The 3-D viewport is implemented as a custom MAUI `ContentView` (`VeldridViewport`) backed by a platform-specific `ViewHandler`.
- On **Windows**: the handler creates a Win32 child `HWND`; Veldrid targets it with the Direct3D 11 backend.
- On **macOS**: the handler creates a `CAMetalLayer`-backed `NSView`; Veldrid targets it with the Metal backend.
- The MAUI project's `net10.0-windows` and `net10.0-maccatalyst` targets each provide the concrete `VeldridViewportHandler` in `Platforms/`.
- The Core project's `GameViewportRenderer` manages the `OpenSage.Game` instance and renders the `Scene3D` to the native surface each frame. It exposes mouse/keyboard event hooks that the MAUI handler forwards.

---

## Table of Contents

1. [Application Shell](#1-application-shell)
2. [Viewport](#2-viewport)
3. [Toolbar](#3-toolbar)
4. [Object Properties Panel](#4-object-properties-panel)
5. [Terrain Editing](#5-terrain-editing)
6. [Water Editing](#6-water-editing)
7. [Texture Painting](#7-texture-painting)
8. [Object Placement](#8-object-placement)
9. [Roads and Bridges](#9-roads-and-bridges)
10. [Vegetation (Shrubbery / Grove)](#10-vegetation-shrubbery--grove)
11. [Ramps](#11-ramps)
12. [Fences](#12-fences)
13. [Smudges](#13-smudges)
14. [Waypoints and Paths](#14-waypoints-and-paths)
15. [Polygon / Trigger Areas](#15-polygon--trigger-areas)
16. [Map Border](#16-map-border)
17. [Scripting System](#17-scripting-system)
18. [Teams Editor](#18-teams-editor)
19. [Player List / Skirmish Setup](#19-player-list--skirmish-setup)
20. [Map Settings](#20-map-settings)
21. [Companion Files (map.ini / Map.str)](#21-companion-files-mapini--mapstr)
22. [View Options](#22-view-options)
23. [Edit Operations](#23-edit-operations)
24. [File Operations](#24-file-operations)
25. [New Map Wizard](#25-new-map-wizard)
26. [Out of Scope / Future Work](#26-out-of-scope--future-work)

---

## 1. Application Shell

- The editor is a **.NET MAUI** desktop application (`OpenSage.Tools.WorldBuilder.Maui`).
- The main window is a MAUI `Shell` with a desktop-style **menu bar** (`MenuBarItem` entries for File, Edit, View, Help), a horizontal **toolbar** strip below the menu bar, and a three-column layout:
  - **Left column** (fixed ~260 dp): Object Properties / tool-settings panel, implemented as a MAUI `ScrollView` containing context-sensitive `ContentView`s.
  - **Centre column** (fills remaining width): 3-D viewport (`VeldridViewport` custom control).
  - **Right column** (hidden by default, revealed by specific tools): optional secondary detail panel.
- An optional **status bar** (`HorizontalStackLayout`) is anchored to the bottom of the window.
- The application must load the game's asset pack (`.big` archives) on startup to resolve textures, objects, sounds, and INI data used for preview. If no installation is detected from environment variables, a **Settings page** (`ContentPage`) is pushed to let the user browse to the game folder.

---

## 2. Viewport

- Renders the map in 3-D using the existing OpenSAGE rendering pipeline via the `VeldridViewport` custom MAUI control (see *UI Framework and Architecture* above).
- Supports free-look camera navigation: orbit (right-drag), pan (middle-drag or Shift+drag), and zoom (scroll wheel).
- **Lock Angle** toggle: prevents camera rotation (top-down/overhead mode only).
- **Top-down view** toggle: forces the camera directly overhead.
- Viewport overlays are toggled via the View menu (see §22).
- The camera must be usable during area/polygon placement; it must be locked during water placement (matching original WorldBuilder behaviour).
- Minimum zoom is configurable; default is the current engine maximum doubled (matches the existing `MaxCameraDistance` change from this session).

---

## 3. Toolbar

The toolbar is a horizontal `HorizontalStackLayout` of MAUI `ImageButton` controls placed below the menu bar. Each button has an icon and a tooltip. Activating a button switches the active tool and updates the Object Properties panel to show that tool's options. The active tool button is visually highlighted (e.g. accent-coloured background or border).

| ID | Tool | Description |
|----|------|-------------|
| A | New Level | Create a new map (opens New Map Wizard, §25) |
| B | Open | Open an existing `.map` file |
| C | Save | Save current map |
| D | Cut | Copy all map data to clipboard and clear the map |
| E | Copy | Copy all map data to clipboard |
| F | Paste | Paste map data from clipboard |
| G | Ruler | Measure distances; toggle between linear and radial measurement |
| H | Arrow / Select | Default tool; left-click to select placed objects |
| I | Lock Angle | Toggle camera rotation lock |
| J | Height Brush | Set terrain cells to a specific absolute height |
| K | Mound | Raise terrain height (hold mouse to apply continuously) |
| L | Dig | Lower terrain height (hold mouse to apply continuously) |
| M | Smooth Height | Average neighbouring heights to smooth terrain |
| N | Mesh Mold | Conform terrain to a mesh shape |
| O | Water | Place a water area polygon |
| P | Paint Single Tile | Paint one terrain texture tile at a time |
| Q | Paint Large Tile | Paint terrain in a configurable brush radius |
| R | Eye Dropper | Sample an existing terrain texture into the active slot |
| S | Spill Paint | Flood-fill the entire map with the selected texture |
| T | Auto Edge Out | Blend two adjacent textures outward |
| U | Auto Edge In | Blend three adjacent textures together |
| V | Blend Single Tile | Fine-grain single-tile texture blending |
| W | Place Object | Place game objects (units, structures, props) |
| X | Road / Bridge | Draw roads and place bridges |
| Y | Shrubbery / Grove | Mass-place trees and vegetation |
| Z | Ramp | Level a slope into a traversable ramp |
| 1 | Smudge | Paint burn marks and ground decals |
| 2 | Fence | Draw a straight-line fence from a catalogue of fence objects |
| 3 | Build List | Remove an object from AI build lists (advanced; see §18) |
| 4 | Waypoint | Place and connect waypoints (paths) |
| 5 | Polygon / Area | Draw trigger/perimeter areas |
| 6 | Border | Draw additional map boundary polygons |
| 7 | Scripts | Open the Scripts editor (§17) |
| 8 | Teams | Open the Teams editor (§18) |
| 9 | About | Display version information |

---

## 4. Object Properties Panel

- The left column of the main layout; a MAUI `ScrollView` wrapping a `VerticalStackLayout` whose children change based on the active tool.
- Common controls rendered as MAUI form rows (`Label` + `Entry`/`Stepper`/`Slider`):
  - **Brush Width** (cells): integer stepper; controls the radius of terrain tools.
  - **Brush Feather Width** (cells): integer stepper; controls the soft falloff ring around the brush.
  - **Amount to Raise/Lower**: float entry; terrain tools' per-application delta. Maximum terrain height is 316.
- Tool-specific controls are described in each tool's section below.
- Object selection list (for Place Object tool): a `CollectionView` with hierarchical grouping by faction/category, filterable by a search `SearchBar` at the top of the panel.

---

## 5. Terrain Editing

### 5.1 Height Tools (Mound, Dig, Smooth, Height Brush, Mesh Mold)

- Apply height changes within a circular brush defined by **Brush Width** and **Brush Feather Width**.
- **Mound**: adds `Amount` to each cell per application; mouse can be held down for continuous application.
- **Dig**: subtracts `Amount` from each cell per application.
- **Smooth Height**: averages each cell with its neighbours within the brush radius.
- **Height Brush**: sets all cells within brush to an absolute target height.
- **Mesh Mold**: snaps terrain to a smooth mesh shape (used for plateau creation).
- Terrain height range: 0–316 units.
- Terrain height affects passability: any face with slope ≥ 90° is considered **impassable** by default.

### 5.2 Passability Override

- **Paint Large Tile** (Q) has a checkbox: **Paint Passable/Impassable Terrain**.
  - When checked, the texture layer is ignored; only the passability flag of the underlying cells is set.
  - Separate modes: force-passable and force-impassable.
- Passability can also be corrected with the Ramp tool (§11).

---

## 6. Water Editing

- Water is represented as one or more **water area** polygons, each with an independent height.
- **Default water area**: placed automatically at map creation at height 7. Below-ground terrain dug to less than height 16 appears submerged.
- The Water tool (O) adds corner handles (displayed as small squares) that the user drags to define the polygon shape.
- **Camera restriction**: while placing water handles the camera must not pan or zoom (matches original limitation).
- Water area properties (in Object Properties panel):
  - **Height**: float; the water surface elevation.
  - **River mode**: boolean; changes rendering to a flowing river rather than a still lake.
  - **Name**: string; required for dam scripting (e.g. `Upper Dam Water Area`, `Lower Dam Water Area`).
- Water height is set independently from terrain height (different coordinate spaces; see §21 for `map.ini` integration).
- Animated water height change at runtime is driven by scripts (§17 action: *Change water area altitude with damage over time*).

---

## 7. Texture Painting

- Terrain uses a tile-based texture system. Each cell has a primary texture and optional blended edge tiles.
- **Paint Single Tile (P)**: paints one cell at a time; useful for fine detail.
- **Paint Large Tile (Q)**: paints in a configurable brush; brush size set in Object Properties.
- **Eye Dropper (R)**: click an existing painted cell to load its texture as the active selection.
- **Spill Paint (S)**: flood-fills the entire map (or a contiguous region) with the selected texture.
- **Auto Edge Out (T)**: click-drag from the first texture to the second to insert a blended transition row. Before/after visually distinct.
- **Auto Edge In (U)**: blends a three-texture junction.
- **Blend Single Tile (V)**: per-tile manual blending, same behaviour as Auto Edge but applied one cell at a time.
- Texture catalogue is loaded from the game's terrain INI/texture assets; displayed hierarchically in Object Properties (e.g. `Field > 2%grassmediumtype3`, `Desert2 > 11%clifflargetype12`).
- Textures are selected via buttons that filter the list by terrain set (Field, Desert, Snow, etc.).

---

## 8. Object Placement

### 8.1 Place Object Tool (W)

- Object Properties panel shows a hierarchical browser: **faction** → **category** → **object name**.
  - Example factions: USA, GLA, China, Civilian, Neutral.
  - Example categories: Structure, Infantry, Vehicle, Prop.
- Clicking places the selected object at the cursor world position.
- Placed objects snap to terrain height.
- Each placed object has properties editable via right-click or a selection inspector:
  - **Name / Label**: unique string identifier (used in scripts).
  - **Owner / Player**: which player owns the object at mission start.
  - **Angle / Rotation**: facing direction in degrees.
  - **Initial health**: percentage (0–100).
  - **Garrison-able content**: for container objects.
  - **Build list participation**: whether the AI may rebuild this object.
- **Faction buildings** (e.g. USA Command Center placed in the editor) are **not** automatically transferred to the computer AI; they must be owned explicitly.
- Civilian supply structures:
  - `SupplyDock`: full supply depot.
  - `SupplyPile`: large resource pile.
  - `SupplyPileSmall`: ~3 000 credit pile.
  - `SupplyWarehouse`: slower-rate warehouse.
- Tech structures (oil derricks, artillery platforms, etc.) are placed as neutral objects contested at runtime.
- Artillery platforms placed on cliffs will **not** attack units below them (engine constraint to document).

### 8.2 Delete

- Pressing **Delete** removes the selected object(s), waypoint(s), water area(s), area polygon(s), or road segment(s).
- **Backspace** is not equivalent to Delete (to match original behaviour).

---

## 9. Roads and Bridges

- **Road Tool (X)**: click to place road nodes; the editor connects sequential nodes into a road segment.
- Road segments auto-connect when two endpoints are placed near each other.
- Curve modes (selectable in Object Properties): **Broad Curve** (default), **Tight Curve**, **Angled**.
- The road catalogue is loaded from game assets; roads can also represent decorative cracks or worn paths.
- **Bridges**: a special road type that spans water or elevated terrain; can be placed from the same tool by selecting a bridge template.
- Roads and bridges are stored in the `.map` format's road layer (already partially supported in `OpenSage.FileFormats.Map`).

---

## 10. Vegetation (Shrubbery / Grove)

- **Shrubbery Tool (Y)**: mass-places trees and vegetation objects in a configurable cluster pattern.
- Object Properties panel exposes:
  - **Object type**: tree/shrub template selector.
  - **Count**: number of objects to scatter per click.
  - **Radius**: scatter radius.
  - **Density** / **Spacing minimum**: prevent overlapping instances.
- Some tree types render as invisible in-game; the editor should provide a visual indicator or filter them.
- Individual trees can also be placed with the standard Place Object tool.

---

## 11. Ramps

- **Ramp Tool (Z)**: levels a sloped region into a passable, traversable ramp.
- User clicks and holds to define the ramp region; Object Properties exposes a **Width** field.
- After defining the region, the user presses **Place Ramp** to apply.
- **Safety constraint**: ramp width must not exceed 350 cells; exceeding this limit crashes the original WorldBuilder (should be validated and rejected with an error message in OpenSAGE).
- Ramps can be applied iteratively (smaller widths) until the area is level enough to traverse.

---

## 12. Fences

- **Fence Tool (2)**: draws a straight line of fence objects between two clicked points.
- The fence template (chain-link, concrete wall, etc.) is selected from the Object Properties panel.
- Object count is automatically calculated from line length and object dimensions.

---

## 13. Smudges

- **Smudge Tool (1)**: paints decal overlays (burn marks, craters, stains) onto terrain.
- Smudge type selected from a catalogue in Object Properties.
- Applied like a brush; brush size configurable.

---

## 14. Waypoints and Paths

- **Waypoint Tool (4)**: click to place a waypoint node.
- **Path (directed edge)**: click and drag from an existing waypoint to another to create a directional path arrow.
- Waypoints and paths are stored by name; the name field is editable in Object Properties.
- **Reserved waypoint names** with special engine meaning:

  | Name Pattern | Meaning |
  |---|---|
  | `Player_N_Start` | Player N's spawn point (N = 1–8) |
  | `Center1` … `Center8` | Main attack path into base N |
  | `Flank1` … `Flank8` | Secondary attack path into base N |
  | `BackDoor1` … `BackDoor8` | Sneak-attack path into base N |
  | `Railroad` | Train path prefix; assigns path to RailroadBehavior |
  | `[Name]Station` | Train stop at this waypoint |
  | `[Name]Tunnel` | Train holds altitude from this point until a waypoint without `Tunnel` suffix |

- Paths for trains: the last waypoint must connect back to the first (forming a loop).
- Paths for AI attack routes: multiple labels can be set per path; each label represents a different attack corridor.
- Waypoints have three **Path Label** slots in Object Properties (for Center/Flank/BackDoor labelling).

---

## 15. Polygon / Trigger Areas

- **Polygon Tool (5)**: click each corner of a polygon in sequence; click the first point again to close the loop.
- The camera can be moved freely during placement (unlike water areas).
- Area name is set in Object Properties and selected from a drop-down or typed manually.
- **Reserved area names** with special AI/engine meaning:

  | Name Pattern | Meaning |
  |---|---|
  | `InnerPerimeter_N_` | Core base area for player N; AI builds structures inside here |
  | `OuterPerimeter_N_` | Outer defensive perimeter for player N; AI places defenses and patrols here |
  | `CombatZone` | Area the AI uses to stage forces and capture tech buildings |
  | `Skirmish World` | Bounding area for the entire playable map (required for skirmish AI) |

- Polygon areas are also used for:
  - Script conditions (`Unit X is inside area Y`).
  - Marking intended mountain regions during layout planning.
  - Water area polygon handles (§6).
- Custom-named areas can be referenced in scripts (§17).

---

## 16. Map Border

- **Border Tool (6)**: draws an additional boundary polygon.
- Multiple named borders can be defined; they are activated at runtime via a script action.
- The default boundary (the orange rectangle visible at map edges) is set at map creation time and cannot be removed, only scaled.
- Content placed outside the active boundary is invisible in-game.

---

## 17. Scripting System

### 17.1 Overview

The scripting system controls all dynamic map behaviour beyond terrain, lighting, and static object placement. Scripts are attached to **player folders** (one per player plus a shared civilian/neutral folder).

### 17.2 Script Properties

Each script has:

- **Name**: string identifier.
- **Active**: boolean; inactive scripts are never evaluated.
- **Subroutine**: boolean; if true, the script is only run when called by another script, not evaluated automatically each tick.
- **Deactivate Upon Success**: boolean; if true, the script is disabled the first time its conditions evaluate to true.
- **Active In** difficulty flags: Hard, Medium, Easy.
- **Script Conditions** tab: one or more conditions joined by AND; the whole block is true when all conditions pass.
  - A single built-in "True" condition makes the script unconditionally fire each frame.
- **Actions If True** tab: ordered list of actions executed when conditions are met.
- **Actions If False** tab (optional): actions executed when conditions are false.

### 17.3 Condition Types (non-exhaustive)

| Category | Example Conditions |
|---|---|
| Scripting | Timer `<name>` has expired |
| Scripting | Flag `<name>` is true/false |
| Scripting | Counter `<name>` is `<op>` `<value>` |
| Unit | Unit `<name>` is destroyed |
| Unit | Unit `<name>` has `<%>` health |
| Unit | Unit `<name>` is inside area `<area>` |
| Player | Player `<name>` starting position is `<N>` |
| Player | Player `<this>` / `<local>` predicates |

### 17.4 Action Types (non-exhaustive)

| Category | Example Actions |
|---|---|
| Scripting → Timer | Seconds countdown timer `<name>` — set to `<N>` |
| Scripting → Timer | Set timer `<name>` to expire between `<min>` and `<max>` seconds/frames |
| Scripting → Timer | Stop timer `<name>` |
| Scripting → Counter | Set counter `<name>` to `<N>` |
| Scripting → Counter | Add/subtract `<N>` from counter `<name>` |
| Scripting → Flag | Set flag `<name>` to true/false |
| Scripting → Script | Enable/disable script `<name>` |
| Scripting → Briefing | Show military briefing caption `<string>` for `<ms>` |
| Unit | Kill unit `<name>` |
| Unit | Remove unit `<name>` from the world |
| Unit | Transfer unit `<name>` to player `<name>` |
| Map | Change water area altitude with damage over time |
| Map | Override `SnowEnabled` (weather toggle) |
| Sound | Play sound `<name>` at `<%>` volume |
| Player | Set player `<name>` unable/able to build units/buildings |
| Team | Set team `<name>` to Hunt |
| Team | Spawn team |
| Visual | Fade screen (0–1) from `<start>` to `<end>` toward white/black over `<N>` frames |

### 17.5 Script Folders

- Scripts are organised in a two-level tree: **player root** → **script folders**.
- A folder has a **Group is Active** flag; disabling a folder disables all scripts inside it.
- Scripts and folders can be drag-reordered.
- Folders can be imported/exported (EA's WorldBuilder supports a `.scb` or equivalent import format).

### 17.6 Script Evaluation Order

- Scripts are evaluated each game logic tick in the order they appear in the list.
- Scripts marked Subroutine are skipped during automatic evaluation.

---

## 18. Teams Editor

- Accessed via **Edit → Edit Teams** or the Teams toolbar button; opens as a MAUI modal `ContentPage`.
- Each player can have one or more teams.
- **Team properties**:
  - **Name**: string.
  - **Owner**: player.
  - **Units**: list of `(ObjectTemplate, count)` pairs; these are the units the AI will attempt to build/maintain.
  - **Production Condition script**: a subroutine script that controls whether the team rebuilds.
  - **Behavior Script Triggers**: subroutine scripts for AI attack/idle behaviour (e.g. Hunt, Hunt Idle).
  - **Initial Team Behavior**: the default behavior before scripts trigger.
  - **Reinforce**: boolean; whether to replenish dead team members.
- The **Build List** tool (3) can remove an individual placed object from all AI build lists.
- **Add Skirmish Teams** (in Player List): auto-generates standard skirmish-ready teams for all players; used when creating a multiplayer map.

---

## 19. Player List / Skirmish Setup

- Accessed via **Edit → Edit Player List**; opens as a MAUI modal `ContentPage`.
- Buttons:
  - **Add Skirmish Players**: creates standard player entries for all 8 skirmish slots, each with default faction, colour, and team data. Required before placing player-owned objects.
  - **Add Skirmish Teams**: generates default AI team configurations for all skirmish players. Must be done after placing player bases.
- Player properties:
  - Name, faction, start colour, team affiliation, human/AI flag.
- Player count determines how many `Player_N_Start` waypoints and `InnerPerimeter_N_` / `OuterPerimeter_N_` areas are expected.

---

## 20. Map Settings

Accessed via **Edit → Edit Map Settings**; opens as a MAUI modal `ContentPage` with `Picker` and `Entry` fields.

- **Time of Day**: Morning, Afternoon, Evening, Night (affects ambient light angle and colour).
- **Weather**: Snow toggle (uses INI `Weather` block in `map.ini`; see §21).
- **Map Name / Description**: display strings.
- **Initial Camera Position**: world position of the camera at game start.
- **Ambient light colour and intensity**.
- **Fog of War settings**.

---

## 21. Companion Files (map.ini / Map.str)

### 21.1 map.ini

A per-map INI override file located alongside the `.map` file. Supports:

- **Weather block**: snow/rain particle system settings (see §17.4 for runtime toggle via script).

  ```ini
  Weather
    SnowEnabled     = yes
    SnowTexture     = ExSnowFlake.tga   ; or EXRainDrop.tga for rain
    SnowBoxDimensions   = 100
    SnowBoxDensity      = 1
    SnowFrequencyScaleX = 0.0533
    SnowFrequencyScaleY = 0.0275
    SnowAmplitude       = 4.0
    SnowVelocity        = 3.0
    SnowPointSize       = .16
    SnowMaxPointSize    = 10.0
    SnowMinPointSize    = 0.0
    SnowPointSprites    = yes
    SnowQuadSize        = 0.5
  End
  ```

- **Object overrides**: `Object <TemplateName>` blocks with `ReplaceModule` / `AddModule` entries. Used for train garrisoning and custom vehicle configurations (see train tutorial).
- For **single-player** missions the file must be named `solo.ini` instead.
- The editor should expose a **map.ini editor pane** — a MAUI `Editor` control with monospace font for raw text — and a structured **Weather** settings panel with `CheckBox` / `Entry` fields for each parameter.

### 21.2 Map.str

A string table file mapping caption identifiers to localised strings.

```
Caption1
"Welcome to the map!"
End
```

- Used by the *Show military briefing caption* script action (§17.4).
- In multiplayer, `Map.str` is only loaded from the host's installation; captions will show as `MISSING: 'X'` for other players unless the caption name is its own text.
- Maximum 92 characters per caption line; maximum 4 lines per caption (engine crash if exceeded).
- Line break within a caption: `\n`.
- The editor should provide a **String Table editor** that lists all caption IDs referenced by the map's scripts and allows editing their text values.

---

## 22. View Options

Toggleable via the **View** menu:

| Toggle | Default | Effect |
|---|---|---|
| Show Terrain | On | Render terrain mesh and textures |
| Show Boundaries | On | Render the orange map boundary rectangle |
| Show Waypoints | On | Render waypoint nodes and path arrows as overlays |
| Show Objects | On | Render placed game objects |
| Show Impassable Areas | Off | Render impassable cells as a red overlay (Ctrl+I) |
| Show from Top-Down | Off | Lock camera to orthographic overhead view |
| Show Weapon Ranges | Off | Render weapon range indicators around selected objects |
| Toolbar | On | Show/hide the toolbar strip |
| Status Bar | On | Show/hide the bottom status bar |

---

## 23. Edit Operations

| Action | Shortcut |
|---|---|
| Undo | Ctrl+Z |
| Redo | Shift+Ctrl+Z |
| Cut | Ctrl+X |
| Copy | Ctrl+C |
| Paste | Ctrl+V |
| Delete selected | Delete |
| Toggle impassable overlay | Ctrl+I |
| Select all | Ctrl+A |

- Undo/redo stack must cover all terrain, object, waypoint, area, and script changes.
- Copy/Paste operates on the entire map (matching original WorldBuilder clipboard behaviour).

---

## 24. File Operations

- **New** (Ctrl+N): opens New Map Wizard (§25).
- **Open** (Ctrl+O): opens a `.map` file; prompts to save current unsaved changes.
- **Save** (Ctrl+S): saves current map; prompts for filename if new.
- **Save As**: saves with a new filename.
- The editor uses `OpenSage.FileFormats.Map` for reading and writing `.map` files.
- Maps are stored in `<UserDocuments>/Command and Conquer Generals Data/Maps/<MapName>/` for compatibility with the original game.
- On save, the editor should also write `map.ini` and `Map.str` if those panels contain data.

---

## 25. New Map Wizard

Presented when creating a new map:

- **Map dimensions** (width × height in cells; must be a multiple of the engine's terrain tile size).
- **Initial ground height**: default elevation for all cells at creation (e.g. 0).
- **Boundary inset**: the playable area starts this many cells inside the outer edge.
- On confirmation, the map is initialised with the default water area and flat terrain; the camera is positioned at the map centre.

---

## 26. Out of Scope / Future Work

The following features are referenced in the original WorldBuilder but are deferred:

- **Multiplayer lobby integration**: submitting / downloading maps from a community portal.
- **`.scb` script import/export**: the binary script bundle format used by EA's WorldBuilder.
- **Mission briefing audio**: linking `.mp3` / `.vp6` audio files to briefing captions.
- **Cinematic / cutscene scripting**: camera path animations driven by scripts.
- **Pre-placed faction units with AI rebuild**: the original game does not auto-transfer pre-placed faction buildings to AI; faithful reproduction of this limitation is required but a workaround UI hint is desirable.
- **Cliff auto-generation**: automated cliff brush that applies the multi-step workflow described in the "Professional Cliffs" tutorial.
- **Map preview image generation**: thumbnail rendering for map selection screen.

---

*Sources: [CNC Labs WorldBuilder Tutorials](https://www.cnclabs.com/maps/generals/worldbuilder/), EA Official WorldBuilder Manual (C&C Generals).*
