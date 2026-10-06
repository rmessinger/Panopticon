# Task Card `WI-H2a-02`: Ground the import contract and architecture

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-02` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md): unit-content import layer |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H2a-01` |
| Diagrams | Deliverable populates [Architecture diagrams](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Turn the audited loading path into a reviewed contract and grounded implementation boundaries.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`, including the completed evidence table and its cited code ranges.
- `docs/roadmap/open-questions.md`: multi-source unit importing decision.
- `docs/roadmap/agent/cards/WI-H2a-03-interchange-contracts.md` through `WI-H2a-17-multi-source-validation.md`.
- `docs/roadmap/agent/MAINTAINING.md`: design and card-readiness gates.

## Steps

1. Specify adapter inputs/results, engine-owned definitions, omission/override semantics, dependency kinds, capabilities, diagnostics, and publication/lifetime rules.
2. Map every audited native module family to interchange/materialization or an explicit blocker; retain Generals legacy coverage without source parser payloads.
3. Fix the reference roster, selected ability/production/upgrade cases, EE2 dependency closure, units/conversions, and failure fixtures.
4. Populate class and success/failure sequence diagrams with audited call sites and clearly labelled proposed types.
5. Reconcile Draft card paths, tests, and prerequisites; split oversized module work. Record a staging/PR grouping plan and link an implementation-plan entry before source work becomes Ready.

## Files likely to change

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`.
- `docs/roadmap/implementation/README.md` and a new H2a implementation plan.
- H2a cards and their roadmap/checklist/resource entries.
- `docs/roadmap/open-questions.md`, `seams.md` for reviewed contract decisions.

## Acceptance criteria

- [ ] All required fields and native coverage have explicit owners; no opaque unsupported-mechanic passthrough.
- [ ] Class and sequence diagrams cover validation, construction failure, publication, and unload.
- [ ] The EE2 slice is feasible with actual Track C capabilities, or an explicit blocker is recorded.
- [ ] Source-edit cards have bounded, reviewed contracts before promotion to Ready.

## Validation commands

Render the new diagrams with Mermaid CLI using the repository roadmap validation recipe; check links and file citations. No build required.

## Out of scope / guardrails

No implementation or new source-game mechanics. Unsettled architectural choices require review; block rather than guess. If design exceeds one bounded pass, split investigation cards.

## On completion

- Set Status to Done or Blocked and update H2a-2.
- Promote only reconciled cards whose prerequisites are Done; update the phase resource gate.
- Append the design result and staging plan to `.exchange`.
