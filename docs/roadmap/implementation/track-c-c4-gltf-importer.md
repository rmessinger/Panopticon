# Track C · C4: Production glTF importer

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4 row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4-gltf-importer.md](../deep-dives/c4-gltf-importer.md), including its
[architecture diagrams](../deep-dives/c4-gltf-importer.md#11-architecture-diagrams).

## Scope

Add strict `.gltf` and `.glb` import behind the C1 interchange contracts. The importer supports
the bounded scene, mesh, skin, animation, and core PBR subset defined by the deep dive. SharpGLTF
parser objects remain internal to `Content/Importing/Gltf`; runtime graphics code receives only
engine-owned values.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4-01`](../agent/cards/WI-C4-01-bootstrap-gltf-import.md) | SharpGLTF dependency, immutable options, key parsing, and bounded document scaffold. |
| 2 | [`WI-C4-02`](../agent/cards/WI-C4-02-resolve-gltf-resources.md) | Virtual-filesystem resource resolver with traversal protection. |
| 3 | [`WI-C4-03`](../agent/cards/WI-C4-03-add-inverse-bind-matrices.md) | Format-neutral inverse-bind storage and row-vector skinning upload. |
| 4 | [`WI-C4-04`](../agent/cards/WI-C4-04-add-animation-interpolation.md) | Format-neutral linear and step interpolation. |
| 5 | [`WI-C4-05`](../agent/cards/WI-C4-05-convert-gltf-scene-nodes.md) | Selected-scene node, transform, rigid attachment, scale, and bounds conversion. |
| 6 | [`WI-C4-06`](../agent/cards/WI-C4-06-decode-gltf-primitives.md) | Accessor decoding and deterministic 65,535-vertex splitting. |
| 7 | [`WI-C4-07`](../agent/cards/WI-C4-07-convert-gltf-skinning.md) | One-skin, two-influence conversion with inverse-bind validation. |
| 8 | [`WI-C4-08`](../agent/cards/WI-C4-08-convert-gltf-animations.md) | Bind-relative named animation conversion for `LINEAR` and `STEP`. |
| 9 | [`WI-C4-09`](../agent/cards/WI-C4-09-map-gltf-pbr-materials.md) | Core glTF PBR, image, UV, sampler, alpha, and culling mapping. |
| 10 | [`WI-C4-10`](../agent/cards/WI-C4-10-enforce-gltf-import-policy.md) | Extension policy, complete limits, exception contract, and explicit importer composition. |
| 11 | [`WI-C4-11`](../agent/cards/WI-C4-11-add-gltf-fixture-matrix.md) | Repository-owned CPU fixture and exclusion matrix. |
| 12 | [`WI-C4-12`](../agent/cards/WI-C4-12-capture-gltf-runtime-proof.md) | Static PBR, rigid-node animation, and skinned end-to-end runtime captures. |

## Dependencies

Cards 1 and 2 start only after `WI-C1-14` completes. Card 3 also waits for C1 completion because it
changes the shared skeleton and runtime skinning contract. Card 4 waits for `WI-C2-06` so it
extends the final C2 channel and binding model rather than an intermediate shape.

Scene conversion precedes primitive conversion because it owns stable node order, attachment
indices, coordinate conversion, and scale policy. Skinning then combines those node mappings with
decoded primitive data. Animation conversion depends on the scene mapping and C2 interpolation
work, but not on PBR conversion.

PBR mapping waits for `WI-C3-09` and `WI-C3-10`, which establish and test the materialization
contract. Card 10 composes the completed converters and enforces whole-document policy. Card 11
locks CPU behavior against committed fixtures. Card 12 depends on every earlier C4 card,
`WI-C2-06`, `WI-C3-11`, and the `WI-C4a-10` capture helper; it is the C4 completion point.
`WI-C3-11` also depends on `WI-C4a-10`, not on C4, so this ordering does not form a cycle.

---

## Agent context

- Parser boundary: only files under `src/OpenSage.Game/Content/Importing/Gltf/` (new in C4) may
  reference `SharpGLTF.Schema2`.
- Public configuration: `GltfImportOptions` is immutable constructor state. It does not extend
  `AssetImportContext`.
- Source keys: model keys end in `.gltf` or `.glb`; animation keys append
  `#animation=<zero-based-index>`.
- Resource ownership: roots and relatives open through `AssetImportContext.FileSystem`. There is
  no `System.IO.File` or current-directory fallback.
- Basis and units: `(x, y, z)` becomes `(z, x, y)` and one meter becomes `3.28084` world units by
  default. The metric-units helper named in the deep dive does not exist in current source.
- Skeleton order: selected-scene nodes are stable depth-first, parents before children. A
  synthetic root exists only for multiple scene roots.
- Skinning: one skin, two meaningful influences, model-space primary and secondary geometry, and
  `InverseBindMatrix * RelativeBoneTransform` row-vector order.
- Animation: named C2 targets, rest-relative translation and rotation offsets, and only linear or
  step interpolation.
- Materials: card 9 consumes the C3 interchange contract. It does not add glTF fields to runtime
  materials or shaders.
- Construction: C4 importers are instantiated explicitly by internal callers and tests. C4 adds
  no process-global format registry or shipped-game registration.
- Fixtures: project-authored assets only. Runtime tests never download parser data, images, or the
  Khronos validator.
