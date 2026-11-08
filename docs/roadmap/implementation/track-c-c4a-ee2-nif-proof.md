# Track C · C4a: Empire Earth II NIF 10.1 proof

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4a row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/ee2-gamebryo-model-storage.md](../deep-dives/ee2-gamebryo-model-storage.md), including
its [proof scope](../deep-dives/ee2-gamebryo-model-storage.md#9-c4a-proof-of-concept-scope) and
[architecture diagrams](../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams).

## Scope

Load the static War Galley and Twin-Engine Bomber NIF 10.1 models from a local EE2
`graphics.zip`, map their supported geometry and fixed-function material subset into the C1
interchange graph, materialize runtime models, and capture recognizable output. This is an
experimental proof, not an EE2 game definition or a production NIF importer.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4a-01`](https://projects.reid-corp.com/work_packages/479) | Exact target descriptors, coordinate convention, and LOD contract. |
| 2 | [`WI-C4a-02`](https://projects.reid-corp.com/work_packages/480) | NIF 10.1 container reader and block directory. |
| 3 | [`WI-C4a-03`](https://projects.reid-corp.com/work_packages/481) | Node hierarchy and local transforms. |
| 4 | [`WI-C4a-04`](https://projects.reid-corp.com/work_packages/482) | Triangle meshes and bounds. |
| 5 | [`WI-C4a-05`](https://projects.reid-corp.com/work_packages/483) | Fixed-function properties and source texture names. |
| 6 | [`WI-C4a-06`](https://projects.reid-corp.com/work_packages/484) | Deterministic highest-detail LOD selection. |
| 7 | [`WI-C4a-07`](https://projects.reid-corp.com/work_packages/485) | C1 `ImportedModel` adapter. |
| 8 | [`WI-C4a-08`](https://projects.reid-corp.com/work_packages/486) | Case-insensitive ZIP-backed virtual file system. |
| 9 | [`WI-C4a-09`](https://projects.reid-corp.com/work_packages/487) | Installed-asset descriptor checks for both targets. |
| 10 | [`WI-C4a-10`](https://projects.reid-corp.com/work_packages/488) | Shared C3/C4a headless runtime-model capture path. |
| 11 | [`WI-C4a-11`](https://projects.reid-corp.com/work_packages/489) | Installed-asset materialization and visual captures. |

Cards 3 and 8 can proceed independently after their prerequisites. Cards 4, 5, and 6 can proceed
independently after card 3. Card 11 is the completion point.

---

## Agent context

- Interchange root: `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs`.
- Static hierarchy: the parser retains the NIF node graph, then the importer bakes accumulated
  transforms into each mesh and attaches all sub-objects to one identity `ImportedBone`.
- Geometry: `ImportedMesh` uses `ushort` indices and at least one material and mesh part.
- Material properties: `ImportedMaterial` has fixed-function state plus typed named properties;
  texture references use `ImportedMaterialProperty.FromTextureName`.
- Source access: `AssetImportContext` supplies `Panopticon.IO.FileSystem`; C4a adds a
  `ZipFileSystem` implementation rather than opening ZIP paths inside the importer.
- Parser boundary: binary NIF types live in `Panopticon.FileFormats.Nif`; no type from that project
  reaches runtime graphics classes.
- Integration boundary: the proof invokes `NifModelImporter` and
  `ImportedModelMaterializer` directly. It does not add an `IGameDefinition`, global format
  registry, or NIF branch to shipped SAGE game definitions.
- Scheduling boundary: parser and adapter cards may prepare in parallel, but the installed visual
  proof waits for `WI-C1-14` so C4a completes only after the C1 seam is stable.
- Installed data: `EMPIRE_EARTH_II_PATH/zips/graphics.zip`. Tests requiring it are interactive
  and never copy extracted assets into the repository.
