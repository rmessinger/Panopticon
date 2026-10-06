# Task Card `WI-H2b-02`: Design the metric cutover

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-02` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H2b-01` |
| Diagrams | Populates the [architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams) |

## Goal

Define bounded implementation scopes and explicit compatibility and release gates.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2b-metric-core.md` and card 01's inventory.
- Exact adapters, serializers, consumers, and tests cited by that inventory.
- `docs/roadmap/agent/MAINTAINING.md` and `task-card-template.md` in that directory.

## Steps

1. Produce the execution design required by the scaffold: normalization ownership, quantity migration, native bindings, schema policies, dimensional unknowns, and tolerances.
2. Populate grounded class and read/write sequence diagrams in the owning deep dive.
3. Pin exact files, fields, targeted commands, and expectations in every source card before Ready. Split scopes larger than S and propagate follow-ups to all roadmap views.
4. Define staging groups and activation order; capture final-H2a baseline evidence before runtime cutover.

## Files likely to change

- `docs/roadmap/deep-dives/h2b-metric-execution.md` (new).
- `docs/roadmap/deep-dives/h2b-metric-core.md`.
- H2b cards and matching roadmap entries; `.exchange`.

## Acceptance criteria

- [ ] Every field has one normalization owner; metric native definitions cannot double-convert through H2a.
- [ ] Compatibility matrix covers replays, network, saves, checkpoints, runs, and scenarios; old units cannot be silently reinterpreted.
- [ ] Diagrams are grounded; promoted cards have exact scopes, targeted tests, and S effort.

## Validation commands

Documentation link, dependency, and Mermaid checks.

## Out of scope / guardrails

No implementation or baseline approval. Escalate unsettled design choices.

## On completion

Set Status, reconcile checklist/resources and split cards, and append decisions to `.exchange`.
