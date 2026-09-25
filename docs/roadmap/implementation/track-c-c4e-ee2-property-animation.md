# Track C · C4e: EE2 property animation

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4e row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4e-ee2-property-animation.md](../deep-dives/c4e-ee2-property-animation.md), including
its [architecture diagrams](../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams).

## Scope

Import the DDF-bound EE2 NIF 10.1 property-animation controller families: visibility, base-slot UV
translation, material color, and material alpha. Property channels target sub-objects or material
slots through C1, bind through C2's animation activation boundary, and render through per-instance
material state.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4e-01`](../agent/cards/WI-C4e-01-decode-ee2-property-controllers.md) | Property controller and data blocks in `Panopticon.FileFormats.Nif`. |
| 2 | [`WI-C4e-02`](../agent/cards/WI-C4e-02-resolve-ee2-property-targets.md) | NIF target resolution for sub-objects and material slots. |
| 3 | [`WI-C4e-03`](../agent/cards/WI-C4e-03-extend-c1-property-channels.md) | C1 and runtime target/channel types for property animation. |
| 4 | [`WI-C4e-04`](../agent/cards/WI-C4e-04-convert-ee2-property-animation.md) | NIF property-animation converter emitting C1 channels. |
| 5 | [`WI-C4e-05`](../agent/cards/WI-C4e-05-add-runtime-material-animation.md) | Per-instance runtime material and UV animation state. |
| 6 | [`WI-C4e-06`](../agent/cards/WI-C4e-06-verify-ee2-property-proofs.md) | Installed proof assertions and deterministic captures. |

Cards 1 and 2 can proceed after their C4c prerequisites. Card 3 follows card 1. Card 4 needs
cards 1 through 3. Card 5 follows card 3 and can proceed in parallel with card 4 after the target
contract is fixed. Card 6 is the completion point.

---

## Agent context

- Parser boundary: C4e extends the C4c KF parser. It does not duplicate sequence, text-key,
  time-controller, or Hermite key code.
- Target model: property channels target sub-objects or material slots. They do not target bones
  except for visibility controllers whose target resolves to a `NiNode` bone.
- Channel set: `Visibility` is reused. New channels cover UV translate, UV scale, UV rotation,
  material color fields, and material alpha.
- Runtime: property clips bind against a `Model`, not only a `ModelBoneHierarchy`, because material
  slots belong to mesh parts.
- Renderer: animated values are per-instance. Shared `Material`, `MaterialPass`, and fixed-function
  material-cache resources remain immutable.
- Proof assets: `bld_market8_open/close.kf`, `civ12_EIFV_egypt_walk.kf`,
  `afb14_StealthFighterBomber_idle.kf`, and `sh_faufish_idle01.kf`.
- Installed data: `EMPIRE_EARTH_II_PATH/zips/graphics.zip`, `textures.zip`, and the `db*.zip`
  database stack. Tests that require them are interactive.
