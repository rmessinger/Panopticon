# Track C · C4i: EE2 legacy KF formats

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4i row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4i-ee2-legacy-kf-formats.md](../deep-dives/c4i-ee2-legacy-kf-formats.md), including
its [architecture diagrams](../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams).

## Scope

Extend the C4c EE2 KF importer to NIF 10.0.1.0 DDF-bound legacy clips, including uppercase `.KF`
paths, legacy text-key spelling, root-list footers, and rigid proof clips for the guntank and
palisade gate. Reject the three NetImmerse 4.2.2.0 files with an explicit diagnostic.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4i-01`](../agent/cards/WI-C4i-01-add-legacy-kf-header-layouts.md) | Version-gated 10.0.1.0 header and footer parsing plus 4.2.2.0 rejection. |
| 2 | [`WI-C4i-02`](../agent/cards/WI-C4i-02-reuse-kf-block-readers-for-legacy.md) | C4c keyframe blocks accepted under 10.0.1.0 field conditions. |
| 3 | [`WI-C4i-03`](../agent/cards/WI-C4i-03-normalize-legacy-kf-semantics.md) | Text-key trimming, legacy flag validation, and uppercase `.KF` resolution. |
| 4 | [`WI-C4i-04`](../agent/cards/WI-C4i-04-bind-ddf-legacy-proof-clips.md) | Installed guntank and palisade legacy clip import and pose checks. |
| 5 | [`WI-C4i-05`](../agent/cards/WI-C4i-05-lock-legacy-family-rejections.md) | Property, morph, skinned, and NetImmerse rejection fixtures. |
| 6 | [`WI-C4i-06`](../agent/cards/WI-C4i-06-capture-legacy-rigid-proofs.md) | Deterministic legacy rigid captures and phase completion. |

Cards remain blocked until their C4c prerequisite cards are done. Card 6 is the completion point.

---

## Agent context

- Parser boundary: `NifHeader` becomes version-aware for 10.1.0.0 and 10.0.1.0. NetImmerse
  4.2.2.0 is rejected before block decoding.
- 10.0.1.0 condition: no user-version field, but the block type table and per-block leading
  `u32 0` remain present.
- Footer: read `num roots` plus root refs for all supported versions instead of assuming the fixed
  `(1, 0)` pair.
- C4c block readers remain the implementation target. No duplicate curve evaluator or converter
  belongs in C4i.
- Proof clips: `LLM11_guntank_idle.kf`, `LLM11_guntank_walk.kf`,
  `bld_palisade1_gate_open.kf`, and `bld_palisade1_gate_close.kf`.
- Handoffs: C4e owns `NiMaterialColorController`, `NiVisController`, and `NiUVController`; C4g
  owns `NiGeomMorpherController`; C4d owns skinned target models.
