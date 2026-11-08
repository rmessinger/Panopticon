# Track C · C4h: EE2 particle effects

> Audience: human overview above the divider; agent-context detail below it.

Implements the C4h row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c4h-ee2-particle-effects.md](../deep-dives/c4h-ee2-particle-effects.md), including its
[architecture diagrams](../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams).

## Scope

Import DDF-bound EE2 legacy `NiParticles` effects from `fx_*` NIFs, convert the supported subset
into C1-style particle effect interchange, materialize it onto the existing Panopticon particle
runtime, and capture deterministic explosion, muzzle-smoke, and water-splash proofs. Newer
`NiPSys*` blocks are absent from the measured EE2 corpus and fail explicitly.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C4h-01`](https://projects.reid-corp.com/work_packages/523) | Locked DDF-bound proof inventory and particle corpus descriptor tests. |
| 2 | [`WI-C4h-02`](https://projects.reid-corp.com/work_packages/524) | NIF parser blocks for legacy particle effects and exact proof parsing. |
| 3 | [`WI-C4h-03`](https://projects.reid-corp.com/work_packages/525) | CPU-only imported particle effect values and invariant tests. |
| 4 | [`WI-C4h-04`](https://projects.reid-corp.com/work_packages/526) | Materializer that creates existing `FXParticleSystemTemplate` values. |
| 5 | [`WI-C4h-05`](https://projects.reid-corp.com/work_packages/527) | DDF attachment binding to world origins or C4c node bones. |
| 6 | [`WI-C4h-06`](https://projects.reid-corp.com/work_packages/528) | Deterministic TestHarness captures for the three proof effects. |

Cards 1 and 2 start after C4c is complete. Card 3 follows card 2. Card 4 follows cards 2 and 3.
Card 5 follows card 4. Card 6 is the completion point.

---

## Agent context

- Parser boundary: legacy particle blocks join `Panopticon.FileFormats.Nif`; `NiPSys*` names are
  registered as unsupported with explicit tests because EE2 `fx_*` NIFs do not use them.
- Import boundary: `ImportedParticleEffect` and child values are CPU-only C1-style types. The NIF
  importer returns those values and never creates live particle systems.
- Runtime boundary: `ImportedParticleEffectMaterializer` maps to existing `FXParticleSystemTemplate`
  and `ParticleSystemManager`; no new particle runtime is added.
- Timing: C4c key-group and time-controller decoding is reused for adjacent alpha, material color,
  and keyframe controllers.
- Textures: C4b texcache decoding supplies particle texture names. Missing textures use the same
  fallback policy as external model materialization.
- Attachments: DDF `TagPoint`, muzzle tag, child visual, and root bindings resolve to C4c node
  names with exact case-insensitive matching.
- Determinism: capture tests seed the particle random source per effect and run fixed 30 Hz updates.
