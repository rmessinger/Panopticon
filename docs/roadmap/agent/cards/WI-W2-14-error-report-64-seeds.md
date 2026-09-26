# Task Card `WI-W2-14`: Report aggregate error against all-tactical reference

| Field | Value |
|---|---|
| Card ID | `WI-W2-14` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W2-13`; all WI-W1 cards; A2 experiment cards Done |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Add the A2 report comparing aggregate and mixed runs against the all-tactical reference over 64 seeds per arm.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.

## Steps

1. Define the W2 experiment arms: all-tactical reference, contact mixed, aggregate-only.
2. Run or script 64 seeds per arm through A2.
3. Report casualty, ground, SAA, artillery, dwell, and terrain distributions.
4. Store the declared W2 error envelope in the run output.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Experiments/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Report uses 64 seeds per arm.
- [ ] Mixed and aggregate arms compare against all-tactical reference.
- [ ] The report declares error distributions without setting an unsupported permanent threshold.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not reduce seed count for speed without marking the card Blocked.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
