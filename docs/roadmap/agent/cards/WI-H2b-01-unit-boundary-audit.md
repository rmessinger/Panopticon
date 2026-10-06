# Task Card `WI-H2b-01`: Audit the merged H2a unit system

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-01` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | Complete H2a group merged, including outstanding registration work |
| Diagrams | Supplies evidence for the [architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams) |

## Goal

Ground the dimensional inventory in final merged H2a code. Blocked pending that merge.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2b-metric-core.md`: audit deliverables.
- `docs/roadmap/deep-dives/h2a-unit-content-import.md`: H2a ownership.
- `src/Panopticon.Simulation/Content/Importing/Units/Quantity.cs` and `Materialization/UnitConversionPolicy.cs` beneath that directory.
- Exact source paths located by read-only searches for the listed audit surfaces.

## Steps

1. Verify and record the final H2a merge commit, card completion, and unresolved blockers. Do not mark H2a complete on its behalf.
2. Produce the field-level inventory specified by the deep dive. Classify by semantics, not syntax; include writers and native bindings.
3. Identify exact source and test scopes for subsequent cards. Partition oversized audit work into read-only follow-ups before continuing.

## Files likely to change

- `docs/roadmap/deep-dives/h2b-metric-inventory.md` (new, audience line required).
- This card and `.exchange`.

## Acceptance criteria

- [ ] Merged H2a commit and file:line dimensional evidence are cited; every surface has an owner or explicit nonapplicability finding.
- [ ] Wire formats, source scales, native bindings, ambiguous quantities, and oversized tasks are listed without guesses.

## Validation commands

Read-only searches; check inventory citations against the recorded commit.

## Out of scope / guardrails

No engine edits. Unknown source units block conversion.

## On completion

Set Status, reconcile the H2b checklist, and append findings to `.exchange`. Hand exact scopes to card 02.
