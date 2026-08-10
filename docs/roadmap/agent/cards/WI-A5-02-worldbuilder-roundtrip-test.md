# Task Card `WI-A5-02`: Add the WorldBuilder author, save, load, play round-trip test

| Field | Value |
|---|---|
| Card ID | `WI-A5-02` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring & visualization, sub-phase A5-6 |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A5-01` (the audit says which round-trip steps exist, so the test asserts reality rather than aspiration) |
| Diagrams | Not applicable: A5-6 is WorldBuilder tooling, not the headless-sim pipeline the A5 §8 diagrams cover. |

## Goal

Lock the A5-6 acceptance criterion as an automated test: a normal-sized map with terrain edits and
placed objects round-trips through `EditorMap` create, save, and reopen with its edits intact, and
the saved file loads in the engine. B4a then builds on a tested baseline.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-6-worldbuilder-baseline.md`: the round-trip verdicts from
  `WI-A5-01`. Only assert steps the audit marked implemented; skip or mark the rest as follow-ups.
- `src/OpenSage.Tools.WorldBuilder.Core/EditorMap.cs`: `CreateNew`, `Open`, `Save`, `SaveAs`,
  `IsDirty`, `MapIniContent`, `MapStrContent`.
- `src/OpenSage.Tools.WorldBuilder.Core/NewMapFactory.cs`: blank-map construction.
- `src/OpenSage.Tools.WorldBuilder.Core/HeightMapEdit.cs` and `ObjectPlacementEdit.cs`: the two edit
  types the test applies.
- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj:44-47`: the project reference list, which the
  test needs extended.
- `src/OpenSage.Game.Tests/GameFactAttribute.cs`: the `[GameFact]` attribute for the install-
  dependent half of the test.

## Steps

1. Add a `ProjectReference` to `OpenSage.Tools.WorldBuilder.Core` in
   `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj`, keeping the existing alphabetical order.
2. Add a test under `src/OpenSage.Game.Tests/Tools/WorldBuilder/` that builds a normal-sized map via
   `NewMapFactory`, applies one `HeightMapEdit` and one `ObjectPlacementEdit`, saves via `SaveAs` to
   a temp path, reopens via `EditorMap.Open`, and asserts both edits survived and `IsDirty` is
   false after save. This half needs no game install: keep it in the non-interactive suite.
3. Assert undo and redo of both edits behave correctly before the save, using
   `EditorMap.UndoStack`.
4. Add a second test marked `[GameFact(SageGame.CncGenerals)]` that loads the saved map through the
   engine's map-loading path, proving the play leg of the round-trip. It is skipped in CI, which is
   expected for install-dependent tests.
5. Delete the temp file in the test's cleanup. Do not leave artifacts in the repo or in the output
   directory.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game.Tests/OpenSage.Game.Tests.csproj`
- `src/OpenSage.Game.Tests/Tools/WorldBuilder/` (new test file)

## Acceptance criteria

- [ ] A normal-sized map with a terrain edit and a placed object round-trips through save and
      reopen with both edits intact.
- [ ] Undo and redo of both edit types are asserted.
- [ ] The non-interactive half passes with no game install under
      `dotnet test src --filter Category!=Interactive`.
- [ ] The `[GameFact]` half passes locally with `CNC_GENERALS_PATH` set and skips without it.
- [ ] No temp file survives the test run.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~WorldBuilder"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not fix WorldBuilder bugs the test uncovers. Record them as follow-up cards and, if a step is
  genuinely unimplemented, assert only what the audit said works.
- Do not test large or oversized maps. Normal sizes only; scaling is B4a.
- Do not add a new test project. Extend `OpenSage.Game.Tests`.
- Do not exercise the MAUI UI layer. The round-trip is a Core-level contract.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A5-6 in [`implementation-checklist.md`](../../implementation-checklist.md) once both
  `WI-A5-01` and `WI-A5-02` are `Done`, and set the A5-6 sub-phase Status in
  [`a5-visualization.md`](../../deep-dives/a5-visualization.md) §6.
- Append a result line to repo-root `.exchange`, noting whether B4a's prerequisite is now satisfied.
- Follow-up cards to create:
  - One card per WorldBuilder bug the test uncovered.
