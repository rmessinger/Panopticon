# Task Card `WI-W0-04`: Add the theater clock and scheduler

| Field | Value |
|---|---|
| Card ID | `WI-W0-04` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add `TheaterClock` and `WesternFrontScheduler` with W0 cadence and mode conversion rules.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §7.
- `src/Panopticon.Simulation/SimulationHost.cs:68-159`: host timing events.
- `src/Panopticon.Simulation.WesternFront/` enums from `WI-W0-01`.

## Steps

1. Add `WesternFrontClockOptions` with defaults operational 60 s and campaign 3600 s.
2. Add `TheaterClock`, `ClockConversion`, `IWesternFrontSystem`, and `WesternFrontScheduler`.
3. Advance compressed mode by 60000 ms per host frame.
4. In tactical mode compute time as `entryMs + round(tacticalFrames * MsPerLogicFrame)`.
5. Queue mode changes and apply them only on operational boundaries.
6. Add scheduler unit tests with default and shortened cadence options.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Clock/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontSchedulerTests.cs`

## Acceptance criteria

- [ ] Default compressed mode produces one campaign tick every 60 host frames.
- [ ] Shortened 10 s operational cadence produces one tactical operational boundary after 300 30-fps frames.
- [ ] Both mode conversions are recorded on operational boundaries.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontScheduler"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No gameplay systems.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
