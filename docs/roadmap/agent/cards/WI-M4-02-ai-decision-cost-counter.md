# Task Card `WI-M4-02`: AI decision cost counter

| Field | Value |
|---|---|
| Card ID | `WI-M4-02` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [M4 army hierarchy](../../deep-dives/mass-battle-1k.md#m4-army-hierarchy) |

## Goal

B1 records per-tick AI decision cost as an `ai` system span, so M4-3's budget can be measured. The M1 report listed this counter as a missing follow-up.

## Context to read (and only this)

- `src/Panopticon.Simulation/LogicStepper.cs` `LogicTick`: the measured path and `SystemSpan` event.
- `src/Panopticon.Simulation/Logic/Player.cs` `LogicTick` (calls `AIPlayer?.Update()`).
- `src/Panopticon.Simulation/Logic/AI/AIPlayer.cs` `OnDecisionTick`.
- `src/Panopticon.Benchmarks.Simulation/BenchmarkMetricRecorder.cs` `RecordSystemSpan` and its statistics.
- `src/Panopticon.Simulation/Navigation/Navigation.cs` `PathRequestCount`: the existing allocation-free counter pattern.

## Steps

1. Accumulate decision time per logic tick: time each `OnDecisionTick` (policy decision plus dispatch) with `Stopwatch` timestamps into an allocation-free accumulator on the game logic, reset at tick start.
2. In the measured `LogicTick` path, emit the accumulated value as `spanSink("ai", ...)` after `gameLogic`. Document that `ai` is nested inside `gameLogic`, not additive.
3. Keep the default (unmeasured) path unchanged and allocation-free.
4. Confirm the recorder produces `system.ai.spanMs` statistics with no recorder change, or add it.
5. Add an install-free test: a stub policy that spins for a known interval yields a nonzero `ai` span on its decision frame and zero on other frames.

## Files likely to change

- `src/Panopticon.Simulation/LogicStepper.cs`
- `src/Panopticon.Simulation/Logic/AI/AIPlayer.cs`
- `src/Panopticon.Simulation/Logic/GameLogic.cs` (or wherever the accumulator lives)
- `test/Panopticon.Simulation.Tests/Benchmarks/AiDecisionSpanTests.cs` (new)

## Acceptance criteria

- [ ] A measured run emits an `ai` span every tick; it is zero on non-decision ticks.
- [ ] The B1 record carries `system.ai.spanMs` statistics.
- [ ] The default stepping path allocates nothing new (existing B1 allocation tests pass).
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~AiDecisionSpan"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No budget values or verdicts; WI-M4-06 owns them.
- Per-unit `AIUpdate` state machines are not AI decision cost; they stay inside `gameLogic`.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none.
