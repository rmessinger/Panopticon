# Task Card `WI-W3a-26`: Assemble the Sedan scenario

| Field | Value |
|---|---|
| Card ID | `WI-W3a-26` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-11`, `WI-W3a-06`, `WI-W3a-22` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-11`, `WI-W3a-06`, and `WI-W3a-22` are Done.

Create the 31 August 00:00 Sedan scenario state from topology, order of battle, and orders timeline.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §3-§5 and §8.2.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3-§6.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §3-§4.

## Steps

1. Load the validated Sedan generated payload and build topology.
2. Expand the order of battle to platoon floor and place formations in assembly-area cells.
3. Initialize headquarters, depots, communication links, ledger holdings, and scenario clock.
4. Attach the orders timeline and environment schedule.
5. Add a smoke test that validates initial state and resolvable links.

## Files likely to change

- Western Front Sedan scenario files
- `src/Panopticon.Simulation.WesternFront/Data/Sedan/`
- Western Front scenario tests

## Acceptance criteria

- [ ] 31 August 00:00 state places every formation in its assembly area.
- [ ] All formation, topology, communication, and order keys resolve.
- [ ] Initial ledger totals match the loaded datasets.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SedanScenarioAssembly"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No 60 h reference run in this card.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
