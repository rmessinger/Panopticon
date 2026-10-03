# Task Card `WI-AV-06`: Analyzer enforcement and style rule

| Field | Value |
|---|---|
| Card ID | `WI-AV-06` |
| Plan | [Single-line argument validation](../../plans/argument-validation.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-AV-02`, `WI-AV-03`, `WI-AV-04`, `WI-AV-05` |
| Diagrams | N/A: configuration and docs |

## Goal

Prevent regressions: enable the BCL throw-helper analyzers as warnings and record the convention
in the coding style.

## Context to read (and only this)

- `.editorconfig`
- `docs/coding-style.md` rules 1, 17, 18.
- `AGENTS.md` `Repository-Specific Memories` section.

## Steps

1. In `.editorconfig` under `[*.cs]`, add:
   ```ini
   dotnet_diagnostic.CA1510.severity = warning
   dotnet_diagnostic.CA1511.severity = warning
   dotnet_diagnostic.CA1512.severity = warning
   ```
2. Build. Fix any reported site with the matching BCL helper, or `Ensure` if it carries a message.
   Suppress for `Panopticon.Simulation.CodeGen` (`netstandard2.0`) via a
   `[src/Panopticon.Simulation.CodeGen/**.cs]` section if it reports.
3. Add a rule to `docs/coding-style.md`: argument validation is one line, using BCL `ThrowIf*`
   helpers when no custom message exists, `Panopticon.Ensure` when one does, and a brace-less `if`
   for conditions neither expresses.
4. Add one bullet to `AGENTS.md` under `Validation` pointing at the new rule.

## Files likely to change

- `.editorconfig`
- `docs/coding-style.md`
- `AGENTS.md`

## Acceptance criteria

- [ ] `dotnet build src` reports zero CA1510/CA1511/CA1512 warnings.
- [ ] Coding-style rule and AGENTS.md bullet present.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No new analyzers beyond the three listed.

## On completion

- Set Status to `Done`.
- Append a result line to repo-root `.exchange`.
