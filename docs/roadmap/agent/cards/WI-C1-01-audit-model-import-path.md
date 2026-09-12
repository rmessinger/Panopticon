# Task Card `WI-C1-01`: Audit the W3D model import path

| Field | Value |
|---|---|
| Card ID | `WI-C1-01` |
| Roadmap phase | [Track C · C1: Format-agnostic import abstraction](../../track-c-external-assets.md) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | This card grounds the later C1 architecture diagrams by documenting the current model path. |

## Goal

Produce the authoritative trace from a model asset name to the runtime `Model`, including hierarchy
resolution, render-object conversion, and GPU resource creation. The trace must identify every
W3D-specific assumption that the C1 interchange and importer contracts must isolate. Read-only: no
source changes.

## Context to read (and only this)

- `docs/roadmap/track-c-external-assets.md`: the C1 scope and milestone.
- `docs/roadmap/seams.md:110-120`: the planned model/import seam.
- `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs`: current loader construction and path
  resolver ownership.
- `src/OpenSage.Game/Content/ScopedAssetCollection.cs:61-88`: cache miss and on-demand dispatch.
- `src/OpenSage.Game/Content/AssetStore.cs:137-140,184-202,311-314`: model collections, load context,
  and loader registration.
- `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs`: file resolution, W3D parsing,
  hierarchy selection, sub-object dispatch, and runtime `Model` construction.
- `src/OpenSage.Game/Content/Loaders/OnDemandModelBoneHierarchyLoader.cs`: separately referenced
  hierarchy loading.
- `src/OpenSage.Game/Graphics/Model.cs`: the format-neutral runtime model surface.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`: current W3D-to-runtime hierarchy conversion.
- `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs`: current W3D mesh, material, texture, and GPU-resource
  conversion.
- `src/OpenSage.Game/Graphics/ModelMesh.cs:17-55`: the runtime mesh fields consumed by rendering.

## Steps

1. Trace one `AssetStore.Models.GetByName(name)` cache miss through
   `ScopedAssetCollection<Model>`, `OnDemandAssetLoadStrategy`, path resolution, `W3dFile` parsing,
   hierarchy resolution, sub-object construction, and the returned `Model`.
2. Record each stage in a table with: input type, output type, owning method, file:line, whether it
   is format-specific, whether it allocates a GPU resource, and failure behavior.
3. Cover every branch in `OnDemandModelLoader`: embedded hierarchy, referenced hierarchy, default
   hierarchy, HLOD sub-objects, the single-renderable fallback, skipped missing renderables,
   `W3dMesh`, `W3dBox`, and unsupported render-object chunks.
4. In `ModelMesh.W3d.cs`, classify conversion responsibilities as geometry, skinning, hierarchy,
   material/shader, texture lookup, bounds, or GPU upload. Record the exact runtime field each
   responsibility populates.
5. List every W3D-specific type or convention that crosses into `Model`,
   `ModelBoneHierarchy`, or `ModelMesh` construction. Separately list runtime consumers that are
   already format-neutral. Do not propose final interchange type names or signatures.
6. Create `docs/roadmap/implementation/track-c-c1-import-abstraction.md`. Above the `---` divider,
   state the C1 scope and planned audit-to-design sequence. Below it, add a
   `## Current model import path` section containing the tables and boundary findings.
7. Add the C1 plan to `docs/roadmap/implementation/README.md`, matching the existing track-section
   and table format.
8. Mark unresolved ownership or behavior as `ambiguous` with a one-line reason rather than
   guessing.

## Files likely to change

- `docs/roadmap/implementation/track-c-c1-import-abstraction.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] The trace covers every model and hierarchy branch listed in step 3.
- [ ] Every conversion responsibility in `ModelMesh.W3d.cs` is tied to its runtime output and GPU
      boundary.
- [ ] Format-specific assumptions and format-neutral runtime consumers are separate explicit
      lists.
- [ ] Every table row includes a file:line citation and failure behavior.
- [ ] No `.cs` file is modified.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not design or implement `IModelImporter`, interchange DTOs, or a loader factory.
- Do not change W3D parsing, rendering, materials, or asset lookup.
- Do not audit animation conversion; that is `WI-C1-02`.
- Stop at `Model` / `ModelMesh` runtime construction. C2 retargeting, C3 material translation, and
  C4 glTF support are separate phases.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Do not change the C1 phase Status; the phase has not started implementation.
- Append a result line to repo-root `.exchange`, including the count of `ambiguous` findings.
- Follow-up cards: none new; `WI-C1-02` extends this audit to animations.
