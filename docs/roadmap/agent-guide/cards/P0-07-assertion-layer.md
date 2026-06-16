# Task Card: `P0-07` — Assertion layer: data model and evaluator

| Field | Value |
|---|---|
| Card ID | `P0-07` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-4, see [deep dive §3.4](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none (evaluates against `Game` state accessible independently of other P0 cards) |

## Goal

Add a declarative assertion layer to the ReplaySketch harness: `ScenarioAssertion` subtypes
serializable to/from JSON, an `AssertionEvaluator` that hooks into a running `Game` and produces
a structured `AssertionReport` (pass/fail per assertion with frame + actual value on failure).
This is the data model and evaluation logic only — the CLI entry point is P0-08.

## Context to read (and only this)

- `docs/roadmap/deep-dives/playable-baseline-harness.md §3.4` — the three assertion kinds
  (outcome, state, invariant) and the JSON predicate vocabulary.
- `src/OpenSage.Game/Game.cs:876-959` — `Game.Updating` event (per-tick hook for invariant assertions).
- `src/OpenSage.Game/Logic/Player.cs` and `src/OpenSage.Game/Logic/BankAccount.cs` — state readable
  for assertions (money, unit count, building count). Find property names by grepping for `public.*Money`
  and `public.*int` in those files.
- `src/OpenSage.Tools.ReplaySketch.Core/` — this is where the new types live (already has `OpenSage.Game`
  as a transitive dependency via `OpenSage.Game` project reference).

## Steps

1. In `OpenSage.Tools.ReplaySketch.Core/Assertions/`, create:

   `ScenarioAssertion.cs` — abstract base class with `string Id` property and abstract
   `AssertionResult Evaluate(Game game, uint frame)` method.

   Three concrete subtypes in the same file (or separate files in the same folder):

   - `StateAssertion`: `string Path`, `string Op` (`"=="`,`"!="`,`">="`,`"<="`,`">"`,`"<"`),
     `double Value`. Evaluated once at end-of-run. `Path` vocabulary (implement as a switch):
     `player[N].money`, `player[N].unitCount`, `player[N].buildingCount`, `outcome.status`
     (maps to `int` via `(int)game.Scene.LocalPlayer...` — see grounding step below),
     `outcome.reason`. Resolve `player[N]` by indexing `game.Scene.Players` (0-based).
   - `InvariantAssertion`: same fields as `StateAssertion` but checked every tick during the run.
     A failure records the failing frame.
   - `OutcomeAssertion`: `string ExpectedStatus` (`"Decided"`,`"SuddenDeath"`), optional
     `string ExpectedReason`, optional `int ExpectedWinningTeam`. Evaluated at end-of-run against
     the `GameOutcome` once P3 lands; for now evaluate against a stub that always returns
     `OutcomeStatus.InProgress` so the assertion produces a clear "not yet implemented" failure.

2. Create `AssertionResult.cs`:
   `string AssertionId`, `bool Passed`, `string? ActualValue`, `uint? FailFrame`, `string? Message`.

3. Create `AssertionReport.cs`:
   `IReadOnlyList<AssertionResult> Results`, `bool AllPassed` (computed: `Results.All(r => r.Passed)`),
   static `AssertionReport.Empty`.
   Add a `ToJson()` method that serializes using `System.Text.Json` to a compact object:
   `{ "allPassed": bool, "results": [{ "id", "passed", "actual", "failFrame", "message" }] }`.

4. Create `AssertionEvaluator.cs`:
   Constructor takes `IReadOnlyList<ScenarioAssertion> assertions` and `Game game`.
   Method `void Subscribe()`: registers `InvariantAssertion`s against `game.Updating`.
   Method `AssertionReport Evaluate()`: runs all `StateAssertion`s and `OutcomeAssertion`s against
   current game state, combines with any invariant failures accumulated during the run.
   The evaluator unsubscribes from `game.Updating` after `Evaluate()` is called.

5. Add JSON support: annotate `ScenarioAssertion` with `[JsonPolymorphic]` and
   `[JsonDerivedType]` for each subtype (use `"state"`, `"invariant"`, `"outcome"` discriminators,
   matching the pattern in `CommandArgument.cs`).

6. Extend `ReplayScenario` with `IReadOnlyList<ScenarioAssertion> Assertions` (default empty),
   serialized as `"assertions"` in the JSON profile.

7. Add unit tests in `OpenSage.Tools.ReplaySketch.Tests`:
   - A `StateAssertion` with `Path="player[0].money"`, `Op=">="`, `Value=0` and a stub `Game`
     (or mock via the test infrastructure) evaluates to `Passed=true` when money is non-negative.
   - An `AssertionReport.ToJson()` round-trips correctly (allPassed reflects results, IDs present).
   - An `InvariantAssertion` with a deliberately impossible condition records the correct fail frame.
   (Use a real or in-memory `Game` if available; otherwise defer game-state tests to P0-08.)

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionResult.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionReport.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs` — add `Assertions` property.
- `src/OpenSage.Tools.ReplaySketch.Tests/` — new unit tests.

## Acceptance criteria

- [ ] `ScenarioAssertion` and its three subtypes serialize to/from JSON with the correct
      type discriminator (`"state"`, `"invariant"`, `"outcome"`).
- [ ] `ReplayScenario` round-trips through JSON with an `assertions` array.
- [ ] `AssertionReport.ToJson()` produces valid JSON with `allPassed` and a `results` array.
- [ ] `AssertionEvaluator` subscribes to `game.Updating` for invariant assertions and
      unsubscribes after `Evaluate()`.
- [ ] The path vocabulary (`player[N].money`, `player[N].unitCount`, `player[N].buildingCount`,
      `outcome.status`, `outcome.reason`) resolves without throwing for valid indices.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement the CLI entry point (that is P0-08).
- Do not implement real `GameOutcome` yet — `OutcomeAssertion` evaluates against a stub; real
  support lands when P3 (`GameOutcome`) is implemented.
- The code-defined (C#) predicate escape hatch is out of scope for this card; add it later
  as a follow-up once the JSON path is established.
- If reading game state from `Game.Scene.Players` requires a running game, document the limitation
  in the test rather than fighting it — the evaluator's logic test matters more than full integration.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P0-4 assertion-layer item in the [implementation checklist](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up: `P0-08` (run-and-assert entry point) depends on this card.
