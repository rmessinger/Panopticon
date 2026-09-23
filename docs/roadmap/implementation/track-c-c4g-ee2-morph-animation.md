# Track C · C4g: EE2 morph animation

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4g row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4g-ee2-morph-animation.md](../deep-dives/c4g-ee2-morph-animation.md), including its
[architecture diagrams](../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams).

## Scope

Import EE2 NIF 10.1 `NiGeomMorpherController` / `NiMorphData` morph targets through the C6
`morph-targets` gate, add the shared CPU morph runtime, and prove the DDF-bound farm yam seeding
clip plus one embedded morph model.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4g-01`](../agent/cards/WI-C4g-01-decode-nif-morph-blocks.md) | NIF 10.1 morph controller and morph data blocks. |
| 2 | [`WI-C4g-02`](../agent/cards/WI-C4g-02-add-morph-interchange-gate.md) | C6 morph interchange types and request-scoped gate plumbing. |
| 3 | [`WI-C4g-03`](../agent/cards/WI-C4g-03-import-nif-morph-targets.md) | NIF morph target sets for embedded controllers and paired KF data. |
| 4 | [`WI-C4g-04`](../agent/cards/WI-C4g-04-materialize-cpu-morph-runtime.md) | CPU morph buffers applied before skinning. |
| 5 | [`WI-C4g-05`](../agent/cards/WI-C4g-05-bind-morph-weight-animation.md) | Morph weight animation tracks bound to model target sets. |
| 6 | [`WI-C4g-06`](../agent/cards/WI-C4g-06-capture-ee2-morph-proofs.md) | Installed proof tests and deterministic captures. |

Cards 1 and 2 can proceed after C4c completes. Card 3 needs cards 1 and 2. Card 4 needs card 2.
Card 5 needs cards 3 and 4. Card 6 is the completion point.

---

## Agent context

- C4g depends on C4c for the NIF node hierarchy, key evaluation helpers, and `<model>#kf=<path>`
  animation key.
- C4g implements the shared C6 morph runtime under the `morph-targets` gate rather than waiting
  for all of C6.
- The main proof pair is `cache/nifcache/bld_farm_yam.nifcache` plus
  `Graphics/Building/bld_farm_yam.kf`, bound from `db/Units/farm.ddf`.
- `NiMorphData` morph 0 is the absolute base and is validated, then omitted. Morphs 1..N are
  position deltas.
- KF-owned morph target data must be paired into the model before materialization through
  `<model>#morph-kf=<path>`. Weight animation stays on `<model>#kf=<path>`.
- Runtime deformation order is morph targets first, then existing skinning.
