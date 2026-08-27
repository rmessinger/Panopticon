# WorldBuilder Terrain Diagrams

## 1. Data Structures

```mermaid
classDiagram
    class MapFile {
        +HeightMapData HeightMapData
        +BlendTileData BlendTileData
        +SidesList SidesList
        +GlobalLighting GlobalLighting
        +CreateEmpty(playableW, playableH, borderW, initialHeight) MapFile
    }

    class HeightMapData {
        +uint Width
        +uint Height
        +uint BorderWidth
        +uint Area
        +ushort[,] Elevations
        +float VerticalScale
        +float MinZ
        +float MaxZ
        +HeightMapBorder[] Borders
        --
        VerticalScale = 0.625  (8-bit, v<5)
        VerticalScale = 0.0390625  (16-bit, v≥5)
        Area == Width × Height
    }

    class BlendTileData {
        +int NumTiles
        +int[,] Tiles
        +BlendDescription[,] Blends
        +BlendDescription[,] ThreeWayBlends
        +int[,] CliffTextures
        +BlendTileTexture[] Textures
        +BlendDescription[] BlendDescriptions
        +CliffTextureMapping[] CliffTextureMappings
        +BlendTileTextureIndex[] TextureIndices
        --
        NumTiles == Width × Height
    }

    class BlendTileTexture {
        +int CellStart
        +int CellCount
        +int CellSize
        +string Name
    }

    class BlendDescription {
        +int SecondaryTextureTile
        +BlendDirection BlendDirection
        +bool TwoSided
    }

    class CliffTextureMapping {
        +int TextureTile
        +Vector2[4] UVCorners
    }

    class BlendTileTextureIndex {
        +int TextureIndex
        +int Offset
    }

    MapFile *-- HeightMapData
    MapFile *-- BlendTileData
    BlendTileData *-- "many" BlendTileTexture
    BlendTileData *-- "many" BlendDescription
    BlendTileData *-- "many" CliffTextureMapping
    BlendTileData *-- "many" BlendTileTextureIndex
```

---

## 2. New Map Creation Pipeline

```mermaid
flowchart TD
    A([User enters\nwidth / height / borderWidth\nin NewMapWizardPage]) --> B

    B["NewMapFactory.Create(NewMapParameters)"]
    B --> C

    C["MapFile.CreateEmpty(\n  playableWidth,\n  playableHeight,\n  borderWidth,\n  initialHeight\n)"]

    C --> D["Compute total dimensions\n──────────────────────\ntotalW = playableW + borderW×2 + 1\ntotalH = playableH + borderW×2 + 1"]

    D --> E["Allocate ushort[totalW, totalH]\nFill every cell with initialHeight"]

    E --> F["HeightMapData.Create(\n  borderWidth,\n  elevations\n)"]

    F --> G["MapFile with\nHeightMapData\nBlendTileData (empty)\nSidesList, Lighting…"]

    G --> H["EditorMap.CreateNew(mapFile)"]

    H --> I([EditorMap ready\nfor display in WorldBuilder])

    style D fill:#f0f4ff,stroke:#6080c0
    style E fill:#f0f4ff,stroke:#6080c0
```

---

## 3. Coordinate System

```mermaid
block-beta
    columns 1

    block:legend["Coordinate Spaces"]:1
        space:1
    end

    block:spaces:1
        A["Tile space  (integer)\n──────────────────────────────────\nOrigin: top-left of full heightmap array\nRange: 0 … Width-1, 0 … Height-1\nIncludes border tiles on all edges"]
        B["World space  (float)\n──────────────────────────────────\nOrigin: inner corner of border region\nX = (tile_x − BorderWidth) × 10\nY = (tile_y − BorderWidth) × 10\nZ = elevation × VerticalScale"]
    end
```

```mermaid
flowchart LR
    A["World position\n(x, y)"] -->|"÷ HorizontalScale(10)\n+ BorderWidth"| B["Tile index\n(tx, ty)"]
    B -->|"− BorderWidth\n× HorizontalScale(10)"| A
    B -->|"× VerticalScale"| C["World height Z"]
```

---

## 4. HeightMap (Runtime) Construction

```mermaid
flowchart TD
    A["TerrainLogic.SetHeightMapData(\n  HeightMapData\n)"] --> B

    B["new HeightMap(heightMapData)"]

    B --> C["Derive dimensions\n──────────────────────\nWidth  = heightMapData.Width  − 1\nHeight = heightMapData.Height − 1\n(last row/col are not rendered)"]

    C --> D["Compute playable extent\n──────────────────────\nMaxX = (Width  − 2×BorderWidth) × 10\nMaxY = (Height − 2×BorderWidth) × 10"]

    D --> E["Pre-compute Normals[,]\nfor every (x, y) vertex\nby averaging adjacent quad normals"]

    E --> F["HeightMap ready\n──────────────────────\nGetHeight(x,y)  → elevation × VerticalScale\nGetHeight(fx,fy) → bilinear interpolation\nGetPosition(x,y) → world-space Vector3\nGetNormal(fx,fy) → interpolated normal"]
```

---

## 5. Terrain Mesh / Rendering Pipeline

```mermaid
flowchart TD
    A["HeightMap + MapFile\n(BlendTileData)"] --> B

    B["new Terrain(\n  mapFile,\n  heightMap,\n  …\n)"]

    B --> C1["CreateTileDataTexture()\n──────────────────────\nFor each tile (x,y):\n  ch0 = base texture index\n  ch1 = blend texture index\n  ch2 = three-way blend index\n  ch3 = cliff texture index\nPacked into RGBA GPU texture"]

    B --> C2["CreateCliffDetails()\n──────────────────────\nCliffTextureMapping[] →\nstructured GPU buffer\nwith per-cliff UV corners"]

    B --> C3["CreateTextures()\n──────────────────────\nBlendTileData.Textures[]\n→ GPU texture array\n(terrain albedo tiles)"]

    C1 & C2 & C3 --> D["OnHeightMapChanged()\n→ CreatePatches()"]

    D --> E["Divide map into patches\n──────────────────────\nPatch covers (PatchSize−1)² tiles\nPatchSize = 17  →  16×16 tiles/patch\nnumPatchesX = (Width−1) / 16\nnumPatchesY = (Height−1) / 16"]

    E --> F["For each patch:\nnew TerrainPatch(x0,y0…x1,y1)"]

    F --> G["TerrainPatch builds vertex buffer\n──────────────────────\nFor each vertex (x, y) in patch:\n  pos = HeightMap.GetPosition(x, y)\n  normal = HeightMap.Normals[x, y]\n  uv = tile UV"]

    G --> H["TerrainPatch builds index buffer\n──────────────────────\n2 triangles per quad\nShared index buffer cache\nreused across same-size patches"]

    H --> I["Patches added to\n'Terrain' render bucket\nShader samples tile-data texture\n+ texture array per draw call"]
```

---

## 6. Terrain Edit → Reload Loop (WorldBuilder)

```mermaid
sequenceDiagram
    participant User
    participant Tool as Editor Tool<br/>(HeightBrushTool /<br/>PaintSingleTileTool)
    participant EditorMap
    participant HeightMapData
    participant BlendTileData
    participant GameViewportRenderer
    participant TerrainLogic
    participant Terrain

    User->>Tool: Mouse drag / click

    alt Height edit
        Tool->>HeightMapData: Modify Elevations[x,y]
        Tool->>EditorMap: Push HeightMapEdit to undo stack
        Tool->>GameViewportRenderer: TerrainDirty = true
        GameViewportRenderer->>TerrainLogic: SetHeightMapData(HeightMapData)
        TerrainLogic->>TerrainLogic: new HeightMap(HeightMapData)
        TerrainLogic->>Terrain: OnHeightMapChanged(dirtyBounds)
        Terrain->>Terrain: Rebuild affected patches only
    else Texture paint
        Tool->>BlendTileData: Modify Tiles[x,y]
        Tool->>EditorMap: Push BlendTileEdit to undo stack
        GameViewportRenderer->>Terrain: Recreate tile-data GPU texture
    end

    Terrain-->>User: Updated render
```
