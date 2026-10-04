# Task Card `WI-REN-01`: Rename `SageRandom` to `PanopticonRandom`

| Field | Value |
|---|---|
| Card ID | `WI-REN-01` |
| Roadmap phase | None (cross-track naming hygiene from the OpenSAGE-to-Panopticon rename; see [roadmap index](../README.md)) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | A1-6 stack merged (PRs #143-#145); those branches add new `SageRandom` references and must land first |
| Diagrams | None; mechanical rename with no architecture change. RNG position in the engine: [A1-6 deep dive](../../deep-dives/a1-6-operational-recording.md) sections 4 and 5 |

## Goal

Rename the class `SageRandom` to `PanopticonRandom` everywhere in code, so the deterministic logic RNG carries the project name like the rest of the post-rename types.

## Context to read (and only this)

- `src/Panopticon.Mathematics/IRandom.cs`: definition of `SageRandom` alongside `IRandom` and `SystemRandom`
- `src/Panopticon.Simulation/Logic/GameLogic.cs`: `Random` field, `ApplyPendingRandomState`, `Persist` version 11 block
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs` and `EngineCheckpointRestorer.cs`: random-section capture and restore
- `test/Panopticon.Mathematics.Tests/SageRandomTests.cs`: existing tests, file renames with the class

## Steps

1. Rename the class `SageRandom` to `PanopticonRandom` in `src/Panopticon.Mathematics/IRandom.cs`. Do not rename `IRandom`, `SystemRandom`, the file, or any member (`Seed`, `Initialize`, `CopyState`, `RestoreState`, `StateWordCount`).
2. Rename `test/Panopticon.Mathematics.Tests/SageRandomTests.cs` to `PanopticonRandomTests.cs` and update the class name inside.
3. Update all remaining code references: `IGame.cs`, `Logic/GameLogic.cs`, `Recording/EngineCheckpoint.cs`, `Recording/EngineCheckpointRestorer.cs`, `test/.../PersistableLogicRandomTests.cs`, `test/.../Recording/EngineCheckpointFormatTests.cs`.
4. Update forward-looking docs that name the class: `docs/roadmap/seams.md`, `docs/roadmap/open-questions.md`, `docs/roadmap/implementation-checklist.md`, `docs/roadmap/implementation/track-a-a1-6-operational-recording.md`, `docs/roadmap/implementation/track-w-w1-aggregate-sector.md`, the deep dives under `docs/roadmap/deep-dives/` (`a1-6-operational-recording.md`, `a2-reproducibility.md`, `w0-charter-fixture.md`, `w1-aggregate-sector.md`), and the not-yet-started card `docs/roadmap/agent/cards/WI-W1-03-seeded-streams-poisson.md`.
5. Leave `Done` cards' Result sections (`WI-A1-13` through `WI-A1-16`) unchanged: they record the class name as it was implemented.
6. Run the validation commands below, flip this card's Status, and append a `.exchange` line.

## Files likely to change

- `src/Panopticon.Mathematics/IRandom.cs`
- `src/Panopticon.Simulation/IGame.cs`
- `src/Panopticon.Simulation/Logic/GameLogic.cs`
- `src/Panopticon.Simulation/Recording/EngineCheckpoint.cs`
- `src/Panopticon.Simulation/Recording/EngineCheckpointRestorer.cs`
- `test/Panopticon.Mathematics.Tests/SageRandomTests.cs` (renamed)
- `test/Panopticon.Simulation.Tests/Reproducibility/PersistableLogicRandomTests.cs`
- `test/Panopticon.Simulation.Tests/Recording/EngineCheckpointFormatTests.cs`
- The docs listed in step 4

## Acceptance criteria

- [ ] `grep -rn "SageRandom" src test --include=*.cs` returns no matches.
- [ ] Docs matches remain only in `Done` cards' Result sections (historical record).
- [ ] `SageGame` and `SageGameExtensions` are untouched (3,397+ occurrences; separate decision, out of scope).
- [ ] Focused RNG and recording tests pass unchanged.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PanopticonRandom|FullyQualifiedName~Recording"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not rename `SageGame`, `SageGameExtensions`, `SystemRandom`, `IRandom`, or the `IRandom.cs` file. Do not edit historical Result sections of Done cards. Do not change RNG behavior; the diff is names only.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- List follow-up cards to create here:
  - none (a repo-wide `SageGame` rename, if ever wanted, is its own decision and card)
