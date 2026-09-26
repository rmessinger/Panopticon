# Task Card `WI-W0-02`: Add the Western Front state schema

| Field | Value |
|---|---|
| Card ID | `WI-W0-02` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01`, `WI-A4-42` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add `WesternFrontSchema` version 1 matching the W0 state vector.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §3.
- `src/Panopticon.Simulation.WesternFront/` types from `WI-W0-01`.

## Steps

1. Add schema-building code for theater, pool, ledger, formation, depot, link, region, aggregate, and soldier kinds.
2. Use exact units, quantity types, conservation roles, and families from §4.
3. Ensure formation references include parent and region, and soldier references include formation and cell.
4. Add tests that call `Validate`, inspect every kind, and assert conserved fields are integer-only.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontSchemaTests.cs`

## Acceptance criteria

- [ ] Schema version is 1 and domain id is `western-front`.
- [ ] Echelon values include Company 5 and Platoon 6.
- [ ] Tactical layer kind is `soldier`, not `squad`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontSchema"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No capture, ledger, or recording code.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
