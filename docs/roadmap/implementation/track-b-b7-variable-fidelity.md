# Track B · B7: Variable simulation fidelity

> Audience: human overview above the divider; agent-context detail below it.

Implements the carded scope of the B7 row of [Track B](../track-b-scale.md). Design source of
truth: [deep-dives/b7-variable-fidelity.md](../deep-dives/b7-variable-fidelity.md), including its
[architecture diagrams](../deep-dives/b7-variable-fidelity.md#9-architecture-diagrams).

## Scope

Add a simulation-side fidelity model and `FidelityRegistry`, define replicated-state policy
inputs, generalize W2 group aggregation into a reusable contract after W2 lands, and expose
fidelity states to A5 visualization and A2 error reports. Temporal LOD, simplified
behavior/physics, and precision reduction are not carded under the current detail-first stance.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| B7-1 | [`WI-B7-01`](../agent/cards/WI-B7-01-fidelity-model-registry.md) | Fidelity model and registry. |
| B7-1 | [`WI-B7-02`](../agent/cards/WI-B7-02-replicated-policy-input-contract.md) | Replicated-state policy input contract. |
| B7-1 | [`WI-B7-03`](../agent/cards/WI-B7-03-registry-domain-projection.md) | Registry projection into domain state and metrics. |
| B7-4 | [`WI-B7-04`](../agent/cards/WI-B7-04-group-aggregation-contract.md) | Reusable group aggregation contract after W2. |
| B7-4 | [`WI-B7-05`](../agent/cards/WI-B7-05-group-aggregation-error-hooks.md) | A2 error hooks for group aggregation. |
| B7-6 | [`WI-B7-06`](../agent/cards/WI-B7-06-fidelity-visualization-bridge.md) | A5-3 visualization bridge. |
| B7-6 | [`WI-B7-07`](../agent/cards/WI-B7-07-fidelity-error-report.md) | Fidelity transition and error report. |

---

## Agent context

## Dependency order

```text
WI-B7-01 ─ WI-B7-02 ─ WI-B7-03 ─┬─ WI-B7-06 ─ WI-B7-07
                                └─ WI-B7-04 ─ WI-B7-05 ─┘
```

`WI-B7-04` waits for all WI-W2 cards. `WI-B7-06` waits for `WI-A5-19`, currently Blocked.

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Simulation/Fidelity/` | fidelity levels, state, registry, policy contracts, projections |
| `src/Panopticon.Simulation/Fidelity/Aggregation/` | reusable group aggregation contracts |
| `src/Panopticon.Simulation/Fidelity/Reports/` | error and transition report models |
| `test/Panopticon.Simulation.Tests/Fidelity/` | registry, policy, projection, aggregation, and report tests |

## Invariants

- Fidelity is keyed by `ObjectId` and records hysteresis state.
- Policy inputs for shared and distributed runs come from replicated simulation state only.
- Simulation fidelity is separate from presentation LOD.
- Group aggregation preserves declared quantities and writes transition records.
- B7 does not add detail-reducing techniques unless the open-question stance changes.
