# Track W · W2: Fidelity promotion and reconciliation

> Audience: human overview above the divider; agent-context detail below it.

Implements the W2 row of [Track W](../track-w-western-front.md). Design source of truth:
[deep-dives/w2-fidelity-promotion.md](../deep-dives/w2-fidelity-promotion.md), including its
[architecture diagrams](../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams).

## Scope

Promote contacted regions into soldier-level tactical `GameObject`s, reconcile them back to
platoon records, keep artillery strategically controlled, and report aggregate and mixed error
against the whole-sector all-tactical reference. W2 uses Generals placeholder infantry definitions
until Track D content exists.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| W2-1 | [`WI-W2-01`](../agent/cards/WI-W2-01-activation-policy-hysteresis.md) | Activation policy interface, references, and dwell. |
| W2-2 | [`WI-W2-02`](../agent/cards/WI-W2-02-transition-records-schema.md) | Transition records and schema additions. |
| W2-3 | [`WI-W2-03`](../agent/cards/WI-W2-03-tactical-template-map.md) | Tactical template map. |
| W2-4 | [`WI-W2-04`](../agent/cards/WI-W2-04-soldier-placement.md) | Soldier placement from posture and trench level. |
| W2-5 | [`WI-W2-05`](../agent/cards/WI-W2-05-promotion-ledger-split.md) | Promotion ledger split. |
| W2-6 | [`WI-W2-06`](../agent/cards/WI-W2-06-soldier-casualty-classification.md) | Soldier casualty classification. |
| W2-7 | [`WI-W2-07`](../agent/cards/WI-W2-07-soldier-ammo-module.md) | Per-soldier SAA module. |
| W2-8 | [`WI-W2-08`](../agent/cards/WI-W2-08-mg-teams.md) | MG team preservation. |
| W2-9 | [`WI-W2-09`](../agent/cards/WI-W2-09-artillery-impacts-active-areas.md) | Strategic artillery impacts into active tactical areas. |
| W2-10 | [`WI-W2-10`](../agent/cards/WI-W2-10-formation-objectives-to-soldier-orders.md) | Formation objectives to A4-3 soldier orders. |
| W2-11 | [`WI-W2-11`](../agent/cards/WI-W2-11-demotion-reconciliation-terrain.md) | Demotion, reconciliation, and terrain fold-in. |
| W2-12 | [`WI-W2-12`](../agent/cards/WI-W2-12-mode-switching-scheduler.md) | Scheduler mode switching. |
| W2-13 | [`WI-W2-13`](../agent/cards/WI-W2-13-all-tactical-reference-run.md) | Whole-sector all-tactical reference run. |
| W2-14 | [`WI-W2-14`](../agent/cards/WI-W2-14-error-report-64-seeds.md) | Aggregate-vs-tactical A2 report over 64 seeds per arm. |

---

## Agent context

## Dependency order

```text
WI-W2-01 ─ WI-W2-02 ─┬─ WI-W2-03 ─ WI-W2-04 ─ WI-W2-05 ─┬─ WI-W2-06 ─┐
                     │                                  ├─ WI-W2-07 ─┼─ WI-W2-11 ─ WI-W2-12 ─ WI-W2-13 ─ WI-W2-14
                     │                                  └─ WI-W2-08 ─┘
                     └──────────────────────────────────── WI-W2-09 ──┘
WI-W2-10 depends on WI-W2-05 and WI-W2-08, then joins before WI-W2-11.
```

Every W2 card is Draft until all WI-W1 cards and the gating W0 cards are Done and the resulting
type names are reconciled.

## Layout

| Folder | Contents |
|---|---|
| `src/Panopticon.Simulation.WesternFront/Activation/` | activation policy interfaces and references |
| `.../Promotion/` | promotion, demotion, transition records, and reconciliation |
| `.../Tactical/` | template map, placement, soldier ammo, casualties, MG teams, artillery impacts |
| `.../Command/` | formation objective translation through A4-3 |
| `.../Experiments/` | all-tactical reference and A2 report wiring |
| `test/Panopticon.Simulation.Tests/WesternFront/` | W2 unit and evidence tests |

## Invariants

- One tactical entity is one soldier `GameObject` with stock health.
- Squads and sections are A4-3 hierarchy groupings, not objects.
- Conserved quantities move only through `WesternFrontLedger`.
- Artillery formations stay operational; tactical areas receive shell impacts through the damage
  path.
- Whole-sector all-tactical is the reference and the primary product.

Code hooks: see the deep dive's
[appendix](../deep-dives/w2-fidelity-promotion.md#appendix-current-state-hooks).
