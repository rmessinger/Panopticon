# Task Card `WI-H2a-18`: Select and probe the EE2 ground-combat unit

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-18` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md): unit-content import layer |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H2a-02` |
| Diagrams | Deliverable records the selection in the [EE2 feasibility finding](../../deep-dives/h2a-unit-content-import.md#ee2-feasibility-finding) |

## Goal

Pick one real EE2 ground combat unit whose stats and four visual states are provable with the existing Track C importers, or record the missing capability.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`: EE2 feasibility finding.
- `docs/system-investigations/EE2_UNIT_VISUAL_PIPELINE.md`.
- `docs/roadmap/deep-dives/c4d-ee2-skinned-animation.md`: skin and animation limits.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`: operator install and archive mounting.

## Steps

1. List `UnitType` records under `db/Units` (base and EE2X) with an attack ability that deals damage and resolvable `HitPoints`, following `parent` chains.
2. For each candidate, resolve the bound `UnitModel` and check for Idle, Walk or Run, Attack1, and Death states.
3. Probe the candidate's `nifcache` and `kf` files against the C4d importer limits without committing assets.
4. Record the selected unit, its source files, authored stats with units, and the dependency closure; or the first failing capability and a reviewed follow-up.

## Files likely to change

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`: selection or blocker.
- This card and the matching checklist item.

## Acceptance criteria

- [ ] One unit has resolvable health, damage, range, reload, and four bound states, or a blocker names the missing Track C capability.
- [ ] The dependency closure and source layers (base, EE2X, patch) are listed.
- [ ] No other game's values are substituted.

## Validation commands

Read-only probe against the operator install; no build required unless a harness probe is run.

## Out of scope / guardrails

No adapter code, no asset commits, no new importer capability.

## On completion

- Set Status to Done or Blocked and update H2a-18 in the checklist.
- Update `WI-H2a-15` to Ready when a unit is selected.
- Append the result to `.exchange`.
