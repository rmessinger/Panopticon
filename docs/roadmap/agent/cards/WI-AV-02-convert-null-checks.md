# Task Card `WI-AV-02`: Convert bare null checks to `ThrowIfNull`

| Field | Value |
|---|---|
| Card ID | `WI-AV-02` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | N/A: mechanical refactor |

## Goal

Replace the 59 braced `if (x is null) { throw new ArgumentNullException(nameof(x)); }` guards
(also `== null`) with `ArgumentNullException.ThrowIfNull(x);`.

## Context to read (and only this)

- `docs/roadmap/plans/argument-validation.md`: `Decisions`.
- Each file returned by the site search below.

## Steps

1. List sites:
   ```sh
   grep -rn --include='*.cs' -B3 'throw new ArgumentNullException(nameof(' src test
   ```
2. Convert only guards whose condition is exactly `x is null` or `x == null` and whose throw is
   `new ArgumentNullException(nameof(x))` with no message.
3. Where the guard is followed by an assignment of the same argument to a field, either form is
   acceptable; prefer `_x = x ?? throw new ArgumentNullException(nameof(x));` only when the guard
   and assignment are adjacent.
4. Add `using System;` where missing.
5. Skip `src/Panopticon.Simulation.CodeGen` (`netstandard2.0`).

## Files likely to change

- Files under `src/` and `test/` returned by step 1, excluding `Panopticon.Simulation.CodeGen`.

## Acceptance criteria

- [ ] Step 1 search returns no braced bare-`nameof` null guards outside CodeGen.
- [ ] Guards carrying a custom message are untouched (owned by `WI-AV-03`).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No behavior change: same exception type and `ParamName`.
- No other guard categories.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
