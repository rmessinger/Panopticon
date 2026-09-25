# Track C · C4d: EE2 skinned character animation

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4d row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4d-ee2-skinned-animation.md](../deep-dives/c4d-ee2-skinned-animation.md), including
its [architecture diagrams](../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams).

## Scope

Import the DDF-bound British Druid (`CIV1_Druid_british`) with `NiSkinData` inverse binds and up
to four influences per vertex. Play its five DDF-bound clips through the C4c converter and C2 in
textured TestHarness captures. Widen C1, the runtime vertex layout, and glTF import to four
influences. Correct the NIF rotation convention first.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4d-01`](../agent/cards/WI-C4d-01-fix-nif-rotation-convention.md) | Column-convention NIF rotations; C4a and C4c locked outputs regenerated. |
| 2 | [`WI-C4d-02`](../agent/cards/WI-C4d-02-decode-nif-skin-blocks.md) | `NiSkinInstance`, `NiSkinData`, and `NiSkinPartition` blocks. |
| 3 | [`WI-C4d-03`](../agent/cards/WI-C4d-03-widen-c1-skin-influences.md) | Four-slot `ImportedVertex` and C1 validation. |
| 4 | [`WI-C4d-04`](../agent/cards/WI-C4d-04-widen-runtime-skin-influences.md) | Four-slot mesh vertex layout and shaders. |
| 5 | [`WI-C4d-05`](../agent/cards/WI-C4d-05-accept-gltf-four-influences.md) | glTF import accepts third and fourth influences. |
| 6 | [`WI-C4d-06`](../agent/cards/WI-C4d-06-convert-nif-skinned-shapes.md) | NIF skin conversion to model-space vertices and inverse binds. |
| 7 | [`WI-C4d-07`](../agent/cards/WI-C4d-07-verify-ee2-skinned-proof.md) | Installed Druid assertions and deterministic captures. |

Cards 1, 2, and 3 can proceed in parallel. Card 4 follows card 3. Card 5 needs cards 3 and 4.
Card 6 needs cards 1, 2, and 3. Card 7 needs cards 4 and 6 and is the completion point.

---

## Agent context

- Convention: NIF `Matrix33` is column-vector. Transpose once when copying into `Matrix4x4`.
  Quaternion keys stay unchanged. Card 1 owns the only change to this rule.
- Parser boundary: skin blocks join `Panopticon.FileFormats.Nif`. The C4a skinned-geometry
  rejection in `NifFile.ValidateReferences` becomes typed reference validation.
- Spaces: vertices are stored in skin space. Model space is `S^-1 * p`; inverse bind is `B_i * S`
  in column form, conjugated to the engine frame. The shape transform is ignored for skinned
  geometry.
- Influence policy: meaningful above 1e-4, at most four, sum within 0.01 of 1, normalized,
  descending order. Shared by NIF and glTF converters.
- Runtime: one shared `MeshVertex.Basic` layout. Slots 2 and 3 skin primary geometry. W3D
  writes zeros.
- Animation: C4c converter unchanged. Offsets are relative to the stored node rest, not the bind
  pose.
- Proof assets: `cache/nifcache/CIV1_Druid_british.nifcache`,
  `Graphics/Humans/CIV1_Druid_british_{idle01,walk01,jog01,attack01,death01}.kf`,
  `cache/texcache/CIV1_Druid_british.texcache`. Negative fixtures: `lsf7_priest_fe`,
  `llm2_mountedslinger_fe`.
- Installed data: `EMPIRE_EARTH_II_PATH/zips/graphics.zip` and `textures.zip`. Tests that
  require them are interactive.
