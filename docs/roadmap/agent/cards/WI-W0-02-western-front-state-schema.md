# Task Card `WI-W0-02`: Declare the Western Front state schema

| Field | Value |
|---|---|
| Card ID | `WI-W0-02` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01`, `WI-A4-42` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Declare the §4 state vector as one A4-4 `DomainStateSchema` (version 1).

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §4.
- `src/Panopticon.Simulation/Simulation/State/DomainStateSchema.cs` (from `WI-A4-42`).

## Steps

1. Add `WesternFrontSchema.Current`: domain id `western-front`, version 1, kinds `theater`, `pool`,
   `ledger`, `formation`, `depot`, `link`, `region`, `aggregate`, and `squad` with exactly the §4
   fields, units, types, roles, families, layers, and reference names.
2. Add `WesternFrontFamilies` constants for the six conserved families.
3. Add per-kind field-index constants (for example `FormationFields.Personnel`) so adapters never
   look up fields by string at capture time.
4. Add `WesternFrontSchemaTests`: `Validate()` passes; every field has unit and layer; every
   conserved family has at least one `Holding` and one `Sink` field; kind layers match §4.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/State/WesternFrontSchema.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontSchemaTests.cs`

## Acceptance criteria

- [ ] The schema validates.
- [ ] Field-index constants match declaration order (asserted by test).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontSchema"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No capture code. A field beyond §4 needs a deep-dive change first.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-05`.
