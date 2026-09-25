# Task Card `WI-W0-04`: Add the theater clock and multi-rate scheduler

| Field | Value |
|---|---|
| Card ID | `WI-W0-04` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Implement `TheaterClock`, `WesternFrontClockOptions`, `IWesternFrontSystem`, and `WesternFrontScheduler` with compressed and tactical modes, testable without a host.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §7.
- `src/Panopticon.Simulation/SimulationHost.cs:20-80`: `SimulationHostTickEventArgs` and `TickStarting`.

## Steps

1. Add `TheaterClock` (whole milliseconds) and `WesternFrontClockOptions` (campaign and
   operational seconds; campaign must be a positive whole multiple of operational).
2. Add `IWesternFrontSystem` (`Cadence`, `Step(WesternFrontStepContext)`) and
   `WesternFrontStepContext` (host frame, theater ms, operational tick index, campaign tick index).
3. Add `WesternFrontScheduler` with `Register`, `RequestMode`, `Advance(uint hostFrame, float msPerLogicFrame)`,
   `Mode`, and `Conversions`. Queued mode changes apply only on an operational boundary. Tactical
   time is `entryMs + round(frames * msPerLogicFrame)`.
4. Add `AttachTo(SimulationHost host, float msPerLogicFrame)` subscribing `Advance` to `TickStarting`.
5. Tests: default cadences in compressed mode (operational every frame, campaign every 36 frames);
   tactical mode at 1000/30 ms reaches the first operational boundary at frame 18000 without drift;
   a mid-interval mode request applies at the next boundary and appends one `ClockConversion`;
   campaign systems run before operational systems on shared boundaries.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Clock/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontSchedulerTests.cs`

## Acceptance criteria

- [ ] Every §7 rule holds in tests without constructing a `SimulationHost`.
- [ ] Invalid cadence options throw `ArgumentException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontScheduler"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No gameplay systems. No change to `SimulationHost`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-07`.
