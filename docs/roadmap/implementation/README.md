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

## Track C · C1 : Format-agnostic import abstraction

Source of truth: [deep-dives/c1-import-abstraction.md](../deep-dives/c1-import-abstraction.md) :
[Architecture diagrams §9](../deep-dives/c1-import-abstraction.md#9-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c1-import-abstraction.md](track-c-c1-import-abstraction.md) | C1 | Format-agnostic import abstraction |

## Track C · C4a : Empire Earth II NIF 10.1 proof

Source of truth:
[deep-dives/ee2-gamebryo-model-storage.md](../deep-dives/ee2-gamebryo-model-storage.md) :
[Architecture diagrams §10](../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-c-c4a-ee2-nif-proof.md](track-c-c4a-ee2-nif-proof.md) | C4a | Empire Earth II NIF 10.1 proof of concept |

## Track G · G1 : High-DPI drawable & resolution correctness

Source of truth: [deep-dives/g1-high-dpi-resolution.md](../deep-dives/g1-high-dpi-resolution.md) :
[Architecture diagrams §5](../deep-dives/g1-high-dpi-resolution.md#5-architecture-diagrams).

| Plan | Sub-phase | Checklist item |
|---|---|---|
| [track-g-g1-high-dpi.md](track-g-g1-high-dpi.md) | G1-1…G1-4 | High-DPI drawable & resolution correctness |
