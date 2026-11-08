# Track C · C3: Material and texture mapping

> Audience: human overview above the divider; agent-context detail below it.

Implements the C3 row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c3-material-texture-mapping.md](../deep-dives/c3-material-texture-mapping.md), including
its [architecture diagrams](../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams).

## Scope

Add format-neutral core metallic-roughness materials, deterministic tangent generation, external
and embedded image materialization, sampler and color-space semantics, and one engine-owned PBR
shader path. Existing W3D fixed-function and named shader-material paths remain unchanged.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C3-01`](https://projects.reid-corp.com/work_packages/456) | Typed PBR, texture, image, and sampler interchange values. |
| 2 | [`WI-C3-02`](https://projects.reid-corp.com/work_packages/457) | Complete CPU-side PBR graph validation. |
| 3 | [`WI-C3-03`](https://projects.reid-corp.com/work_packages/458) | Deterministic tangent-basis generation. |
| 4 | [`WI-C3-04`](https://projects.reid-corp.com/work_packages/459) | External and embedded encoded-image decoding. |
| 5 | [`WI-C3-05`](https://projects.reid-corp.com/work_packages/460) | GPU texture/sampler materialization and caching. |
| 6 | [`WI-C3-06`](https://projects.reid-corp.com/work_packages/461) | PBR shader set, resource layouts, and base-color path. |
| 7 | [`WI-C3-07`](https://projects.reid-corp.com/work_packages/462) | Core metallic-roughness lighting and texture roles. |
| 8 | [`WI-C3-08`](https://projects.reid-corp.com/work_packages/463) | Alpha, culling, sampler, and material cache states. |
| 9 | [`WI-C3-09`](https://projects.reid-corp.com/work_packages/464) | Imported-model materializer integration. |
| 10 | [`WI-C3-10`](https://projects.reid-corp.com/work_packages/465) | Synthetic validation and materialization coverage. |
| 11 | [`WI-C3-11`](https://projects.reid-corp.com/work_packages/466) | Deterministic visual acceptance matrix. |

Cards 2, 3, 4, and 6 can proceed independently after card 1. Cards 6 and 7 separate shader
scaffolding from lighting behavior. Card 11 reuses the shared runtime-model capture helper from
`WI-C4a-10` and is the completion point.

---

## Agent context

- Interchange root: `src/Panopticon.Simulation/Content/Importing/ImportedMaterial.cs`.
- PBR payload: optional typed data; fixed-function and PBR payloads cannot coexist.
- Validation boundary: the entire imported graph is checked before GPU allocation or asset-store
  access.
- Tangent generation: CPU-only, indexed triangles, deterministic accumulation and
  orthonormalization; incomplete or degenerate inputs fail explicitly.
- Image sources: exact virtual paths or immutable encoded bytes with MIME type and diagnostic
  name. Missing referenced images are errors.
- Texture roles own color-space and channel interpretation. Uploaded textures remain ordinary
  UNorm data.
- Shader owner: `PbrMetallicRoughnessShaderResources`, independent of W3D shader-material
  resources.
- Cache identity includes every output-affecting factor, texture role, UV selection, sampler,
  alpha mode, cutoff, culling state, and pipeline state.
- Visual acceptance uses deterministic TestHarness captures for the feature matrix in the deep
  dive.
