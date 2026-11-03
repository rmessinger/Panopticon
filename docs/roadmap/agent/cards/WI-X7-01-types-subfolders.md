# Task Card `WI-X7-01`: Organize non-logic types into `Types` subfolders

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X7-01` |
| Roadmap phase | [X7](../../track-x-fixes-tweaks.md): Types subfolder organization |
| Status | `Ready` |
| Type | `refactor` |
| Effort | `M` |
| Prerequisites | none (schedule when few branches are in flight, to limit rebase conflicts) |
| Diagrams | none (file moves only) |

## Goal

Move non-logic types (enums, small records and structs, data-only classes, constants, interfaces with no behavior) into a `Types/` subfolder of their domain, so each domain folder lists its logic classes first. No namespace, signature, or behavior change.

## Context to read (and only this)

- `docs/roadmap/implementation/track-x-x7-types-subfolders.md`.
- `AGENTS.md`, "Naming conventions": one type per file, with the small-type exception and the `Types/` rule.
- Existing `Types/` folders for the reference layout: `src/Panopticon.Simulation/Simulation/Types`, `src/Panopticon.Simulation/Content/Importing/Units/Types`.

## Steps

1. Inventory: list candidate files per domain under `src/` (enum-only, record-only, constants-only, data-only files). Record the table, with a domain per row and a file count, in the plan before moving anything.
2. Apply the selection rule from the plan. A domain moves only when it has at least 3 qualifying files, so one-off types stay beside their owner.
3. Move files with `git mv` so history follows. Do one domain per commit.
4. Keep namespaces unchanged, matching the existing `Types/` folders. Do not rename types.
5. Leave types that carry behavior, are tightly paired with a single consumer, or are persisted-format definitions in place unless the plan lists them.
6. Fix `.csproj` includes, source generators, embedded-resource paths, and docs that reference moved paths. Grep `docs/` and `docs/roadmap/` for old paths and update them.
7. Record moved-file counts per domain and skipped domains with reasons in "WI-X7-01 results".

## Files likely to change

- `src/**/Types/*.cs` (moved, new folders)
- `docs/roadmap/implementation/track-x-x7-types-subfolders.md`
- Docs that cite moved file paths

## Acceptance criteria

- [ ] Every moved file keeps its namespace and type name; no source line changes other than file location.
- [ ] Every domain with at least 3 qualifying files has a `Types/` subfolder holding them; skipped domains are listed with reasons.
- [ ] No behavior-bearing class moved into a `Types/` folder.
- [ ] `SeededRunTests`, golden baselines, and run-output byte-identity pass unmodified.
- [ ] No stale path references remain in docs.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
git diff -M --stat origin/main | tail -1
```

`git diff -M` must show the moves as renames with no content changes.

## Out of scope / guardrails

- No namespace changes, type renames, or splitting and merging of types.
- No test file moves unless the test folder mirrors a moved source folder and the plan lists it.
- No logic changes. A semantic issue found on the way is recorded as a follow-up.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, tick X7-1 in `docs/roadmap/implementation-checklist.md`, set X7 `Done` in the track doc, update the README progress row, and fill "WI-X7-01 results" in the X7 plan.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
