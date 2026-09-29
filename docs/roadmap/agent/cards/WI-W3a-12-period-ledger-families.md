# Task Card `WI-W3a-12`: Add period ledger families

| Field | Value |
|---|---|
| Card ID | `WI-W3a-12` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-08`, `WI-W2-06` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W0-08` and `WI-W2-06` are Done and schema migration types are reconciled.

Raise the Western Front schema for 1870 families, casualty sinks, and capture transfers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §7.4-§7.6 and §11.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4 and §7.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §5-§6.

## Steps

1. Bump the W0 schema version for period family additions.
2. Add `horse` and `mitrailleuse` families while keeping `mg` and `heavy_gun` present at zero.
3. Add `captured` sinks by captor side and an `interned` sink.
4. Model equipment capture as a holding transfer, not a sink.
5. Add conservation tests for horse, mitrailleuse, captured, and interned flows.

## Files likely to change

- Western Front schema and ledger files
- Western Front recording schema files
- Western Front tests

## Acceptance criteria

- [ ] `horse` and `mitrailleuse` conserve exactly.
- [ ] `captured` and `interned` sinks reconstruct from recorded state.
- [ ] `mg` and `heavy_gun` remain in the schema at zero for Sedan.
- [ ] Captured equipment transfers to captor holdings.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PeriodLedgerFamilies"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No combat or surrender policy logic beyond ledger operations.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
