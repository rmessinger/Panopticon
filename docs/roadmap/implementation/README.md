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

## Track A · A1 : Headless simulation host

Source of truth: [deep-dives/a1-headless-host.md](../deep-dives/a1-headless-host.md) :
[Architecture diagrams §5](../deep-dives/a1-headless-host.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a1-headless-host.md](track-a-a1-headless-host.md) | A1-1…A1-5 | Headless simulation host |

## Track A · A1-6 : Operational recording and seeded reconstruction

Source of truth:
[deep-dives/a1-6-operational-recording.md](../deep-dives/a1-6-operational-recording.md) :
[Architecture diagrams §10](../deep-dives/a1-6-operational-recording.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-a-a1-6-operational-recording.md](track-a-a1-6-operational-recording.md) | A1-6a…A1-6g | Operational recording and seeded reconstruction (WI-A1-13..24) |

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

## Track B · B1 : Benchmarking

Source of truth: [deep-dives/b1-benchmarking.md](../deep-dives/b1-benchmarking.md) :
[Architecture diagrams §](../deep-dives/b1-benchmarking.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b1-benchmarking.md](track-b-b1-benchmarking.md) | B1-1…B1-7 | Benchmarking utilities (WI-B1-01..07) |

## Track B · B4a : Larger maps

Source of truth: [deep-dives/b4a-larger-maps.md](../deep-dives/b4a-larger-maps.md) :
[Architecture diagrams §](../deep-dives/b4a-larger-maps.md#architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b4a-larger-maps.md](track-b-b4a-larger-maps.md) | B4a-1…B4a-6 | Larger maps (WI-B4a-01..11) |

## Track B · B4b : Geospatial map generation

Source of truth:
[deep-dives/b4b-geospatial-map-generation.md](../deep-dives/b4b-geospatial-map-generation.md) :
[Architecture diagrams §10](../deep-dives/b4b-geospatial-map-generation.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-b-b4b-geospatial.md](track-b-b4b-geospatial.md) | B4b-1…B4b-6b | Geospatial map generation (WI-B4b-01..24) |

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

## Track G · G1 : High-DPI drawable & resolution correctness

Source of truth: [deep-dives/g1-high-dpi-resolution.md](../deep-dives/g1-high-dpi-resolution.md) :
[Architecture diagrams §5](../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g1-high-dpi.md](track-g-g1-high-dpi.md) | G1-1…G1-4 | High-DPI drawable & resolution correctness |

## Track H · H1-H3 : Map storage

Source of truth: [track-h-storage-formats.md](../track-h-storage-formats.md).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-h-h1-h3-map-storage.md](track-h-h1-h3-map-storage.md) | H1-1…H3-4 | Map storage audit, interchange, package format (WI-H1-01..02, WI-H2-01, WI-H3-01..04) |

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
