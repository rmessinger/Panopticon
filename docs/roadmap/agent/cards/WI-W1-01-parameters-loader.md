# Task Card `WI-W1-01`: Add Western Front parameters with unit schema

| Field | Value |
|---|---|
| Card ID | `WI-W1-01` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-09` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W0-09` is Done and W0 type names are reconciled.

Add `WesternFrontParameters` JSON loading with `{ value, unit }` validation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3.
- `Directory.Packages.props`: central package pattern.
- `src/Panopticon.Simulation.WesternFront/` parameter and state types from W0.

## Steps

1. Add `System.IO.Hashing` PackageVersion to `Directory.Packages.props` if not already present.
2. Add `WesternFrontParameterValue`, `WesternFrontParameterSchema`, `WesternFrontParameters`, and `WesternFrontParameterLoader`.
3. Encode default W1 synthetic parameters in a checked-in JSON fixture under tests or project resources.
4. Validate missing, extra, non-numeric, and wrong-unit entries.
5. Add tests for successful load and each validation failure.

## Files likely to change

- `Directory.Packages.props`
- `src/Panopticon.Simulation.WesternFront/Parameters/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontParameterTests.cs`

## Acceptance criteria

- [ ] Every numeric JSON value is an object with `value` and `unit`.
- [ ] Loader rejects a valid numeric value with the wrong unit.
- [ ] Defaults include combat, movement, logistics, communication, morale, weather, production, and entrenchment parameters.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontParameter"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No gameplay systems.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
