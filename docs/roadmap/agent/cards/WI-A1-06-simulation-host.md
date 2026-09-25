# Task Card `WI-A1-06`: Introduce `SimulationHost` with faster-than-real-time stepping

| Field | Value |
|---|---|
| Card ID | `WI-A1-06` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-4 |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-05` (the stepper is the host's engine) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Add the `SimulationHost` of
[a1-headless-host.md §2.4](../../deep-dives/a1-headless-host.md#24-simulationhost): a host that owns
the logic-side world through `IGameEngine`, advances it by N frames or until a stop condition as
fast as the hardware allows, and exposes order-injection and per-tick result hooks.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-headless-host.md` §2.4, §3 (A1-4 row), §5.
- `docs/roadmap/implementation/track-a-a1-headless-host.md`: the tick-path audit.
- `src/Panopticon.Simulation/LogicStepper.cs`: produced by `WI-A1-05`; the host drives this.
- `src/Panopticon.Simulation/GameEngine.cs`: the `IGameEngine` seam the host owns the world through.
- `src/Panopticon.Tools.TestHarness/Program.cs`: the existing headless loop, which this host
  generalizes. Do not modify it in this card.
- `src/Panopticon.Tools.ReplaySketch.Core/`: the existing order-injection path, for the shape of
  `InjectOrders`. Mirror it; do not duplicate its logic.

## Steps

1. Add `src/Panopticon.Simulation/SimulationHost.cs` holding an `IGameEngine` and a `LogicStepper`.
2. Give it a run method taking a frame budget and an optional stop predicate, stepping in a tight
   loop with no wall-clock sleep and no `Render` call.
3. Add an order-injection hook that enqueues orders for a given logic frame, delegating to the same
   path ReplaySketch already uses. Do not invent a second order pipeline.
4. Add a per-tick callback the caller can use to observe frame number and logic state. Keep it a
   plain event or delegate; do not design the A1-5 recording format here.
5. Keep the host domain-agnostic in shape: it must not reference RTS-specific types beyond what
   `IGameEngine` already exposes. Where a domain seam would be needed, take a parameter and note the
   A4 dependency in a comment rather than hard-wiring RTS.
6. Add a test under `test/Panopticon.Simulation.Tests/` that runs the host for a fixed frame budget against
   the mocked game and asserts the frame count and that a stop predicate terminates the run early.
7. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/Panopticon.Simulation/SimulationHost.cs` (new)
- `test/Panopticon.Simulation.Tests/` (new test file)

## Acceptance criteria

- [ ] `SimulationHost` runs N logic frames with no `Game.Render` call and no window.
- [ ] A stop predicate ends the run before the frame budget and reports the frame reached.
- [ ] Injected orders reach the same order pipeline the existing harness uses; no parallel pipeline
      is added.
- [ ] `SimulationHost` references no RTS-specific type not already exposed by `IGameEngine`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement the A4 domain-model seam. Parameterize where it belongs and stop.
- Do not implement run outputs, snapshots, metrics, or the tiered recording. That is `WI-A1-07`.
- Do not rewire the TestHarness or ReplaySketch onto the host in this card.
- Do not add a benchmark harness; the faster-than-real-time measurement belongs to B1.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A1-4 in [`implementation-checklist.md`](../../implementation-checklist.md), except the
  benchmark clause, which B1 owns.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A card migrating `TestHarness.RunHeadless` onto `SimulationHost`.
