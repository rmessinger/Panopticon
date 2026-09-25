# Implementation Plan: Track P · P0-4: Validation and Assertion Layer

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/p0-playable-baseline-harness.md), sub-phase P0-4: Assertion layer
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Add a validation/assertion layer (expected post-conditions, invariants, outcomes)"
> Decision: [open-questions.md](../open-questions.md), "P0 harness: assertion DSL" (JSON predicates over a fixed vocabulary plus a code-defined escape hatch).
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

The harness can author and drive commands but cannot check what happened. `RunHeadless`
(`TestHarness/Program.cs:195-271`) only renders and exits on enemy-CC destruction; there is no way to
assert "money never went negative", "player 2 still had buildings after frame N", or "the match ended
with player 1 winning". Without an assertion layer, every other P phase (P3/P6/P7) has no
pass/fail signal to validate against, and cheap agents/CI cannot tell a good run from a broken one.

Checklist item AC:
- Assertions are authored as JSON predicates (`{path, op, value}`) over a fixed path/operator
  vocabulary spanning ≥1 outcome, ≥1 state, and ≥1 per-tick invariant type, with a code-defined (C#)
  predicate escape hatch.
- A headless run emits a machine-readable pass/fail report and a deliberately-failing assertion is
  reported with frame + expected/actual.

## Design (per the settled Decision)

Three assertion kinds, all declarative JSON predicates over a fixed vocabulary, plus a C# escape
hatch, evaluated headlessly:

1. State assertions : a predicate over engine state at end-of-run or at a trigger frame, e.g.
   `{path: "player[1].money", op: ">=", value: 0}`, `{path: "player[2].buildingCount", op: ">", value: 0, atFrame: 1800}`.
2. Invariant assertions : a predicate checked every tick via `Game.Updating` (`Game.cs:126`), e.g.
   money never negative, unit count never duplicated/leaked. A failing invariant records the frame
   and aborts the run.
3. Outcome assertions : match terminated; winner/loser/team/reason. These consume P3's queryable
   `GameOutcome` ([victory-defeat.md](../deep-dives/p3-victory-defeat.md)). `GameOutcome` does not exist
   yet, so this plan ships state + invariant assertions now and adds the outcome path as a thin
   adapter once P3 lands (the deep dive §6 notes this dependency).

### Path vocabulary

A fixed, documented set of read-only paths resolved against the running `Game`:

| Path | Resolves to |
|---|---|
| `player[i].money` | `Game` players' `BankAccount.Money` (`Logic/BankAccount.cs:13`) |
| `player[i].buildingCount` / `player[i].unitCount` | owned-object counts (the P1-5 maintained ownership set, [skirmish-setup.md](../deep-dives/p1-skirmish-setup.md)); until then, a `GameLogic` scan filtered by owner) |
| `player[i].name` / `player[i].relationshipTo[j]` | `Player.Name`, `Player.GetRelationship` (`Logic/Player.cs:64,811`) |
| `frame` | current logic frame (`game.MapTime` / `MsPerLogicFrame`, as `Program.cs:184,216` computes) |
| `outcome.winner` / `outcome.reason` | P3 `GameOutcome` (deferred until P3) |

Path resolution is a single `StatePath` resolver (string → value) with a closed switch; unknown
paths fail loudly at load time, not mid-run.

### Operator vocabulary

Closed set: `==`, `!=`, `>`, `>=`, `<`, `<=`, and `exists`. Values are JSON scalars (number/bool/string).

### Escape hatch

`{predicate: "MyInvariantName"}` references a C# predicate registered by name in a
`Func<Game, AssertionContext, bool>` registry, for invariants that exceed the path/operator
vocabulary (e.g. "no two objects share an `ObjectId`"). Registration is code-side; the scenario only
names it.

### Model + evaluator (Core)

- `Assertion` (JSON): `{ kind: state|invariant|outcome, path?, op?, value?, atFrame?, predicate? }`,
  parsed with the existing `System.Text.Json` polymorphic style used by `CommandArgument`.
- `AssertionSet` attached to `ReplayScenario` (new optional `Assertions` list; serialized in the
  scenario JSON, ignored by the `.rep` itself).
- `AssertionEvaluator` : given a `Game`:
  - subscribes to `Game.Updating` and evaluates invariant assertions each tick; on failure records
    `{assertionId, expected, actual, frame}` and signals abort;
  - evaluates state assertions at their `atFrame` (or end-of-run);
  - evaluates outcome assertions when the run terminates (P3 outcome; until then skipped/unsupported
    with a clear "requires P3" report entry).
- `AssertionReport` : structured, machine-readable: overall pass/fail + per-assertion
  `{id, kind, status, expected, actual, frame}`. Serializable to JSON.

### Where it runs

- The evaluator is wired by the [run-and-assert entry point](track-p-p0-6-run-and-assert-entry-point.md)
  (P0-5), which owns the headless boot. This plan delivers the evaluator + model + report as a
  library so P0-5 just constructs and drives it; a minimal in-process test can exercise the evaluator
  against a mocked/headless `Game` without the full CLI.

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/Panopticon.Tools.ReplaySketch.Core/Model/Assertion.cs` | NEW : assertion model (JSON predicates, 3 kinds) |
| `src/Panopticon.Tools.ReplaySketch.Core/Model/ReplayScenario.cs` | Add optional `Assertions` list (serialized in scenario JSON) |
| `src/Panopticon.Tools.ReplaySketch.Core/Services/StatePath.cs` | NEW : closed path resolver over `Game` state |
| `src/Panopticon.Tools.ReplaySketch.Core/Services/AssertionEvaluator.cs` | NEW : per-tick + end-of-run + outcome evaluation, abort on invariant failure |
| `src/Panopticon.Tools.ReplaySketch.Core/Services/AssertionReport.cs` | NEW : machine-readable pass/fail report |
| `src/Panopticon.Tools.ReplaySketch.Core/Services/CodePredicateRegistry.cs` | NEW : named C# predicate escape hatch |
| `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Tests below |

## Tests

- JSON round-trip: each assertion kind serializes/deserializes; an unknown `path` or `op` fails at
  load with a clear message.
- State assertion pass + fail against a mocked `Game` (set `BankAccount.Money` to known values;
  assert `player[0].money >= 0` passes and `>= 99999` fails with expected/actual recorded).
- Invariant assertion: drive `Game.Updating` for several frames with money going negative on a known
  frame; assert the report flags it with that frame.
- Escape-hatch: register a named C# predicate; assert it is invoked and its result reported.
- Report shape: a deliberately-failing assertion produces a JSON report with `frame` +
  `expected`/`actual` (the AC's machine-readable requirement).

## Out of scope

- Outcome assertions' real signal depends on P3 `GameOutcome`; implement the adapter only when P3
  lands. Until then outcome assertions parse but report "requires P3".
- The CLI wrapper and exit-code contract → [track-p-p0-6-run-and-assert-entry-point.md](track-p-p0-6-run-and-assert-entry-point.md).
- A query/expression language beyond fixed paths + operators (explicitly rejected by the Decision).
