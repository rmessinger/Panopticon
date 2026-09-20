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
| C1. Format-agnostic import abstraction | Define engine-internal interchange types for meshes, skeletons, animations, and materials, plus an `IModelImporter` / `IAnimationImporter` abstraction behind the existing `OnDemandAssetLoadStrategy`. Refactor the W3D path to be one implementation of this abstraction (proves the seam). No new external formats yet. | The W3D path loads entirely as one implementation of the import abstraction with no behavior change. | Done | — |
| C2. Skeleton & animation retargeting | Bind imported animation channels to `ModelBoneHierarchy` through direct-index or validated exact-name targets, then reuse existing gameplay animation-state playback. This is channel rebinding, not humanoid bind-pose compensation; see the [deep dive](deep-dives/c2-skeleton-animation-retargeting.md). | An externally-authored named animation drives the intended bones through a gameplay animation state even when source and target bone order differs, while W3D direct-index skip and playback behavior remains unchanged. | Done | C1 |
| C3. Material & texture mapping | Add a format-neutral core metallic-roughness payload, external and embedded image sources, sampler/color-space semantics, deterministic tangent generation, and an engine-owned PBR materializer/shader; see the [deep dive](deep-dives/c3-material-texture-mapping.md). | Synthetic static and skinned meshes render the complete core PBR feature matrix with validated caching and deterministic captures. | Done | C1 |
| C4. First production external format | Implement strict `.gltf` and `.glb` import through SharpGLTF.Core, isolated behind C1 engine-owned values; see the [deep dive](deep-dives/c4-gltf-importer.md). The bounded subset covers static meshes, one skin, named skeletal animation, core PBR materials, and deterministic coordinate conversion. Morph targets and advanced PBR extensions remain C6. | Repository-owned static and skinned glTF fixtures import through C1-C3, render with correct coordinates and materials, and play named animation end-to-end. | Done | C1, C2, C3 |
| C4a. Empire Earth II NIF 10.1 proof of concept | Add a deliberately bounded, non-production Gamebryo importer through the C1 seam. Load the static `nwg1_galley.nifcache` and `ab11_TwinEngineBomber.nifcache` models from an operator-supplied EE2 install, including the Bomber's highest-detail LOD. Preserve fixed-function material values and texture names, but render untextured diffuse materials until cooked textures are supported. | Both target models import from `graphics.zip` into `ImportedModel`, materialize as runtime `Model` assets, and produce recognizable captured geometry without committing game assets. | Not started | C1 |
| C5. Authoring & validation tooling | Add a dedicated ReplaySketch Model tab backed by Core session state and structured diagnostics; see the [deep dive](deep-dives/c5-external-model-tooling.md). TestHarness performs isolated graphics-backed captures through a versioned request and report protocol. External roots remain separate from game assets, and preview corrections never rewrite sources. Licensing/provenance remains out of scope per the [Decision](open-questions.md). | ReplaySketch inspects a glTF source, pairs animation through C2, presents stable diagnostics, and produces a deterministic TestHarness preview and report. | Done | C4 |
| C6. Extended model features (deferred) | Add morph targets and advanced PBR families only through named, request-scoped experiment gates; see the [deep dive](deep-dives/c6-extended-model-features.md). Default C4 and shipped-game behavior remains unchanged. | A triggering experiment implements and validates its selected morph or PBR family without changing feature-disabled output. | Not started | C4 |

## Phase resources

Navigation index for each phase: deep dives, architecture diagrams, and agent task cards.
Diagrams are required before any phase's first source-edit task card is marked `Ready`.

| Phase | Deep dive | Diagrams | Agent cards |
|---|---|---|---|
| C1 | [`c1-import-abstraction.md`](deep-dives/c1-import-abstraction.md) | [Architecture diagrams](deep-dives/c1-import-abstraction.md#9-architecture-diagrams) | [`WI-C1-01..14`](agent/cards/) |
| C2 | [`c2-skeleton-animation-retargeting.md`](deep-dives/c2-skeleton-animation-retargeting.md) | [Architecture diagrams](deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) | [`WI-C2-01..06`](agent/cards/) |
| C3 | [`c3-material-texture-mapping.md`](deep-dives/c3-material-texture-mapping.md) | [Architecture diagrams](deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams) | [`WI-C3-01..11`](agent/cards/) |
| C4 | [`c4-gltf-importer.md`](deep-dives/c4-gltf-importer.md) | [Architecture diagrams](deep-dives/c4-gltf-importer.md#11-architecture-diagrams) | [`WI-C4-01..12`](agent/cards/) |
| C4a | [`ee2-gamebryo-model-storage.md`](deep-dives/ee2-gamebryo-model-storage.md) | [Architecture diagrams](deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams) | [`WI-C4a-01..11`](agent/cards/) |
| C5 | [`c5-external-model-tooling.md`](deep-dives/c5-external-model-tooling.md) | [Architecture diagrams](deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) | [`WI-C5-01..12`](agent/cards/) |
| C6 | [`c6-extended-model-features.md`](deep-dives/c6-extended-model-features.md) | [Architecture diagrams](deep-dives/c6-extended-model-features.md#11-architecture-diagrams) | — |

## Cross-track dependencies

- C1 and A3 both touch the presentation seam, so the import abstraction should feed the same runtime `Model`/`ModelInstance` types that [B5](track-b-scale.md) expects.
- B5 should account for Track C models so instancing is not W3D-specific.
- C4a turns the [Empire Earth II Gamebryo survey](deep-dives/ee2-gamebryo-model-storage.md)
  into a static second-format proof of the C1 seam. It does not replace C4 or add EE2 as a
  supported game.
