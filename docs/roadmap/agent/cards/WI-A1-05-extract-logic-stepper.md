# Task Card `WI-A1-05`: Extract the logic tick into a renderer-free stepper

| Field | Value |
|---|---|
| Card ID | `WI-A1-05` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-3 |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A1-03` (diagrams), `WI-A1-04` (data-only asset loading, so the stepper can be exercised without a device) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Move the fixed-rate logic step out of `Game.Update` into a reusable stepper that runs with no
presentation systems, closing blockers H3 and H5 from
[a1-headless-host.md §2.3](../../deep-dives/a1-headless-host.md#23-logic-tick-extraction), with no
behavior change for the windowed game.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a1-headless-host.md`: the `WI-A1-02` tick-path audit table.
  Its `logic` rows are exactly what moves; its `presentation` rows are exactly what stays.
- `docs/roadmap/deep-dives/a1-headless-host.md` §2.3, §2.4, §5.
- `src/OpenSage.Game/Game.cs:914-1000`: `StartRun`, `Update`, `LocalLogicTick`, `LogicTick`,
  `GetTimeInterval`, `EnableFixedLogicTimestep`.
- `src/OpenSage.Game/GameEngine.cs`: the `IGameEngine` services the stepper reads.

## Steps

1. Add `src/OpenSage.Game/LogicStepper.cs` owning the logic-side state the audit identified:
   `_nextLogicUpdate`, `_nextScriptingUpdate`, `LogicUpdateInterval`, the scripting interval,
   `CumulativeLogicUpdateError`, and the `IsLogicRunning` / stepping flags.
2. Give it one public method that advances logic given the current map time, performing exactly the
   work in today's `LogicTick` plus the scripting-tick branch of `Update`, in the same order.
3. Change `Game.Update` to delegate to the stepper. `LocalLogicTick` and its presentation calls stay
   in `Game` and stay in their current position relative to the logic step.
4. Preserve the existing tick order exactly: `LocalLogicTick`, then logic step, then scripting step.
   Do not resolve the two `// TODO: What is the order?` comments; carry them across unchanged.
5. Keep `EnableFixedLogicTimestep` working: the P9 Unit Validation Ground depends on fixed-timestep
   logic-frame counts being explicit.
6. Add a test under `src/OpenSage.Game.Tests/` that drives the stepper directly for N steps with no
   `Game.Render` call and asserts the logic frame count advances by N.
7. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/LogicStepper.cs` (new)
- `src/OpenSage.Game/Game.cs`
- `src/OpenSage.Game.Tests/` (new test file)

## Acceptance criteria

- [ ] The logic step runs through the stepper with no reference to `Scene2D`, `Audio`, `Cursors`,
      `InputMessageBuffer`, or `_renderTimer`.
- [ ] Tick ordering and interval arithmetic are byte-for-byte equivalent to the previous
      `Game.Update`.
- [ ] The P7 regression suite passes unchanged:
      `dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"`.
- [ ] The P9 fixed-timestep behavior is unchanged: the Unit Validation Ground tests still pass.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not introduce `SimulationHost`. That is `WI-A1-06`.
- Do not remove or reorder any presentation call; only relocate the logic ones.
- Do not change `MsPerLogicFrame`, the scripting rate, or any per-game tick-rate value.
- If a `logic` row in the audit turns out to read presentation state, stop and mark `Blocked`; that
  is an A3 concern, not an A1 one.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A1-3 in [`implementation-checklist.md`](../../implementation-checklist.md).
- Change `WI-A1-06` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
