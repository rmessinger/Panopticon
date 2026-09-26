# Task Card `WI-W1-15`: Add the 64-seed A2 experiment report

| Field | Value |
|---|---|
| Card ID | `WI-W1-15` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W1-14` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-14` is Done.

Run both combat variants through the A2 statistical reporting path with 64 seeds.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §11.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`: A2 distribution harness pattern.
- W1 aggregate scenario tests from `WI-W1-14`.

## Steps

1. Add a W1 experiment runner or test helper that executes stochastic and mean-field arms for 64 seeds.
2. Capture casualties, ground taken, order latency, and unmet demand per run.
3. Emit JSON run records compatible with existing A2 distribution reports.
4. Add a test that validates report shape without requiring a long default run.
5. Document the command used to run the full 64-seed report in the test output or README-local comment.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Experiments/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontA2ExperimentTests.cs`

## Acceptance criteria

- [ ] Experiment arm seed count is 64.
- [ ] Report includes stochastic and mean-field arms.
- [ ] Metrics include casualties, ground taken, order latency, and unmet demand.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontA2Experiment"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No performance acceptance threshold.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
