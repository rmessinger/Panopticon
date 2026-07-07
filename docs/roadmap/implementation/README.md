# Implementation plans

> Audience: human overview of the implementation plan set. Code-level details sit below the
> divider in each plan.

Concrete implementation plans for [implementation-checklist.md](../implementation-checklist.md)
items, ordered by the dependency sequence in the checklist. Each plan cites one checklist item,
the deep dive it derives from, and the implementation scope.

These are design docs, not the checklist itself. The checklist tracks status; these explain how.

## Track P · P0 : ReplaySketch arbitrary-command harness

Source of truth: [deep-dives/playable-baseline-harness.md](../deep-dives/playable-baseline-harness.md) :
[Architecture diagrams §9](../deep-dives/playable-baseline-harness.md#9-architecture-diagrams).

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
