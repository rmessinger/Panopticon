# Task Card `WI-A1-08`: Migrate the TestHarness headless loop onto `SimulationHost`

| Field | Value |
|---|---|
| Card ID | `WI-A1-08` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, A1-4 follow-up |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A1-06` |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Replace the TestHarness headless run loop's manual `game.Update(empty)` + `game.Render()` cycling
with the `SimulationHost` run loop, so the harness and the host share one stepping path.

## Context to read (and only this)

- `src/OpenSage.Game/SimulationHost.cs`: the host to migrate onto.
- `src/OpenSage.Tools.TestHarness/Program.cs` and the runner it dispatches to
  (`ReplayScenarioRunner` / `ValidationGroundRunner`): the loops to change.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs:60-135`: the current frame/dispatch loop
  the host's `InjectOrders(frame, ...)` + `Run` generalize.

## Steps

1. Construct a `SimulationHost` from the loaded game's `GameEngine` after
   `StartSkirmishOrMultiPlayerGame` / `LoadReplayFile`.
2. Drive frames through `host.Run(budget, stopPredicate)` with the existing exit conditions as
   the stop predicate; keep the existing frame caps and timeout semantics.
3. Route scheduled order dispatch through `host.InjectOrders(frame, orders)` instead of the
   per-iteration frame matching.
4. Keep `Game.Render` calls for windowed/capture modes; headless mode must make no `Render` call.
5. Run the TestHarness headless against a scenario and confirm identical action-results output
   before and after the migration.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/` (runner wiring)
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`

## Acceptance criteria

- [ ] Headless TestHarness runs produce byte-identical `.results.txt` output before and after.
- [ ] No `Game.Render` call remains on the headless path.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change windowed mode's render loop.
- Do not change the validation-ground baseline capture semantics (fixed timestep stays).

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
