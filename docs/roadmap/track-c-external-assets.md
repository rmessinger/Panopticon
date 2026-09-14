# Track C: External Models & Animations

> Part of the [Large-Scale RTS Roadmap](README.md). This track adds unit models and animations
> authored outside the Westwood `.w3d` ecosystem. The design begins with a format-agnostic import
> abstraction before any concrete format is implemented.
>
> Architecture context: see [`agent/current-state.md`](agent/current-state.md).
>
> Audience: human overview of the external-assets track.

> The Milestone column states the demonstrable outcome that marks each phase done. Granular steps live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| C1. Format-agnostic import abstraction | Define engine-internal interchange types for meshes, skeletons, animations, and materials, plus an `IModelImporter` / `IAnimationImporter` abstraction behind the existing `OnDemandAssetLoadStrategy`. Refactor the W3D path to be one implementation of this abstraction (proves the seam). No new external formats yet. | The W3D path loads entirely as one implementation of the import abstraction with no behavior change. | In progress | — |
| C2. Skeleton & animation retargeting | Map imported skeletons/animations onto the engine's `ModelBoneHierarchy` / animation-clip model, including bone-name remapping and the INI model-condition/animation-state wiring so external models can be driven by gameplay states. | An externally-authored skeleton/animation drives an engine model through gameplay animation states via bone remapping. | Not started | C1 |
| C3. Material & texture mapping | Translate external PBR/material definitions and textures into the engine's material/shader model. | External materials/textures render correctly through the engine material/shader model. | Not started | C1 |
| C4. First concrete external format | Implement a concrete importer against the C1 abstraction, with glTF 2.0 as the candidate because it is an open standard with strong animation/tooling support. Validate it end-to-end on a unit. Committed scope per the [Decision](open-questions.md): static meshes plus skinned skeletal animation only. That proves mesh, skeleton, and gameplay-driven animation through C1/C2/C3. Morph targets and advanced PBR material extensions stay out of scope here; see C6. | A glTF 2.0 unit (static mesh + skinned skeletal animation) imports and plays end-to-end. | Not started | C1, C2, C3 |
| C5. Authoring & validation tooling | Extend ReplaySketch, the primary tool, to import, preview, and validate external models. Do not add a dedicated viewer; see the [C5 tooling Decision](open-questions.md). Surface skeleton/animation/material mapping issues such as missing bones, unmapped materials, and bad scale/orientation. Licensing/provenance is out of scope per the [Decision](open-questions.md) because this is a single-operator tool; revisit only if it is ever distributed. | The tool previews an external model and flags missing bones, unmapped materials, and bad scale/orientation. | Not started | C4 |
| C6. Extended model features (deferred) | Add the features intentionally cut from C4 only if an experiment needs them: glTF morph targets (blendshapes) and advanced PBR/material extensions (e.g. `KHR_materials_*`). Deferred by default. | On demand, glTF morph targets and advanced PBR extensions import for an experiment that needs them. | Not started | C4 |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first source-edit task card is marked `Ready`. C2-C6 deep
dives are still unwritten.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| C1 | [`c1-import-abstraction.md`](deep-dives/c1-import-abstraction.md) | [Architecture diagrams](deep-dives/c1-import-abstraction.md#9-architecture-diagrams) | [`WI-C1-01..14`](agent/cards/) |
| C2 | — | — | — |
| C3 | — | — | — |
| C4 | — | — | — |
| C5 | — | — | — |
| C6 | — | — | — |

## Cross-track dependencies

- C1 and A3 both touch the presentation seam, so the import abstraction should feed the same runtime `Model`/`ModelInstance` types that [B5](track-b-scale.md) expects.
- B5 should account for Track C models so instancing is not W3D-specific.
