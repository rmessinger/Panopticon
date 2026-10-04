# Task Card `WI-AV-03`: Convert string and null-element checks

| Field | Value |
|---|---|
| Card ID | `WI-AV-03` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-AV-01` |
| Diagrams | N/A: mechanical refactor |

## Goal

Replace the 23 `string.IsNullOrEmpty` / `IsNullOrWhiteSpace` guards and the 6 null-element loop
guards with `Ensure` calls that keep the existing message text verbatim.

## Context to read (and only this)

- `docs/roadmap/plans/argument-validation.md`: `Decisions` and `Ensure API`.
- `src/Panopticon.Core/Ensure.cs`.

## Steps

1. List string sites:
   ```sh
   grep -rn --include='*.cs' -A3 -E 'IsNullOr(Empty|WhiteSpace)\(' src test | grep -B3 'throw new Argument'
   ```
2. Convert each to `Ensure.NotNullOrEmpty(x, "<existing message>");` or
   `Ensure.NotNullOrWhiteSpace(...)`, matching the original predicate.
3. List element sites:
   ```sh
   grep -rn --include='*.cs' -B3 'at index {i} must not be null' src
   ```
   Include `ImportedMesh.cs` (`materials[i] is null`), `ImportedAnimation.cs` (`channels[i] is null`),
   and `ImportedMaterial.cs` (`properties[i] is null`), which use the same pattern.
4. Replace each loop with `Ensure.NoNullElements(items, "<Element name>");` where the element name
   reproduces the original message prefix (for example `"Mesh part"`). Keep the loop if it does
   other work and only remove the guard inside it.
5. For null-string inputs, tests asserting `Assert.Throws<ArgumentException>` now see
   `ArgumentNullException`. Update those assertions to `ArgumentNullException`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/*.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/**/*.cs` returned by step 1
- Matching tests under `test/`

## Acceptance criteria

- [ ] Step 1 and step 3 searches return no braced guards.
- [ ] Every converted message is byte-identical to the original.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Projects that do not reference `Panopticon.Core`: use a brace-less `if` instead of `Ensure`.
- No message rewording.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
