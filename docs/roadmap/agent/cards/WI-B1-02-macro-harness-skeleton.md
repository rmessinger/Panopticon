# Task Card `WI-B1-02`: Create the macro benchmark harness skeleton

| Field | Value |
|---|---|
| Card ID | `WI-B1-02` |
| Roadmap phase | [Track B · B1](../../track-b-scale.md): B1-2 macro harness |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B1-01` |
| Diagrams | [B1 diagrams](../../deep-dives/b1-benchmarking.md#architecture-diagrams) |

## Goal

Create a B1 benchmark runner project and CLI skeleton around `SimulationHost` with one empty-host scenario.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b1-benchmarking.md` §2, §7, and diagrams.
- `src/Panopticon.Benchmarks.DataStructures/Panopticon.Benchmarks.DataStructures.csproj`.
- `src/Panopticon.Benchmarks.DataStructures/Program.cs`.
- `src/Panopticon.Simulation/SimulationHost.cs`.

## Steps

1. Create `src/Panopticon.Benchmarks.Simulation` and add it to the solution.
2. Add `SimulationBenchmarkPlan`, `SimulationBenchmarkScenario`, and `SimulationBenchmarkRunner`.
3. Implement `b1.empty-host` using existing `SimulationHost` setup patterns.
4. Add CLI arguments for scenario id, seed, warmup frames, measured frames, JSON path, and CSV path.
5. Add install-free tests for argument parsing and plan creation.

## Files likely to change

- New `src/Panopticon.Benchmarks.Simulation/`
- New tests under `test/Panopticon.Simulation.Tests/Benchmarks/`
- `src/Panopticon.sln`

## Result

Landed 2026-10-04 as `Panopticon.Benchmarks.Simulation` (console, references
`Panopticon.Simulation` + `Panopticon.Mods.Generals`, no BenchmarkDotNet):

- `SimulationBenchmarkPlan` / `SimulationBenchmarkScenario` / `SimulationBenchmarkRun` /
  `SimulationBenchmarkRunner` / `BenchmarkRunRecord`: warmup frames step unrecorded, then the
  measured frames run under the A1 `RunOutputRecorder`; the skeleton record adds
  `measuredWallMs` plus frames completed. Tick/system samples arrive in B1-3.
- `EmptyHostBenchmarkScenario` (`b1.empty-host`): skirmish boot on Alpine Assault with two
  `PlayerOwner.None` players, inert victory rule, and a post-boot `DisableAI()` sweep so
  nothing but host stepping is measured. Needed one
  `InternalsVisibleTo("Panopticon.Benchmarks.Simulation")` line beside the existing tool grants.
- `SimulationBenchmarkCommandLine`: `--scenario --seed --warmup-frames --measured-frames --map
  --json --csv --help`; `Program` writes a camelCase skeleton JSON embedding the A1 run output.
- Install-free tests (15 passing): plan validation, CLI parsing/defaults/errors, scenario
  lookup. Proven run with install: `b1.empty-host` seed 1, warmup 30, measured 300: 300/300
  frames, ~730 ms measured, exit 0, JSON populated with metric samples.

## Acceptance criteria

- [x] `dotnet build src` builds the benchmark project.
- [x] The CLI can construct a plan for `b1.empty-host` without a game install.
- [x] The project does not use BenchmarkDotNet.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SimulationBenchmark"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No metrics beyond skeleton timing and no reference workload implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
