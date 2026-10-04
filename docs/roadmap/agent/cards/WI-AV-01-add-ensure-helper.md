# Task Card `WI-AV-01`: Add the `Ensure` helper and tests

| Field | Value |
|---|---|
| Card ID | `WI-AV-01` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | N/A: single static class, API table in the plan |

## Goal

Add `Panopticon.Ensure` with the methods in the plan's [API table](../../plans/argument-validation.md#ensure-api),
covered by unit tests.

## Context to read (and only this)

- `docs/roadmap/plans/argument-validation.md`: `Decisions` and `Ensure API` sections.
- `src/Panopticon.Core/DeltaTimer.cs`: namespace and file style.
- `src/Panopticon.Simulation/Content/Importing/ImportedMesh.cs:50-115`: existing message wording.

## Steps

1. Create `src/Panopticon.Core/Ensure.cs`: `public static class Ensure` in namespace `Panopticon`.
2. Implement each method from the API table. The last parameter of every method is
   `[CallerArgumentExpression(nameof(value))] string? paramName = null` (use the method's first
   parameter name). Mark `value` / `items` with `[NotNull]` on the null-rejecting methods so
   nullable flow analysis treats them as non-null afterwards.
3. When `message` is null, fall back to a generic message naming `paramName`.
4. `ArgumentOutOfRangeException` instances carry `actualValue` (`new(paramName, value, message)`).
5. Add `test/Panopticon.Simulation.Tests/EnsureTests.cs`: per method, one passing input and one
   failing input asserting exact exception type, `ParamName`, and that a supplied message appears
   in `Message`. Cover `NaN`, `+Infinity`, and `-Infinity` for the `Finite*` methods and
   `NoNullElements` index wording.

## Files likely to change

- `src/Panopticon.Core/Ensure.cs`
- `test/Panopticon.Simulation.Tests/EnsureTests.cs`

## Acceptance criteria

- [ ] Every method in the API table exists with the listed exception types.
- [ ] `EnsureTests` passes and covers every method.
- [ ] No call sites changed.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive&FullyQualifiedName~EnsureTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No call-site conversions.
- No project-reference changes.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
