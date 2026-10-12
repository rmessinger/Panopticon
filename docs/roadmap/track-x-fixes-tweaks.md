# Track X: Miscellaneous Fixes and Tweaks

> Part of the [roadmap](README.md).
> Audience: human.

Open-ended queue for bounded fixes and presentation tweaks. Each phase has its own milestone and dependencies; the track has no terminal completion milestone. New entries receive the next unused phase number. Work already owned by another track stays there.

## Phases

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| X1. Imported-unit hit feedback | Bind explicit target-side damage effects and improve the mixed-source duel capture. | Rebel hits produce visible effects on the tank's Rebel-facing surface; a labeled, uncropped capture shows target impacts and damage on both units, with unchanged combat results. | Done | H2a, P9 |

## Phase resources

| Phase | Design | Diagrams | Cards |
|---|---|---|---|
| X1 | [Hit feedback](deep-dives/x1-imported-unit-hit-feedback.md) | [Architecture](deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) | [WI-X1-01..07](implementation/track-x-x1-hit-feedback.md) |

X1 consumes the published M2 tank row, not the unresolved M2 aircraft or infantry contracts. `WI-M2-03` is a card-level prerequisite. X1 does not close M2 or introduce an EE2 effects importer.
