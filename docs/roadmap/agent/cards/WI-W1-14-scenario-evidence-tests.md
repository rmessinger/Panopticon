# Task Card `WI-W1-14`: Add W1 scenario and evidence tests

| Field | Value |
|---|---|
| Card ID | `WI-W1-14` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W1-04`, `WI-W1-06`, `WI-W1-07`, `WI-W1-09`, `WI-W1-12`, `WI-W1-13` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once W1 systems are Done and type names are reconciled.

Build the seven-day offensive scenario and prove W1 milestone evidence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §10-§11.
- W0 scenario from `WI-W0-06`.
- All W1 systems from prerequisite cards.

## Steps

1. Extend `WesternFrontScenarioOptions` with initial offensive intent and W1 parameters.
2. Register all W1 systems in documented order.
3. Run 10080 compressed host frames or stop on front-line collapse.
4. Assert conservation, command metrics, logistics metrics, reconstruction at sampled frames, and same-seed equality for both combat variants.
5. Preserve determinism-failure artifacts under a repository-local diagnostics folder and report before fixing if same-seed equality fails.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Scenario/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontAggregateSectorTests.cs`

## Acceptance criteria

- [ ] Seven theater days equal 10080 compressed host frames.
- [ ] Both combat variants pass same-seed equality or preserve artifacts per determinism policy.
- [ ] Reconstruction includes front, formation, command, contact, and logistics records.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontAggregateSector"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No A2 report formatting.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
