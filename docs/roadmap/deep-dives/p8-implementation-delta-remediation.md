# Deep Dive: Implementation Delta Remediation (P8)

> Expands phase P8 of [Track P: Playable Baseline](../track-p-playable-baseline.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md).
>
> Audience: human overview of the delta-remediation design. Code references are in the source
> deep dives this phase remediates: [P3 victory/defeat](p3-victory-defeat.md),
> [P2/P5 skirmish AI](p2-p5-skirmish-ai.md), [P4/P6 economy/production/combat](p4-p6-economy-production-combat.md).

Purpose: P3 through P6 are marked `Done`, and that status is accurate for the design surface each
phase set out to build. A May 2026 delta audit compared each phase's implementation against its
deep dive and found each `Done` phase also has specific, disclosed gaps between the design intent
and what actually ships: a scenario matrix that asserts a synthetic stand-in for the real behavior,
a rule that is only test-settable rather than data-driven, an edge case in an otherwise-correct
state machine, a missing UI consumer of an otherwise-correct headless result, or a stub method
where a real implementation belongs. None of these gaps changes a `Done` phase's status; each is a
named, scoped follow-up. This phase collects them in one place instead of re-opening P3-P6.

---

## 1. Design

This phase does not introduce new subsystems. Each sub-phase closes one gap named by the delta
audit, in the existing subsystem the gap was found in:

- P8-1 and P8-2 extend `VictorySystem` (from [P3](p3-victory-defeat.md)) without changing its
  existing outcome semantics.
- P8-3 adds a UI/exit consumer on top of the `GameOutcome` value P3 already exposes; it does not
  change how the outcome is computed.
- P8-4 replaces a synthetic assertion in the P4 scenario matrix ([P4/P6](p4-p6-economy-production-combat.md))
  with one that drives the real `SupplyAIUpdate` gather-cycle state machine.
- P8-5 replaces the P5 scenario matrix's ([P2/P5](p2-p5-skirmish-ai.md)) reflection-built synthetic
  prerequisite chain with real per-faction build trees, and routes the `human` `playerType` cell
  through `OrderProcessor` like the `AI` cell already does.
- P8-6 replaces the P6 command-coverage and scenario matrices' ([P4/P6](p4-p6-economy-production-combat.md))
  `OrderType`-mapping-only assertions with live control-bar dispatch.
- P8-7 implements the `FireWeaponUpdate.Update()` no-op stub and closes the 9 of 15 P6 scenario
  matrix cells that currently assert a disclosed "GAP" message instead of a real observable effect.

## 2. Integration surface

- Touches: `VictorySystem` (P8-1, P8-2), skirmish/game-start match configuration (P8-1), an
  end-of-match UI panel and `Panopticon.Tools.TestHarness`'s exit path (P8-3), `SupplyAIUpdate`-driven
  harness scenarios (P8-4), per-faction `BuildList`/prerequisite data and `OrderProcessor` (P8-5),
  `GeneralsControlBar` dispatch (P8-6), `FireWeaponUpdate` (P8-7).
- No new `GameSystem`, no new save-data version, no new `OrderType`: this phase closes gaps in
  systems P1-P6 already introduced.
- Depends on the systems named above already existing and being `Done`: P8 cannot start before
  P3, P4, P5, and P6.

## 3. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| P8-1. Data-driven victory configuration seam | Expose `VictorySystem`'s frame limit and score metric through a real skirmish/scenario configuration path instead of only `internal` test-settable properties; document `ConditionRule` full pluggability as an explicit remaining gap (Track A's A4 concern) if left out of scope. | Done |
| P8-2. Sudden-death repeat-elimination fix | Close the case where a mutually-eliminated match's `SuddenDeath` status never resolves to `Decided` because the next elimination check window also mutually eliminates the remaining players. | Done |
| P8-3. End-of-match UI and `TestHarness` integration | Add a real UI/exit consumer of `GameOutcome` for interactive play, and migrate `Panopticon.Tools.TestHarness` off its pre-`GameOutcome` ad hoc exit path onto the same result. | Done |
| P8-4. Real gather-cycle harness verification | Replace the P4 scenario matrix's synthetic dump/debit assertion with one that runs a harvester through the real `SupplyAIUpdate` state machine (`SearchingForSupplySource → Approaching → Requesting → Gathering → DumpingSupplies`) via `Scene3D`. | Blocked (locomotor + PartitionCellManager not present in the headless host); precise gap recorded, verified terminals retained |
| P8-5. Real per-faction build trees and production assertions | Replace the P5 scenario matrix's reflection-built synthetic prerequisite chain with real per-faction (`USA`/`GLA`/`China`) build trees, assert an actual unit/upgrade spawn (not only `CanProduceObject`), and route the `human` `playerType` cell through `OrderProcessor` like the `AI` cell. | Done (synthetic per-faction trees are materially distinct; real INI trees still require a full install) |
| P8-6. Control-bar live dispatch verification | Replace the P6 command-coverage matrix's `OrderType`-mapping-only assertion with one that dispatches through `GeneralsControlBar` end to end. | Done at the order layer; the `GeneralsControlBar` button layer (WND Window + asset CommandSet) remains the documented UI gap |
| P8-7. `FireWeaponUpdate` and real observable-effect assertions | Implement the `FireWeaponUpdate.Update()` no-op stub, and close the 9 of 15 P6 scenario matrix cells that currently assert a disclosed "GAP" message rather than a real observable effect. | Done (`FireWeaponUpdate` fires `DamageNugget`→`AttemptDamage`; 6 of 9 matrix cells now real; SpecialPower remains asset-gated) |

## 4. Sequencing & dependencies

- P8 depends on P3, P4, P5, and P6 (it remediates their already-`Done` implementations).
- P8-1 and P8-2 are independent of each other and of P8-4 through P8-7; either can start first.
- P8-3 depends on P8-1/P8-2 only in that it should present whatever outcome states they leave in
  place, but does not require them to land first; it can proceed in parallel.
- P8-4, P8-5, P8-6, and P8-7 are independent of each other; each touches a different subsystem's
  scenario matrix.
- [P7](p7-end-to-end-regression.md) depends on P8: its regression suite exercises the same command
  and scenario-matrix paths P8-4 through P8-7 make real, so P7's invariant checks are only as
  trustworthy as the matrices they build on.

## 5. Related

- [P3 victory/defeat](p3-victory-defeat.md): source of the P8-1/P8-2/P8-3 gaps.
- [P2/P5 skirmish AI](p2-p5-skirmish-ai.md): source of the P8-5 gap (and background for P8-4's
  supply-AI state machine).
- [P4/P6 economy, production, combat](p4-p6-economy-production-combat.md): source of the P8-4,
  P8-6, and P8-7 gaps.
- [P7 end-to-end regression](p7-end-to-end-regression.md): depends on this phase's matrices being real.

## 6. Architecture Diagrams

No new types are introduced by this phase; it closes gaps in the types and subsystems diagrammed
in [P3](p3-victory-defeat.md#7-architecture-diagrams), [P2/P5](p2-p5-skirmish-ai.md), and
[P4/P6](p4-p6-economy-production-combat.md#architecture-diagrams).
