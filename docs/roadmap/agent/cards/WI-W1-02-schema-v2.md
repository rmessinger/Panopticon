# Task Card `WI-W1-02`: Extend domain state schema to version 2

| Field | Value |
|---|---|
| Card ID | `WI-W1-02` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-01`, `WI-W0-09` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-01` and `WI-W0-09` are Done and schema types are reconciled.

Add W1 message, commlink, contact, and shipment records.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §9.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4.
- `src/Panopticon.Simulation.WesternFront/State/` from W0.

## Steps

1. Raise the Western Front schema version to 2 behind an explicit W1 schema builder.
2. Add state records for `message`, `commlink`, `contact`, and `shipment`.
3. Mark `shipment.amount` as a conserved Holding in the commodity family.
4. Convert W0 link `in_transit_*` fields to derived metrics when schema v2 is active.
5. Add tests for schema validation and record vector lengths.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontSchemaV2Tests.cs`

## Acceptance criteria

- [ ] Schema version 2 contains exactly the four W1 record kinds.
- [ ] `shipment` contributes to conservation balances.
- [ ] Delivered and expired message state can be omitted after one campaign tick.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontSchemaV2"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No systems or message delivery.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
