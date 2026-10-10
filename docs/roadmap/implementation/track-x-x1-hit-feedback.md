# X1: Hit-feedback card sequence

> Audience: human + agent.

Design and architecture: [X1 hit feedback](../deep-dives/x1-imported-unit-hit-feedback.md).
Phase and dependencies: [Track X](../track-x-fixes-tweaks.md).

| Card | Deliverable | Initial status |
|---|---|---|
| [WI-X1-01](../agent/cards/WI-X1-01-hit-feedback-policy.md) | Source/runtime audit and bounded compatibility policy | Ready |
| [WI-X1-02](../agent/cards/WI-X1-02-imported-damage-fx-binding.md) | Opt-in imported-vehicle damage-effect binding | Draft |
| [WI-X1-03](../agent/cards/WI-X1-03-hit-feedback-regressions.md) | Policy, lifecycle, and combat regression tests | Draft |
| [WI-X1-04](../agent/cards/WI-X1-04-readable-duel-capture.md) | Readable labeled duel and rendered impact evidence | Draft |

One commit per card; leave any PR open for operator merge. Reconcile Draft cards against preceding evidence before execution. Stop and preserve artifacts on determinism/golden-output deviations, pending operator determination.

---

## Audit and results

Pending `WI-X1-01`: exact mapping fields, policy scope, reference lookup, and tests. Pending final capture: effect timing, both actors' damage results, complete camera bounds, and shared artifact paths.
