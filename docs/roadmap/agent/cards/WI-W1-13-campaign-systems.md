# Task Card `WI-W1-13`: Add weather, production, and entrenchment systems

| Field | Value |
|---|---|
| Card ID | `WI-W1-13` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-01`, `WI-W0-08` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-01` and `WI-W0-08` are Done.

Add campaign-cadence systems for weather, production, and entrenchment.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §2 and §8.3.
- W1 parameters from `WI-W1-01`.
- W0 ledger and region state.

## Steps

1. Add `WeatherSystem` using seeded streams and parameterized transition probabilities.
2. Add `ProductionSystem` producing personnel, artillery ammo, and SAA into pools.
3. Add `EntrenchmentSystem` increasing formation entrenchment and region trench or wire state.
4. Register all three at campaign cadence before operational systems.
5. Add tests proving they run every 60 compressed frames by default.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Systems/WeatherSystem.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/ProductionSystem.cs`
- `src/Panopticon.Simulation.WesternFront/Systems/EntrenchmentSystem.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/CampaignSystemsTests.cs`

## Acceptance criteria

- [ ] Production uses `WesternFrontLedger.Produce`.
- [ ] Weather and entrenchment change state only on campaign ticks.
- [ ] Default cadence updates after 60 compressed host frames.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CampaignSystems"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No operational combat changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
