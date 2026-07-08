# Task Card: `WI-P0-17` — Code-defined predicate escape hatch

| Field | Value |
|---|---|
| Card ID | `WI-P0-17` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-4, see [deep dive §3.4](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-07` |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Add a `CodeAssertion` type: a named C# predicate registered by name that can be referenced
from a scenario's assertion list. This is the escape hatch for invariants that exceed the
JSON path/operator vocabulary — e.g., "unit count is distinct (no duplicates)". The registration
is in-process code; the scenario JSON references the predicate by its registered name.

## Context to read (and only this)

- `docs/roadmap/deep-dives/playable-baseline-harness.md §3.4` — "escape hatch of code-defined
  (C#) predicates (registered by name, referenced from the scenario)".
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` — the base class and
  the three existing subtypes; `CodeAssertion` is a fourth subtype.
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionEvaluator.cs` — needs a static
  registration table and must dispatch `CodeAssertion` evaluations through it.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — the
  `CodeDefinedPredicateEscapeHatch_Registered_EvaluatesCorrectly` stub to unblock.

## Steps

1. Add a static `CodeAssertionRegistry` class in
   `OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs`:
   ```csharp
   public static class CodeAssertionRegistry
   {
       private static readonly Dictionary<string, Func<Game, uint, bool>> s_predicates = new();

       public static void Register(string name, Func<Game, uint, bool> predicate)
           => s_predicates[name] = predicate;

       public static bool TryGet(string name, out Func<Game, uint, bool>? predicate)
           => s_predicates.TryGetValue(name, out predicate);
   }
   ```

2. Add a `CodeAssertion` subtype to `ScenarioAssertion.cs`:
   ```csharp
   [JsonDerivedType(typeof(CodeAssertion), "code")]
   ```
   (Add this attribute to the `ScenarioAssertion` class alongside the existing ones.)

   ```csharp
   public sealed class CodeAssertion : ScenarioAssertion
   {
       public string PredicateName { get; set; } = string.Empty;

       public override AssertionResult Evaluate(Game game, uint frame)
       {
           if (!CodeAssertionRegistry.TryGet(PredicateName, out var predicate) || predicate is null)
           {
               return new AssertionResult
               {
                   AssertionId = Id,
                   Passed = false,
                   FailFrame = frame,
                   Message = $"No predicate registered with name '{PredicateName}'.",
               };
           }

           var passed = predicate(game, frame);
           return new AssertionResult
           {
               AssertionId = Id,
               Passed = passed,
               FailFrame = passed ? null : frame,
               Message = passed ? null : $"Code predicate '{PredicateName}' returned false.",
           };
       }
   }
   ```

3. Update the `CodeDefinedPredicateEscapeHatch_Registered_EvaluatesCorrectly` stub in
   `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs`:
   - Register a predicate with a test name (e.g., `"always-true"`) via `CodeAssertionRegistry.Register`.
   - Build a `ReplayScenario` with a `CodeAssertion { PredicateName = "always-true" }`.
   - Tag `[Trait("Category", "Interactive")]` and run via `HarnessRunner.RunAndAssert`.
   - Assert `report.AllPassed`.

4. Add unit tests in `OpenSage.Tools.ReplaySketch.Tests/CodeAssertionTests.cs`:
   - `CodeAssertion_RegistryMiss_ReportsFail`: a `CodeAssertion` with an unregistered name
     evaluates to `Passed=false` with a clear message.
   - `CodeAssertion_RegistryHit_ReportsPredicateResult`: register a predicate that returns `true`,
     build a `CodeAssertion` referencing it, serialize/deserialize the assertion, call `Evaluate`
     with `null` game (verify the predicate call path works with a stub — or use `default!` if the
     predicate ignores the `Game` argument in the test).
   - `CodeAssertion_Json_RoundTrips`: serialize and deserialize a `CodeAssertion` with
     `kind == "code"`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` — add `CodeAssertion`
  subtype and `[JsonDerivedType]` attribute.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs` — unblock stub.
- `src/OpenSage.Tools.ReplaySketch.Tests/CodeAssertionTests.cs` (new unit tests)

## Acceptance criteria

- [ ] `CodeAssertion` with an unregistered name evaluates to `Passed=false` with a clear message.
- [ ] `CodeAssertion` with a registered predicate evaluates to the predicate's return value.
- [ ] `CodeAssertion` serializes/deserializes with discriminator `"kind":"code"`.
- [ ] The `CodeDefinedPredicateEscapeHatch_Registered_EvaluatesCorrectly` stub no longer throws
      `NotImplementedException`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter "FullyQualifiedName~CodeAssertion OR FullyQualifiedName~EscapeHatch"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- `CodeAssertionRegistry` is a simple static dictionary. Do not add thread-safety, DI
  integration, or dynamic loading.
- `InvariantAssertion` subtypes are separate from `CodeAssertion` — a code-defined invariant
  (per-tick check) is not in scope for this card. If needed later, it can be added as a fifth
  subtype.

## On completion

- Set Status above to `Done`.
- Remove the `[Trait("Category", "BlockedOnP0-17")]` annotation added by WI-P0-15.
- Append a result line to repo-root `.exchange`.
