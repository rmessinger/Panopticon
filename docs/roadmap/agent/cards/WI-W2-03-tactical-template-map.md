# Task Card `WI-W2-03`: Map sides and roles to placeholder object definitions

| Field | Value |
|---|---|
| Card ID | `WI-W2-03` |
| Roadmap phase | [Track W · W2](../../track-w-western-front.md): fidelity promotion and reconciliation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W2-02`; all WI-W1 cards; `WI-W0-06` |
| Diagrams | [W2 §10](../../deep-dives/w2-fidelity-promotion.md#10-architecture-diagrams) |

## Goal

Draft: flip to Ready once all WI-W1 cards and gating W0 cards are Done and the named types are reconciled.

Create `TacticalTemplateMap` that resolves W2 soldier definitions from the asset store.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w2-fidelity-promotion.md`: owning W2 design.
- `docs/roadmap/deep-dives/w0-charter-fixture.md`: W0 keys, scheduler, registry, topology, and ledger contracts.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md`: W1 orders, parameters, seeded streams, and combat contracts.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:77-90`: object creation API.
- `src/Panopticon.Simulation/Data/Ini/IniParser.cs:676-697`: object-definition lookup patterns.

## Steps

1. Add `TacticalTemplateMap` with blue and red defaults.
2. Resolve blue infantry to `AmericaInfantryRanger` and red infantry to `ChinaInfantryRedguard`.
3. Validate missing definitions with clear diagnostics.
4. Add tests using the Generals asset store or mocked definitions.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Tactical/`
- `test/Panopticon.Simulation.Tests/WesternFront/`

## Acceptance criteria

- [ ] Blue soldiers resolve to `AmericaInfantryRanger`.
- [ ] Red soldiers resolve to `ChinaInfantryRedguard`.
- [ ] Missing definitions fail before promotion mutates the ledger.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No new Western Front art or object definitions.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
