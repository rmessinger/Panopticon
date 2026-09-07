# Task Card `WI-A4-06`: Design simulation-domain contracts

| Field | Value |
|---|---|
| Card ID | `WI-A4-06` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-05` |
| Diagrams | This card creates the populated A4-1 diagrams. |

## Goal

Define the exact domain contracts used by the existing RTS runtime and the minimal transport/graph
adapter, including lifecycle and host wiring.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a4-simulation-domain-model.md`.
- `docs/roadmap/track-a-foundations.md`: A4 notes and A4-1 acceptance criterion.
- `docs/roadmap/seams.md`.
- `docs/roadmap/deep-dives/a1-headless-host.md` §5.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §6.

## Steps

1. Create `docs/roadmap/deep-dives/a4-simulation-domain-model.md`.
2. Specify contracts for entity enumeration, topology keys and adjacency, interaction locality and
   authority, metrics/probes, recording buckets, visualization projection, and fidelity hooks.
3. Specify ownership and lifecycle through `SimulationHost`; reuse the `GameObject`/module runtime.
4. Define the deliberately minimal transport/graph validation scenario.
5. Add grounded class and sequence diagrams for both RTS and graph-domain runs.
6. Link the deep dive and diagrams from Track A and the roadmap README.

## Files likely to change

- `docs/roadmap/deep-dives/a4-simulation-domain-model.md` (new)
- `docs/roadmap/track-a-foundations.md`
- `docs/roadmap/README.md`
- `docs/roadmap/implementation/track-a-a4-simulation-domain-model.md`

## Acceptance criteria

- [ ] Every contract has one owner and a concrete RTS mapping.
- [ ] The graph adapter shares the entity runtime and host.
- [ ] Class and sequence diagrams contain no placeholder types lacking defined responsibilities.
- [ ] The implementation plan gives `WI-A4-07` exact symbols to add.

## Validation commands

```sh
cd docs/roadmap
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/a4-simulation-domain-model.md -o /tmp/a4-domain.svg
rm -f /tmp/a4-domain*.svg
```

## Out of scope / guardrails

Do not implement code or expand the transport domain into a complete game.

## On completion

- Set Status above to `Done` or `Blocked`.
- Remove the A4-1 diagram blocker from dependent cards.
- Append a result line to `.exchange`.
