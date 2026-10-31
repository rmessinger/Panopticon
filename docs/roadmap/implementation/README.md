# Implementation plans

> Audience: human overview of the implementation plan set. Code-level details sit below the
> divider in each plan.

Concrete implementation plans for [implementation-checklist.md](../implementation-checklist.md)
items, ordered by the dependency sequence in the checklist. Each plan cites one checklist item,
the deep dive it derives from, and the implementation scope.

These are design docs, not the checklist itself. The checklist tracks status; these explain how.

## Track P · P0 : ReplaySketch arbitrary-command harness

Source of truth: [deep-dives/p0-playable-baseline-harness.md](../deep-dives/p0-playable-baseline-harness.md) :
[Architecture diagrams §8](../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-p-p0-1-open-command-model.md](track-p-p0-1-open-command-model.md) | P0-1 | Open command model (done) |
| [track-p-p0-1b-viewmodel-testability.md](track-p-p0-1b-viewmodel-testability.md) | P0-1b | `IPreferencesStore` injection |
| [track-p-p0-2-generic-definition-resolution.md](track-p-p0-2-generic-definition-resolution.md) | P0-2 | Generic object-definition resolution |
| [track-p-p0-2b-player-owner-targeting.md](track-p-p0-2b-player-owner-targeting.md) | P0-2 | Per-player/owner targeting |
| [track-p-p0-3-runtime-object-references.md](track-p-p0-3-runtime-object-references.md) | P0-3 | Runtime object references (handles + selectors) |
| [track-p-p0-4-ordertype-mapping.md](track-p-p0-4-ordertype-mapping.md) | P0-3 | Gameplay `OrderType` mapping |
| [track-p-p0-5-assertion-layer.md](track-p-p0-5-assertion-layer.md) | P0-4 | Validation/assertion layer |
| [track-p-p0-6-run-and-assert-entry-point.md](track-p-p0-6-run-and-assert-entry-point.md) | P0-5 | Run-and-assert entry point + end-to-end test |
| [track-p-p0-7-procedural-authoring.md](track-p-p0-7-procedural-authoring.md) | P0-6 | Procedural authoring at scale |
| [deep dive §2.8](../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8) | P0-8 | TestHarness launcher UI (design in the deep dive; cards `WI-P0-20` to `WI-P0-29`) |

## Track A · A1 : Headless simulation host

Source of truth: [deep-dives/a1-headless-host.md](../deep-dives/a1-headless-host.md) :
[Architecture diagrams §5](../deep-dives/a1-headless-host.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a1-headless-host.md](track-a-a1-headless-host.md) | A1-1…A1-5 | Headless simulation host |

## Track A · A1-6 : Operational recording and checkpoint re-simulation

Source of truth:
[deep-dives/a1-6-operational-recording.md](../deep-dives/a1-6-operational-recording.md) :
[Architecture diagrams §10](../deep-dives/a1-6-operational-recording.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a1-6-operational-recording.md](track-a-a1-6-operational-recording.md) | A1-6a…A1-6g | Operational recording and checkpoint re-simulation (WI-A1-13..24) |

## Track A · A2 : Statistical validation

Source of truth: [deep-dives/a2-reproducibility.md](../deep-dives/a2-reproducibility.md) :
[Architecture diagrams §5](../deep-dives/a2-reproducibility.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a2-reproducibility.md](track-a-a2-reproducibility.md) | audits + seeded-source conversion | Statistical validation |

## Track A · A3 : Simulation/presentation seam

Source of truth: [deep-dives/a3-sim-presentation-seam.md](../deep-dives/a3-sim-presentation-seam.md) :
[Architecture diagrams §8](../deep-dives/a3-sim-presentation-seam.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a3-sim-presentation-seam.md](track-a-a3-sim-presentation-seam.md) | A3-1…A3-4 | Simulation/presentation seam |
| [track-a-a4-simulation-domain-model.md](track-a-a4-simulation-domain-model.md) | A4-1 | Simulation Domain Model seam |
| [track-a-a4-ai-policy-evolution.md](track-a-a4-ai-policy-evolution.md) | A4 evolution follow-on | Neural-policy evolutionary training (WI-A4-11..16) |
| [track-a-a4-hierarchical-ai.md](track-a-a4-hierarchical-ai.md) | A4-3 | Hierarchical AI: ranks, contracts, co-evolution (WI-A4-17..30) |

## Track A · A4-4 : Domain state projection

Source of truth:
[deep-dives/a4-4-domain-state-projection.md](../deep-dives/a4-4-domain-state-projection.md) :
[Architecture diagrams §8](../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a4-domain-state-projection.md](track-a-a4-domain-state-projection.md) | A4-4a…A4-4d | Domain state projection (WI-A4-42..45) |

## Track A · A4 : Experiment seams and harness

Source of truth: [seam registry](../seams.md) and
[a4-experiment-harness.md](../deep-dives/a4-experiment-harness.md).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a4-experiment-seams.md](track-a-a4-experiment-seams.md) | WI-A4-31..36 | Seam extraction audit and registrations |
| [track-a-a4-experiment-harness.md](track-a-a4-experiment-harness.md) | WI-A4-37..41 | Experiment harness, registry, runs, and comparison |

## Track A · A5 : Scenario authoring & visualization

Source of truth: [deep-dives/a5-visualization.md](../deep-dives/a5-visualization.md) :
[Architecture diagrams §8](../deep-dives/a5-visualization.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a5-scenario-visualization.md](track-a-a5-scenario-visualization.md) | A5-1…A5-5 | Scenario authoring & visualization (WI-A5-02..33) |

## Track A · A5-6 : WorldBuilder baseline

Source of truth: [deep-dives/a5-visualization.md](../deep-dives/a5-visualization.md) §6-§7 (A5-6 row).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a5-6-worldbuilder-baseline.md](track-a-a5-6-worldbuilder-baseline.md) | A5-6 | WorldBuilder functional baseline audit (WI-A5-01, follow-ups 02/27/28/34/35) |

## Track B · B1 : Benchmarking


Source of truth: [deep-dives/b1-benchmarking.md](../deep-dives/b1-benchmarking.md) :
[Architecture diagrams §](../deep-dives/b1-benchmarking.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b1-benchmarking.md](track-b-b1-benchmarking.md) | B1-1…B1-7 | Benchmarking utilities (WI-B1-01..07) |

## Track B · B4 : Spatial and pathfinding scaling

Source of truth: [deep-dives/b4-spatial-pathfinding-scaling.md](../deep-dives/b4-spatial-pathfinding-scaling.md) :
[Architecture diagrams](../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b4-spatial-scaling.md](track-b-b4-spatial-scaling.md) | B4-1…B4-6 | Partition hot path, group movement, async requests, gated hierarchy (WI-B4-01..06) |

## Track B · B4a : Larger maps

Source of truth: [deep-dives/b4a-larger-maps.md](../deep-dives/b4a-larger-maps.md) :
[Architecture diagrams §](../deep-dives/b4a-larger-maps.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b4a-larger-maps.md](track-b-b4a-larger-maps.md) | B4a-1…B4a-6 | Larger maps (WI-B4a-01..11) |

## Track R · R1-R2 : Geospatial import core, payload, and dev preview (formerly B4b)

Source of truth:
[deep-dives/b4b-geospatial-map-generation.md](../deep-dives/b4b-geospatial-map-generation.md) :
[Architecture diagrams §10](../deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b4b-geospatial.md](track-b-b4b-geospatial.md) | R1 (B4b-1…B4b-6), R2 (B4b-6a, B4b-6b) | Geospatial map generation (WI-B4b-01..24) |

## Track B · B5 : Rendering at scale

Source of truth: [deep-dives/b5-rendering-at-scale.md](../deep-dives/b5-rendering-at-scale.md) :
[Architecture diagrams §6](../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b5-rendering-scale.md](track-b-b5-rendering-scale.md) | B5-1…B5-5 | Rendering at scale (WI-B5-01..05) |

## Track B · B6 : Distributed simulation

Source of truth: [deep-dives/b6-distributed-simulation.md](../deep-dives/b6-distributed-simulation.md) :
[Architecture diagrams §9](../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b6-distributed-simulation.md](track-b-b6-distributed-simulation.md) | B6-1, B6-2, B6-6 | Distributed simulation (WI-B6-01..10) |

## Track B · B7 : Variable fidelity

Source of truth: [deep-dives/b7-variable-fidelity.md](../deep-dives/b7-variable-fidelity.md) :
[Architecture diagrams §9](../deep-dives/b7-variable-fidelity.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b7-variable-fidelity.md](track-b-b7-variable-fidelity.md) | B7-1, B7-4, B7-6 | Variable fidelity (WI-B7-01..07) |

## Track C · C1 : Format-agnostic import abstraction

Source of truth: [deep-dives/c1-import-abstraction.md](../deep-dives/c1-import-abstraction.md) :
[Architecture diagrams §9](../deep-dives/c1-import-abstraction.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c1-import-abstraction.md](track-c-c1-import-abstraction.md) | C1 | Format-agnostic import abstraction |

## Track C · C2 : Skeleton and animation retargeting

Source of truth:
[deep-dives/c2-skeleton-animation-retargeting.md](../deep-dives/c2-skeleton-animation-retargeting.md) :
[Architecture diagrams §8](../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c2-animation-retargeting.md](track-c-c2-animation-retargeting.md) | C2 | Skeleton / animation retargeting |

## Track C · C3 : Material and texture mapping

Source of truth:
[deep-dives/c3-material-texture-mapping.md](../deep-dives/c3-material-texture-mapping.md) :
[Architecture diagrams §9](../deep-dives/c3-material-texture-mapping.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c3-material-texture-mapping.md](track-c-c3-material-texture-mapping.md) | C3 | Material mapping |

## Track C · C4 : Production glTF importer

Source of truth:
[deep-dives/c4-gltf-importer.md](../deep-dives/c4-gltf-importer.md) :
[Architecture diagrams §11](../deep-dives/c4-gltf-importer.md#11-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4-gltf-importer.md](track-c-c4-gltf-importer.md) | C4 | First production format |

## Track C · C4a : Empire Earth II NIF 10.1 proof

Source of truth:
[deep-dives/ee2-gamebryo-model-storage.md](../deep-dives/ee2-gamebryo-model-storage.md) :
[Architecture diagrams §10](../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4a-ee2-nif-proof.md](track-c-c4a-ee2-nif-proof.md) | C4a | Empire Earth II NIF 10.1 proof of concept |

## Track C · C4c : EE2 rigid KF animation

Source of truth:
[deep-dives/c4c-ee2-rigid-animation.md](../deep-dives/c4c-ee2-rigid-animation.md) :
[Architecture diagrams §10](../deep-dives/c4c-ee2-rigid-animation.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4c-ee2-rigid-animation.md](track-c-c4c-ee2-rigid-animation.md) | C4c | EE2 rigid KF animation |

## Track C · C4d : EE2 skinned character animation

Source of truth:
[deep-dives/c4d-ee2-skinned-animation.md](../deep-dives/c4d-ee2-skinned-animation.md) :
[Architecture diagrams §12](../deep-dives/c4d-ee2-skinned-animation.md#12-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4d-ee2-skinned-animation.md](track-c-c4d-ee2-skinned-animation.md) | C4d | EE2 skinned character animation |

## Track C · C4e : EE2 property animation

Source of truth:
[deep-dives/c4e-ee2-property-animation.md](../deep-dives/c4e-ee2-property-animation.md) :
[Architecture diagrams §11](../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4e-ee2-property-animation.md](track-c-c4e-ee2-property-animation.md) | C4e | EE2 property animation |

## Track C · C4f : EE2 scale animation

Source of truth:
[deep-dives/c4f-ee2-scale-animation.md](../deep-dives/c4f-ee2-scale-animation.md) :
[Architecture diagrams §12](../deep-dives/c4f-ee2-scale-animation.md#12-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4f-ee2-scale-animation.md](track-c-c4f-ee2-scale-animation.md) | C4f | EE2 scale animation |

## Track C · C4g : EE2 morph animation

Source of truth:
[deep-dives/c4g-ee2-morph-animation.md](../deep-dives/c4g-ee2-morph-animation.md) :
[Architecture diagrams §10](../deep-dives/c4g-ee2-morph-animation.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4g-ee2-morph-animation.md](track-c-c4g-ee2-morph-animation.md) | C4g | EE2 morph animation |

## Track C · C4h : EE2 particle effects

Source of truth:
[deep-dives/c4h-ee2-particle-effects.md](../deep-dives/c4h-ee2-particle-effects.md) :
[Architecture diagrams §11](../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4h-ee2-particle-effects.md](track-c-c4h-ee2-particle-effects.md) | C4h | EE2 particle effects |

## Track C · C4i : EE2 legacy KF formats

Source of truth:
[deep-dives/c4i-ee2-legacy-kf-formats.md](../deep-dives/c4i-ee2-legacy-kf-formats.md) :
[Architecture diagrams §10](../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4i-ee2-legacy-kf-formats.md](track-c-c4i-ee2-legacy-kf-formats.md) | C4i | EE2 legacy KF formats |

## Track C · C5 : External model tooling

Source of truth:
[deep-dives/c5-external-model-tooling.md](../deep-dives/c5-external-model-tooling.md) :
[Architecture diagrams §12](../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c5-external-model-tooling.md](track-c-c5-external-model-tooling.md) | C5 | Authoring and validation tooling |

## Track C · C8a-C8c : BFME-family W3D

Source of truth:
[deep-dives/c8-sage-family-w3d.md](../deep-dives/c8-sage-family-w3d.md) :
[Architecture diagrams §9](../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams).
The deep dive's phase split (section 7) is the plan; no separate plan file.

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [c8-sage-family-w3d.md §7](../deep-dives/c8-sage-family-w3d.md#7-phase-split) | C8a | SAGE-family W3D corpus (`WI-C8a-01`, `WI-C8a-02` in parallel, then `WI-C8a-03`, `WI-C8a-04`) |
| [c8-sage-family-w3d.md §7](../deep-dives/c8-sage-family-w3d.md#7-phase-split) | C8b | BFME-family static meshes (`WI-C8b-01`, `WI-C8b-02`, `WI-C8b-03`; `WI-C8b-04` after `WI-C8a-02`) |
| [c8-sage-family-w3d.md §7](../deep-dives/c8-sage-family-w3d.md#7-phase-split) | C8c | BFME-family skinning and animation (`WI-C8c-01..03` in parallel after `WI-C8a-04`, then `WI-C8c-04`) |


## Track C · C10a-C10d : AoE3 DE Granny models

Source of truth: [c10-aoe3de-granny-models.md](../deep-dives/c10-aoe3de-granny-models.md) :
[Architecture diagrams §9](../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [c10-aoe3de-granny-models.md §7](../deep-dives/c10-aoe3de-granny-models.md#7-phase-split) | C10a | Archives, XMB, and DDT (`WI-C10a-01`, then `WI-C10a-02` and `WI-C10a-03` in parallel) |
| [c10-aoe3de-granny-models.md §7](../deep-dives/c10-aoe3de-granny-models.md#7-phase-split) | C10b | Static Granny models (`WI-C10b-01` after `WI-C10a-01`, then `WI-C10b-02..04`) |
| [c10-aoe3de-granny-models.md §7](../deep-dives/c10-aoe3de-granny-models.md#7-phase-split) | C10c | Skins, clips, and animfile resolution (`WI-C10c-01..03`, then `WI-C10c-04`) |
| [C10d architecture](../deep-dives/c10-aoe3de-granny-models.md#c10d-decompression-architecture) | C10d | [Provenance](../agent/cards/WI-C10d-01-decompressor-provenance.md), [codecs](../agent/cards/WI-C10d-02-managed-section-codecs.md), [reader integration](../agent/cards/WI-C10d-03-compressed-reader-integration.md), then [visual proof](../agent/cards/WI-C10d-04-compressed-model-proof.md) |

## Track C · C12a-C12c : SHOGUN 2 models

Source of truth: [c12-total-war-shogun2-models.md](../deep-dives/c12-total-war-shogun2-models.md) :
[Architecture diagrams §11](../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [c12-total-war-shogun2-models.md §9](../deep-dives/c12-total-war-shogun2-models.md#9-phase-split) | C12a | Packs, LZ4, and DB tables (`WI-C12a-01`, then `WI-C12a-02`) |
| [c12-total-war-shogun2-models.md §9](../deep-dives/c12-total-war-shogun2-models.md#9-phase-split) | C12b | Static models and parts (`WI-C12b-02` in parallel with C12a; `WI-C12b-01` after `WI-C12a-01`, then `WI-C12b-03..05`) |
| [c12-total-war-shogun2-models.md §9](../deep-dives/c12-total-war-shogun2-models.md#9-phase-split) | C12c | Clips, skins, composition, and tables (`WI-C12c-01` after `WI-C12a-01`, then `WI-C12c-02..05`, then `WI-C12c-06`) |

## Track G · G1 : High-DPI drawable & resolution correctness

Source of truth: [deep-dives/g1-high-dpi-resolution.md](../deep-dives/g1-high-dpi-resolution.md) :
[Architecture diagrams §5](../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g1-high-dpi.md](track-g-g1-high-dpi.md) | G1-1…G1-4 | High-DPI drawable & resolution correctness |

## Track G · G2 : Graphics settings & fidelity presets

Source of truth: [deep-dives/g2-graphics-settings-presets.md](../deep-dives/g2-graphics-settings-presets.md) :
[Architecture diagrams §5](../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g2-graphics-settings.md](track-g-g2-graphics-settings.md) | G2-1…G2-3 | Graphics settings & fidelity presets |

## Track G · G3 : Visual regression harness

Source of truth: [deep-dives/g3-visual-regression-harness.md](../deep-dives/g3-visual-regression-harness.md) :
[Architecture diagrams §5](../deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g3-visual-regression.md](track-g-g3-visual-regression.md) | G3-1…G3-4 | Visual regression harness |

## Track G · G4 : Multisample antialiasing

Source of truth: [deep-dives/g4-msaa.md](../deep-dives/g4-msaa.md) :
[Architecture diagrams §5](../deep-dives/g4-msaa.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g4-msaa.md](track-g-g4-msaa.md) | G4-1…G4-2 | Multisample antialiasing |

## Track G · G5 : Cutout & alpha-test clarity

Source of truth: [deep-dives/g5-alpha-test-clarity.md](../deep-dives/g5-alpha-test-clarity.md) :
[Architecture diagrams §5](../deep-dives/g5-alpha-test-clarity.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g5-alpha-test-clarity.md](track-g-g5-alpha-test-clarity.md) | G5-1…G5-2 | Cutout & alpha-test clarity |

## Track G · G6 : Gamma-correct rendering

Source of truth: [deep-dives/g6-gamma-correct-rendering.md](../deep-dives/g6-gamma-correct-rendering.md) :
[Architecture diagrams §5](../deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g6-gamma-correct.md](track-g-g6-gamma-correct.md) | G6-1…G6-2 | Gamma-correct rendering |

## Track G · G7 : Texture filtering & mip quality

Source of truth: [deep-dives/g7-texture-filtering.md](../deep-dives/g7-texture-filtering.md) :
[Architecture diagrams §5](../deep-dives/g7-texture-filtering.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g7-texture-filtering.md](track-g-g7-texture-filtering.md) | G7-1…G7-2 | Texture filtering & mip quality |

## Track G · G8 : Shadow quality tier

Source of truth: [deep-dives/g8-shadow-quality.md](../deep-dives/g8-shadow-quality.md) :
[Architecture diagrams §5](../deep-dives/g8-shadow-quality.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g8-shadow-quality.md](track-g-g8-shadow-quality.md) | G8-1…G8-2 | Shadow quality tier |

## Track G · G9 : Resolution-independent 2D & UI

Source of truth: [deep-dives/g9-resolution-independent-2d.md](../deep-dives/g9-resolution-independent-2d.md) :
[Architecture diagrams §5](../deep-dives/g9-resolution-independent-2d.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g9-resolution-independent-2d.md](track-g-g9-resolution-independent-2d.md) | G9-1…G9-3 | Resolution-independent 2D & UI |

## Track G · G10 : Realistic proportions

Source of truth: [deep-dives/g10-realistic-proportions.md](../deep-dives/g10-realistic-proportions.md) :
[Architecture diagrams §5](../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g10-realistic-proportions.md](track-g-g10-realistic-proportions.md) | G10-1…G10-3 | Realistic proportions |

## Track G · G11 : Terrain impact decals

Source of truth: [deep-dives/g11-terrain-impact-decals.md](../deep-dives/g11-terrain-impact-decals.md) :
[Architecture diagrams §5](../deep-dives/g11-terrain-impact-decals.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g11-terrain-impact-decals.md](track-g-g11-terrain-impact-decals.md) | G11-1…G11-4 | Terrain impact decals |

## Track H · H1-H3 : Map storage

Source of truth: [track-h-storage-formats.md](../track-h-storage-formats.md).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-h-h1-h3-map-storage.md](track-h-h1-h3-map-storage.md) | H1-1…H3-4 | Map storage audit, interchange, package format (WI-H1-01..02, WI-H2-01, WI-H3-01..04) |

## Track H · H2a : Unit-content import layer

Source of truth: [deep-dives/h2a-unit-content-import.md](../deep-dives/h2a-unit-content-import.md) :
[Architecture diagrams](../deep-dives/h2a-unit-content-import.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-h-h2a-unit-content-import.md](track-h-h2a-unit-content-import.md) | H2a-1…H2a-19 | Unit-content import layer |

## Track H · H2b : Metric core units

Source of truth: [deep-dives/h2b-metric-core.md](../deep-dives/h2b-metric-core.md) :
[Architecture diagrams](../deep-dives/h2b-metric-core.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-h-h2b-metric-core.md](track-h-h2b-metric-core.md) | H2b-1…H2b-18 | Metric core units |

## Track M · M1 : Capacity probe

Source of truth: [deep-dives/mass-battle-1k.md](../deep-dives/mass-battle-1k.md) :
[Architecture diagrams §9](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-m-m1-capacity-probe.md](track-m-m1-capacity-probe.md) | M1-1…M1-3 | Generals-only 1000 vs 1000 capacity probe (WI-M1-01..03) |

## Track M · M2 : EE2 combat units

Source of truth: [mass-battle-1k.md](../deep-dives/mass-battle-1k.md) :
[Architecture diagrams §9](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-m-m2-ee2-combat-units.md](track-m-m2-ee2-combat-units.md) | M2-1…M2-4 | EE2 reference roster (WI-M2-01..04) |

## Track M · M3 : Mixed roster normalization

Source of truth: [mass-battle-1k.md](../deep-dives/mass-battle-1k.md) :
[Architecture diagrams §9](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-m-m3-mixed-roster.md](track-m-m3-mixed-roster.md) | M3-1…M3-3 | Model scale, damage classes, points budget and duels (WI-M3-01..03) |

## Track M · M4 : Army AI

Source of truth: [mass-battle-1k.md](../deep-dives/mass-battle-1k.md) :
[Architecture diagrams §9](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-m-m4-army-ai.md](track-m-m4-army-ai.md) | M4-1…M4-3 | Hierarchy scale audit, army-AI probe mode, squad group orders, air commander, AI budget (WI-M4-01..06) |

## Track M · M5 : 1000 vs 1000 battle

Source of truth: [mass-battle-1k.md](../deep-dives/mass-battle-1k.md) :
[Architecture diagrams §9](../deep-dives/mass-battle-1k.md#m5-battle).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-m-m5-mass-battle.md](track-m-m5-mass-battle.md) | M5-1…M5-4 | Army AI side setup, roster profile, army-eliminated outcome, battle scenario, kill accounting, batches and map-bias control, recording and replay, spectator, live frame rate (WI-M5-01..10) |

## Track W · W0 : Charter fixture

Source of truth: [deep-dives/w0-charter-fixture.md](../deep-dives/w0-charter-fixture.md) :
[Architecture diagrams §12](../deep-dives/w0-charter-fixture.md#12-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w0-charter-fixture.md](track-w-w0-charter-fixture.md) | W0-1…W0-9 | Charter fixture (WI-W0-01..09) |

## Track W · W1 : Aggregate sector simulation

Source of truth: [deep-dives/w1-aggregate-sector.md](../deep-dives/w1-aggregate-sector.md) :
[Architecture diagrams §13](../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w1-aggregate-sector.md](track-w-w1-aggregate-sector.md) | W1-1…W1-7 | Aggregate sector simulation (WI-W1-01..15) |

## Track W · W2 : Fidelity promotion and reconciliation

Source of truth: [deep-dives/w2-fidelity-promotion.md](../deep-dives/w2-fidelity-promotion.md) :
[Architecture diagrams §10](../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w2-fidelity-promotion.md](track-w-w2-fidelity-promotion.md) | W2-1…W2-14 | Fidelity promotion (WI-W2-01..14) |

## Track W · W3a : Sedan 1870

Source of truth: [deep-dives/w3a-sedan-1870.md](../deep-dives/w3a-sedan-1870.md) :
[Architecture diagrams §13](../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w3a-sedan-1870.md](track-w-w3a-sedan-1870.md) | W3a-1…W3a-31 | Sedan 1870 (WI-W3a-01..31) |

## Track W · W3b : Somme sector

Source of truth: [deep-dives/w3b-somme-sector.md](../deep-dives/w3b-somme-sector.md) :
[Architecture diagrams §5](../deep-dives/w3b-somme-sector.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w3b-somme-sector.md](track-w-w3b-somme-sector.md) | W3b-1…W3b-6 | Somme sector (WI-W3b-01..06) |

## Track W · W4 : Command and logistics experiment

Source of truth: [deep-dives/w4-command-logistics-experiment.md](../deep-dives/w4-command-logistics-experiment.md) :
[Architecture diagrams §6](../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w4-command-logistics-experiment.md](track-w-w4-command-logistics-experiment.md) | W4-1…W4-6 | Command and logistics experiment (WI-W4-01..06) |

## Track W · W5 : Distributed regional simulation

Source of truth: [deep-dives/w5-distributed-regional-simulation.md](../deep-dives/w5-distributed-regional-simulation.md) :
[Architecture diagrams §7](../deep-dives/w5-distributed-regional-simulation.md#7-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w5-distributed-regional-simulation.md](track-w-w5-distributed-regional-simulation.md) | W5-1…W5-8 | Distributed regional simulation (WI-W5-01..08) |

## Track W · W6 : Long-run campaign experiment

Source of truth: [deep-dives/w6-long-run-campaign.md](../deep-dives/w6-long-run-campaign.md) :
[Architecture diagrams §6](../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-w-w6-long-run-campaign.md](track-w-w6-long-run-campaign.md) | W6-1…W6-8 | Long-run campaign (WI-W6-01..08) |

## Track N · N0-N5 : Multi-unit pathfinding performance

Source of truth: [deep-dives/n-multi-unit-pathfinding.md](../deep-dives/n-multi-unit-pathfinding.md) :
[Architecture diagrams §8](../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-n-pathfinding-performance.md](track-n-pathfinding-performance.md) | N0-1…N5-1 | Path cost baseline, search core, group requests, goal field gate, async order paths, verification (WI-N0-01..WI-N5-01) |

## Track N · N6-N10 : Navigation deadlocks

Source of truth: [deep-dives/n-navigation-deadlocks.md](../deep-dives/n-navigation-deadlocks.md) :
[Architecture diagrams §8](../deep-dives/n-navigation-deadlocks.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-n-pathfinding-performance.md](track-n-pathfinding-performance.md#navigation-deadlocks-n6-n10) | N6-1…N10-1 | Stall attribution, deadlock scenario, path liveness, blocked-unit resolution, destination crowding, verification (WI-N6-01..WI-N10-01) |

## Track N · N11-N14 : Confined passage queueing

Source of truth: [deep-dives/n-confined-passage-queueing.md](../deep-dives/n-confined-passage-queueing.md) :
[Architecture diagrams §8](../deep-dives/n-confined-passage-queueing.md#8-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-n-pathfinding-performance.md](track-n-pathfinding-performance.md#confined-passage-queueing-n11-n14) | N11-1…N14-1 | Passage finder, passage cases, ad hoc queues and lane paths, hold and release, opposing flow, squad entry, verification (WI-N11-01..WI-N14-01) |

## Track N · N15-N20 : Layered navigation

Source of truth: [deep-dives/n-layered-navigation.md](../deep-dives/n-layered-navigation.md) :
[Architecture diagrams §9](../deep-dives/n-layered-navigation.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-n-pathfinding-performance.md](track-n-pathfinding-performance.md#layered-navigation-n15-n20) | N15-1…N20-1 | Navigation plan contract, layered cases, sector graph, strategic planner, corridor main path, navigation groups, width-adaptive formation, crowd routing, steering core and integration, verification (WI-N15-01..WI-N20-01) |

## Track X · X1: Imported-unit hit feedback

Design: [hit feedback](../deep-dives/x1-imported-unit-hit-feedback.md), including grounded [architecture diagrams](../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-x-x1-hit-feedback.md](track-x-x1-hit-feedback.md) | X1-1..X1-7 | Policy and directional-impact audits, explicit damage effects, regressions, impact selection and placement, readable duel capture |

## Track X · X2: Capacity-probe graphics resource fixes

Source of truth: [Track X](../track-x-fixes-tweaks.md). Architecture: [upload lifecycle](../agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-x-x2-capacity-probe-fixes.md](track-x-x2-capacity-probe-fixes.md) | X2-1..X2-2 | Upload lifetime cleanup and measured combat FX/model reuse |

## Track X · X3: Cursor modernization

Source of truth: [Track X](../track-x-fixes-tweaks.md). Architecture: none yet; loader diagrams gated on `WI-X3-01`.

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-x-x3-cursor-modernization.md](track-x-x3-cursor-modernization.md) | X3-1 | Open-source cursor evaluation, comparison examples, loader assessment |

## Track X · X4: ReplaySketch TabControl shell

Source of truth: [Track X](../track-x-fixes-tweaks.md). Architecture: [diagrams](track-x-x4-replaysketch-tabcontrol.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-x-x4-replaysketch-tabcontrol.md](track-x-x4-replaysketch-tabcontrol.md) | X4-1 | `TabControl` tab strip in `MainWindow` |

## Track X · X5: Render culling dispatch

Source of truth: [Track X](../track-x-fixes-tweaks.md). Architecture: [diagrams](track-x-x5-render-culling-dispatch.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-x-x5-render-culling-dispatch.md](track-x-x5-render-culling-dispatch.md) | X5-1 | Measured serial threshold and chunk sizing for `FilterVisible` |
