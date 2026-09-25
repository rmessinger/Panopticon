# Track C · C4c: EE2 rigid KF animation

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4c row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4c-ee2-rigid-animation.md](../deep-dives/c4c-ee2-rigid-animation.md), including its
[architecture diagrams](../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams).

## Scope

Import the seven DDF-bound NIF 10.1 `.kf` clips of the War Galley and Twin-Engine Bomber, keep
the NIF node hierarchy as runtime bones, and play the clips through C2 in textured TestHarness
captures. Skinning is C4d.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4c-01`](../agent/cards/WI-C4c-01-decode-kf-animation-blocks.md) | KF sequence, text-key, controller, and keyframe blocks. |
| 2 | [`WI-C4c-02`](../agent/cards/WI-C4c-02-preserve-nif-node-hierarchy.md) | Node skeleton with bone-local rigid meshes and rest-pose parity. |
| 3 | [`WI-C4c-03`](../agent/cards/WI-C4c-03-evaluate-nif-key-curves.md) | Hermite curves and Euler-to-quaternion resampling. |
| 4 | [`WI-C4c-04`](../agent/cards/WI-C4c-04-add-nif-animation-importer.md) | Model-qualified KF importer emitting C1 channels. |
| 5 | [`WI-C4c-05`](../agent/cards/WI-C4c-05-verify-ee2-clip-descriptors.md) | Installed clip, binding, and pose checks. |
| 6 | [`WI-C4c-06`](../agent/cards/WI-C4c-06-capture-animated-proof-models.md) | Deterministic animated captures. |

Cards 1 and 2 can proceed in parallel. Card 3 follows card 1. Card 4 needs cards 2 and 3.
Card 6 is the completion point.

---

## Agent context

- Parser boundary: KF block types join `Panopticon.FileFormats.Nif`. `NifFile.Roots` stays
  NiNode-only, so KF files have no roots, and the importer selects block 0.
- Skeleton: `NifSceneConverter` owns bone construction for both importers, so animation rests
  equal model rests.
- Keys: `<model-key>#kf=<archive-relative-kf-path>`, parallel to glTF `#animation=`.
- Channels: bone-name targets, `Linear` interpolation, and rest-relative offsets under the C4
  formula.
- Playback: `ImportedAnimationMaterializer`, then `ModelInstance.CreateAnimationInstance` binds
  through C2. The TestHarness poses instances with `PlayToTime`.
- Installed data: `EMPIRE_EARTH_II_PATH/zips/graphics.zip` and `textures.zip`. Tests that require
  them are interactive.
