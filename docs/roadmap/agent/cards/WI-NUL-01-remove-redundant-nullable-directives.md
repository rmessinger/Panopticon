# Task Card `WI-NUL-01`: Remove redundant `#nullable enable` directives

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-NUL-01` |
| Roadmap phase | None (cross-track code hygiene; see [roadmap index](../README.md)) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None; mechanical edit with no architecture change |

## Goal

Delete every file-level `#nullable enable` directive in projects whose `.csproj` already sets
`<Nullable>enable</Nullable>`. The directive is a no-op there. Files in projects without the
project-wide setting keep it, since it is what enables nullable annotations for that file.

## Context to read (and only this)

- `src/*/*.csproj`, `test/*/*.csproj`, and `Directory.Build.props`: which projects set `<Nullable>enable</Nullable>`.
- `docs/coding-style.md`: numbered rules, for the closing rule addition.

Counts on `main` at card creation (recompute at execution; open branches may add more):

| Project | Files with the directive |
|---|---|
| `src/Panopticon.Tools.ReplaySketch.Core` | 38 |
| `test/Panopticon.Simulation.Tests` | 16 |
| `test/Panopticon.Tools.ReplaySketch.Tests` | 11 |
| `src/Panopticon.Tools.TestHarness` | 9 |
| `src/Panopticon.IO` | 1 |
| `src/Panopticon.Tools.ReplaySketch.Avalonia` | 1 |

`Panopticon.Simulation` and `Panopticon.Mods.Generals` have no project-wide setting; their
directives stay.

## Steps

1. List projects with the project-wide setting:
   `grep -l '<Nullable>enable</Nullable>' src/*/*.csproj test/*/*.csproj`. Confirm
   `Directory.Build.props` does not set `Nullable`; if it does, stop and escalate, since the
   scope then changes.
2. In each listed project directory, find `.cs` files containing a line exactly `#nullable enable`
   (`grep -rlx '#nullable enable' <dir> --include=*.cs`).
3. In each file, delete that line. If the next line is blank and the directive was the first line,
   delete the blank line too, so the file starts at its first `using` or `namespace`.
4. Leave any `#nullable disable` or `#nullable restore` untouched (none exist at card creation).
5. Add rule 20 to `docs/coding-style.md`: "Do not add `#nullable enable` in projects whose
   `.csproj` sets `<Nullable>enable</Nullable>`; use it only in projects without the
   project-wide setting."

## Files likely to change

- `.cs` files under the six projects in the table above.
- `docs/coding-style.md`.

## Acceptance criteria

- [ ] For every project with `<Nullable>enable</Nullable>`, `grep -rlx '#nullable enable' <dir> --include=*.cs` returns nothing.
- [ ] `Panopticon.Simulation` and `Panopticon.Mods.Generals` directive counts are unchanged (239 and 3 on `main` at card creation; H2a branches add more to `Panopticon.Simulation`).
- [ ] No new compiler warnings: the warning count of `dotnet build src` is unchanged from before the edit.
- [ ] No file starts with a blank line as a result of the edit.
- [ ] `git diff --stat` shows only deletions in `.cs` files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not enable `<Nullable>` in `Panopticon.Simulation` or any other project; that surfaces new
  warnings across hundreds of files and needs its own card.
- Do not touch directives in projects without the project-wide setting.
- Do not change any other code, usings, or formatting in the edited files.
- Determinism tests cannot be affected by this edit; any failure there follows the repository
  rule: stop, preserve artifacts, report, and wait.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
