# Task Card `WI-P0-06`: Generalize ReplaySketch exporter for open `CommandSpec` commands

| Field | Value |
|---|---|
| Card ID | `WI-P0-06` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-3, see [deep dive §2.2/§3.2](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P0-03` (faction-agnostic definition resolution must be done first) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Ensure `ReplayExporter` correctly emits orders for open `CommandSpec` commands (those whose
`ActionEntry.Type` is `null`) for all non-deferred timing types, and replaces the silent skip
for open-command deferred triggers with an explicit exception: so authoring gaps are caught early
rather than producing a silently incomplete replay.

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:44-100`: `Export(...)`: the main
  loop. Line ~67: deferred path (AfterActionTiming). Line ~87: non-deferred path. Line ~91 calls
  `BuildOrders(...)`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:200-270`: `BuildOrders(...)`:
  line ~206 is the open-command early exit (`action.Type == null → action.Command.ToOrder(...)`).
  Lines ~77-83: the silent skip for open-command deferred triggers (comment: "Open commands cannot
  yet be deferred triggers").
- `src/Panopticon.Tools.ReplaySketch.Core/Model/CommandArgument.cs`: the five `CommandArgument`
  subtypes (`IntegerCommandArgument`, `FloatCommandArgument`, `BooleanCommandArgument`,
  `ObjectIdCommandArgument`, `PositionCommandArgument`).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/CommandSpec.cs:32-45`: `ToOrder(...)`:
  calls `arg.AppendTo(order, ctx, rng)` for each argument.
- `test/Panopticon.Tools.ReplaySketch.Tests/`: existing `ReplaySketchTests` for test style.

## Steps

1. In `Export`, locate the silent-skip block for open-command deferred triggers (around line 79,
   comment "Open commands cannot yet be deferred triggers"). Replace the `else { }` with a thrown
   `InvalidOperationException`: `"AfterActionTiming on action '<id>' references trigger '<triggerId>' which is an open CommandSpec command. Open-command deferred triggers are not yet supported."`.
   This makes the gap explicit rather than silently producing an incomplete replay.

2. Add a unit test covering the non-deferred open-command path:
   - Build a `ReplayScenario` with a single player and one `ActionEntry` whose `Type` is `null`
     and whose `Command` is `CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)))`.
   - Give it a `FixedTiming(100)`.
   - Call `ReplayExporter.Export(...)` with a synthetic `MapMetadataService` (use
     `MapMetadataService.CreateForTesting` from the test project).
   - Assert the export succeeds (`error == null`), no deferred actions are returned, and the
     output file exists.

3. Add a unit test that an open-command trigger throws:
   - Build a scenario with two `ActionEntry` values: entry A (`Type == null`, `FixedTiming`),
     entry B (`Type == null`, `AfterActionTiming(A.Id, Completed)`).
   - Assert `Export` throws `InvalidOperationException` containing the trigger's ID.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayExporter.cs`. Replace silent skip with throw.
- `test/Panopticon.Tools.ReplaySketch.Tests/`: two new unit tests.

## Acceptance criteria

- [ ] An `ActionEntry` with `Type == null` and `FixedTiming` exports successfully (no exception,
      output file written, `deferredActions` is empty).
- [ ] An `ActionEntry` with `Type == null` and `AfterActionTiming` whose trigger is also an open
      command throws `InvalidOperationException` with a message that names the offending action IDs.
- [ ] All existing `ReplaySketchTests` still pass (the legacy `alpine-assault-usa-vs-gla` profile
      is unaffected).
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement open-command deferred-trigger support: just make the gap explicit.
- Do not change `CommandSpec`, `CommandArgument`, `DeferredAction`, or the TestHarness injection path.
- Do not change the legacy `ActionType` switch cases in `BuildOrders`.
- `MapMetadataService.CreateForTesting` is already available in the test project; use it.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P0-3 item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up: open-command deferred-trigger support (lifting the `InvalidOperationException`) is
  a future card that requires updating `DeferredAction` and `ReplayActionMonitor` in TestHarness.
