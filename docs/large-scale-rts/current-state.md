# Current-State Summary

> Part of the [Large-Scale RTS Roadmap](README.md). A snapshot of the architecture the roadmap
> builds on (see cited files; update as the code evolves — this is the basis for every phase's
> assumptions).

## Simulation core

- **Game loop:** `src/OpenSage.Game/Game.cs` — `Update()` runs input/local tick, then a
  fixed-rate `LogicTick()` (`GameLogic.Update()`, network buffer, `Scene3D.LogicTick`,
  `PartitionCellManager.Update()`), then `Scripting.ScriptingTick()`. `Render()` is a
  separate pass (`Graphics.Draw`). Logic and render are already decoupled in timing.
- **Entity model:** `GameObject` (`src/OpenSage.Game/Logic/Object/GameObject.cs`) is an
  **object + module composition** model (not an ECS). Behavior lives in `BehaviorModule` /
  `UpdateModule` instances; per-module update scheduling via `NextCallFrame` / `UpdateOrder`.
- **Object storage:** `GameLogic` (`src/OpenSage.Game/Logic/GameLogic.cs`) holds a
  `List<GameObject>` indexed by `ObjectId`, iterated through a copied list (known allocation
  TODO). **Single-threaded**, with a sleepy-update queue.
- **Spatial / pathing:** generic `Quadtree` (`src/OpenSage.Game/DataStructures/`),
  `PartitionCellManager` for coarse cell partitioning + collision pair generation, and A*
  pathfinding in `src/OpenSage.Game/Navigation/Navigation.cs`.
- **Determinism / networking:** frame-ordered `NetworkMessageBuffer` and `ReplayConnection`
  implement lockstep-style order processing. A random-source abstraction exists
  (`IGame.CreateRandom()`) but there are TODOs around full determinism guarantees.
- **Rendering:** `RenderScene` (`src/OpenSage.Rendering/RenderScene.cs`) buckets
  `RenderObject`s; each `GameObject` has a `Drawable` with `DrawModule[]`. **No GPU
  instancing** today; rendering is per-object/per-draw-module.
- **Known scale bottlenecks (TODOs in code):** per-tick allocations in object iteration and
  collision-pair generation; single-threaded logic/collision/pathing; un-cached hot queries
  (e.g. terrain height); render bucket/shadow scalability.

## Model & animation pipeline

- **W3D parsing:** `src/OpenSage.FileFormats.W3d/` — `W3dFile` parses meshes, hierarchy
  (skeleton pivots), animations (incl. compressed), materials, emitters, HLOD, etc.
- **W3D → runtime:** `OnDemandModelLoader` (`src/OpenSage.Game/Content/Loaders/`) builds a
  `Model` (`Graphics/Model.cs`) with `ModelBoneHierarchy` + `ModelSubObject[]`; mesh
  conversion in `Graphics/ModelMesh.W3d.cs`.
- **Animation runtime:** `ModelInstance` + `ModelBoneInstance` advance animations and compute
  bone/skinning transforms; `Graphics/Animation/W3DAnimation.cs` converts W3D channels into
  runtime `AnimationClip`s.
- **Draw/INI wiring:** `W3dModelDraw` + `ModelConditionState`
  (`src/OpenSage.Game/Logic/Object/Draw/`) map INI model-condition / animation states to W3D
  models and animations.
- **Asset loading extension point:** `AssetStore` + `OnDemandAssetLoadStrategy` +
  `IOnDemandAssetLoader` are where a new model/animation format would plug in.
- **External formats:** **none today** — no glTF/FBX/OBJ/COLLADA/Assimp references in `src/`.

## Map & terrain (relevant to larger maps)

- **Heightmap grid:** `HeightMapData` (`src/OpenSage.Game/Data/Map/HeightMapData.cs`) holds
  `Width`/`Height` and a full `ushort[,] Elevations` array (Generals = `uint8`, BFME+ =
  `uint16`).
- **Terrain patches:** `Terrain` (`src/OpenSage.Game/Terrain/Terrain.cs`, `PatchSize = 17`)
  builds all `TerrainPatch`es up front; no streaming/LOD.
- **Authoring:** WorldBuilder (`src/OpenSage.Tools.WorldBuilder.*`) assumes the full grid is
  resident; height tools clone the whole elevation array per stroke.

See the [larger-maps deep dive](deep-dives/larger-maps.md) for how this constrains map size.
