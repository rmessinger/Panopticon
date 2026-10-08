# Task Card `WI-M1-02`: Capacity measurements through B1

| Field | Value |
|---|---|
| Card ID | `WI-M1-02` |
| Roadmap phase | [M1](../../track-m-mass-battle.md): capacity probe |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-M1-01` |
| Diagrams | [M1 capacity probe flow](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Record the WI-M1-01 engagement with the B1 measurement stack: tick cost, frame rate (headless tick wall time and windowed render loop time), memory, GC, and path requests per second over the engagement, written as a `BenchmarkRunRecord` plus an engagement summary.

## Context to read (and only this)

- `src/Panopticon.Benchmarks.Simulation/BenchmarkMetricRecorder.cs`: tick/system span sampling to attach.
- `src/Panopticon.Benchmarks.Simulation/BenchmarkRunRecord.cs`, `BenchmarkReportWriter.cs`, `BenchmarkStatistics.cs`: record shape, JSON/CSV writers, median and bootstrap statistics.
- `src/Panopticon.Simulation/Navigation/Navigation.cs` line 76: `CalculatePath`, the only path funnel; the request counter goes here.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs` (from WI-M1-01): the loop to instrument.

## Steps

1. Add a monotonic path-request counter incremented in `Navigation.CalculatePath`; expose the current count for polling.
2. Reference `Panopticon.Benchmarks.Simulation` from the TestHarness project and attach `BenchmarkMetricRecorder` for the measured window of the probe run.
3. Sample the path counter per tick into the record (delta per sample window), and derive `path.requestsPerSecond` statistics; headless frame rate is `tick.wallMs` inverted, windowed frame rate is measured around the render step loop.
4. Write `BenchmarkRunRecord` JSON and CSV (B1 writers verbatim, scenario id `m1.capacity-probe`) beside the engagement report.
5. Keep the measurement window scoped: setup, spawn, and settle are unmeasured (mirroring B1 warmup); measurement spans the general attack to the frame budget or army destruction.

## Files likely to change

- `src/Panopticon.Simulation/Navigation/Navigation.cs`
- `src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `test/Panopticon.Simulation.Tests/Benchmarks/NavigationPathRequestCounterTests.cs` (new, install-free)

## Acceptance criteria

- [x] The B1 record for a probe run contains tick samples (wall ms, allocations, GC, memory), system span samples, and path-request samples over the measured engagement.
  - Full-scale headless run: 9000 tick samples, 36000 system span samples, per-tick path-request deltas summarized as `path.requestsPerTick` / `path.requestsPerSecond`.
- [x] `tick.wallMs`, derived frame rate, memory, and `path.requestsPerSecond` statistics appear in the record's statistics list.
- [x] JSON is written by `BenchmarkReportWriter` (schema `b1.run/1`) and CSV derives from the same record.
- [x] Path counter counts only `CalculatePath` entries; no per-call allocation on the hot path.
  - `Interlocked.Increment` on a static long; covered by install-free `NavigationPathRequestCounterTests`.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario MassBattleCapacityProbe
dotnet test src --filter "FullyQualifiedName~NavigationPathRequestCounter"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No ScenarioMatrix sweep (B1-07 consumer work), no capacity_estimate model, no bandwidth or GPU probes: the closeout scopes M1 to the four measured quantities.
- No changes to B1 writer formats; the record is extended only through its existing lists.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none; WI-M1-03 reads these records.
