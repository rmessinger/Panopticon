# Task Card `WI-B1-06`: Add seeded reference benchmark scenarios

| Field | Value |
|---|---|
| Card ID | `WI-B1-06` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-6 reference scenarios |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B1-02`, `WI-B1-04`, `WI-B1-05` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Add the initial seeded scenario suite, including the 10k-unit scale anchor.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b1-benchmarking.md`.
- `docs/roadmap/deep-dives/a1-headless-host.md`.
- `docs/roadmap/deep-dives/a2-reproducibility.md`.
- `src/Panopticon.Simulation/SimulationHost.cs`.
- `src/Panopticon.Simulation/RunOutput.cs`.

## Steps

1. Add the named B1 types from the deep dive.
2. Thread them through `SimulationBenchmarkRunner`.
3. Add install-free tests for the new behavior.
4. Keep JSON and CSV output compatible with earlier B1 cards.

## Files likely to change

- `src/Panopticon.Benchmarks.Simulation/`
- Benchmark tests

## Result

Landed 2026-10-04. Four seeded reference scenarios beside `b1.empty-host`, all through the
shared `BootSkirmish` boot (passive owners, inert victory, AI nulled) plus
`BenchmarkUnitSpawner` (direct-spawn mirroring the validation ground) and
`BenchmarkPlayerSettings` (team parameters make sides allied or hostile):

- `b1.1k-move`: 1000 Paladins in a corner grid on Dark Mountain, all ordered across the map.
  Proven: 300 frames, tick median 17.7 ms CI [17.3, 18.1], gameLogic 9.7 ms, 4.7 MB allocs per
  tick.
- `b1.mixed-combat`: 300 vs 300 Rangers on hostile teams, ranks 80 units apart (inside the
  Ranger's 100-unit range), paired weapon targets re-armed every 30 frames, 30 HP units for
  fast kills. Proven: 300 frames with sustained churn (both sides 300 down to 216-250), tick
  median 10-13 ms. Two engine findings recorded: turret auto-acquire overrides scripted
  pairings (low victim HP gives fast kills instead), and combat triggers recurring multi-second
  `scene3d` spans in some runs (first combat-workload bottleneck signal; B2+ owns it).
- `b1.path-query`: 200 Rangers on Twilight Flame, re-targeted to seeded random far points
  every 150 frames. Proven: 450 frames, tick median 3.9 ms, gameLogic 2.8 ms.
- `b1.10k-scale-anchor`: 10000 Rangers centered on Twilight Flame, every tenth unit crossing
  the map. Proven: 60 frames, tick median 150.5 ms (gameLogic 131.7 ms, partition 12.2 ms),
  p90 155.7 ms: 4.5x over the 33 ms real-time frame budget on the dev host.
- Tests: 20 install-free across `SimulationBenchmarkScenarioTests` (five ids, workload
  constants, unknown-id failure); all prior B1 test groups still green (49 total).

## Acceptance criteria

- [x] The new behavior is covered by tests.
- [x] Reports retain scenario id, seed, and measured metric values.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~BenchmarkScenario"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No optimization and no simulation detail reduction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
