# Track C — External Models & Animations

> Part of the [Large-Scale RTS Roadmap](README.md). Incorporating unit models and animations
> authored outside the Westwood `.w3d` ecosystem. The chosen direction is **format-agnostic
> first** — design the import abstraction before committing to concrete formats. See
> [`current-state.md`](current-state.md) for the existing W3D pipeline these phases generalize.

| Phase | Goal | Status | Depends on |
|---|---|---|---|
| **C1. Format-agnostic import abstraction** | Define engine-internal interchange types for meshes, skeletons, animations, and materials, plus an `IModelImporter` / `IAnimationImporter` abstraction behind the existing `OnDemandAssetLoadStrategy`. Refactor the W3D path to be *one implementation* of this abstraction (proves the seam). No new external formats yet. | Not started | — |
| **C2. Skeleton & animation retargeting** | Map imported skeletons/animations onto the engine's `ModelBoneHierarchy` / animation-clip model, including bone-name remapping and the INI model-condition/animation-state wiring so external models can be driven by gameplay states. | Not started | C1 |
| **C3. Material & texture mapping** | Translate external PBR/material definitions and textures into the engine's material/shader model. | Not started | C1 |
| **C4. First concrete external format** | Implement a concrete importer (candidate: glTF 2.0 — open standard, strong animation/tooling support) against the C1 abstraction, validated end-to-end on a unit. | Not started | C1, C2, C3 |
| **C5. Authoring & validation tooling** | Tooling to import/preview/validate external models (extend an existing tool such as ReplaySketch/WorldBuilder, or a dedicated viewer), surfacing skeleton/animation/material mapping issues. | Not started | C4 |

## Cross-track dependencies

- **C1** and **A3** both touch the presentation seam — coordinate so the import abstraction
  feeds the same runtime `Model`/`ModelInstance` types that the rendering refactor
  ([B5](track-b-scale.md)) expects.
- **B5 (instanced rendering)** should account for Track C models so instancing is not
  W3D-specific.
