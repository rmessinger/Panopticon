# Task Card: `WI-P0-16` — Invariant early-abort in HarnessRunner

| Field | Value |
|---|---|
| Card ID | `WI-P0-16` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-4, see [deep dive §3.4](../../deep-dives/p0-playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P0-07`, `WI-P0-08` |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

When a per-tick `InvariantAssertion` fails during a headless run, abort the run immediately and
record the offending frame — instead of continuing to the end of the run as the current
implementation does. This closes the gap between the spec (§3.4: "a failing invariant records the
frame and aborts") and the implementation.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` — `OnGameUpdating`
  records invariant failures but has no abort signal.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs` — the `while (game.IsRunning)` loop
  does not check for invariant violations.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the
  `InvariantAssertion_Violated_AbortsAndReportsOffendingFrame` stub to unblock.

## Steps

1. Add a boolean property `IsAborted` to `AssertionEvaluator`. Set it to `true` when the first
   invariant assertion fails inside `OnGameUpdating`. The existing `_invariantFailures` list and
   `_failedInvariantIds` set remain unchanged.

2. In `HarnessRunner.RunAndAssert`, after `game.Update(...)` inside the while loop, add:
   ```csharp
   if (evaluator.IsAborted)
   {
       break;
   }
   ```
   The loop exits early; `evaluator.Evaluate()` is still called (it returns the accumulated
   results including the failing invariant with its frame number).

3. Update the `InvariantAssertion_Violated_AbortsAndReportsOffendingFrame` stub in
   `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`:
   Tag `[Trait("Category", "Interactive")]`. Build a scenario with an `InvariantAssertion`
   that will fire on the first tick (e.g., `player[0].money < 0` — always false, so it fires
   immediately). Run via `HarnessRunner.RunAndAssert`. Assert:
   - `!report.AllPassed`
   - The failing result has a non-null `FailFrame`.
   - The run terminated before the default frame timeout (i.e., `FailFrame < 18000`).

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`

## Acceptance criteria

- [ ] `AssertionEvaluator.IsAborted` becomes `true` after the first `InvariantAssertion` failure.
- [ ] `HarnessRunner.RunAndAssert` breaks out of the update loop when `evaluator.IsAborted`.
- [ ] The `InvariantAssertion_Violated_AbortsAndReportsOffendingFrame` stub passes (no longer
      throws `NotImplementedException`).
- [ ] Scenarios with no invariant violations still run to their natural termination.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~InvariantAssertion"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the `InvariantAssertion` model type or JSON serialization.
- Do not add any other abort conditions — only invariant violations abort.

## On completion

- Set Status above to `Done`.
- Remove the `[Trait("Category", "BlockedOnP0-16")]` annotation from the stub in
  `P0HarnessAcceptanceTests.cs` (WI-P0-15 adds this annotation; WI-P0-16 removes it by implementing).
- Append a result line to repo-root `.exchange`.
