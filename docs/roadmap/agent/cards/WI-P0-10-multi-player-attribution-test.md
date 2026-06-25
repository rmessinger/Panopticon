# Task Card: `WI-P0-10` — Multi-player command attribution test

| Field | Value |
|---|---|
| Card ID | `WI-P0-10` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-2, see [deep dive §3.2](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P0-03` (faction-agnostic definition resolution must be done first) |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/playable-baseline-harness.md#9-architecture-diagrams) |

## Goal

Add a unit test that verifies a scenario with three players of mixed factions routes each player's
commands to the correct engine `PlayerIndex` in the exported orders — confirming that the
`ownerIdx + 2` convention is correct and that no cross-player contamination occurs.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:55-95` — the export loop:
  `ownerIdx` iterates `scenario.Players`; `playerIndex = ownerIdx + 2` is the engine-side
  player index (`+2` because slots 0 and 1 are reserved for neutral/civilian in Generals).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PlayerSlotConfig.cs` — `FactionIndex`, `Actions`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandSpec.cs:83-91` — `CreateOpen(...)` factory.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/CommandArgument.cs` — `PositionCommandArgument`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/PositionSpec.cs` — `NormalizedPosition`.
- `src/OpenSage.Tools.ReplaySketch.Tests/ReplaySketchTests.cs` — test style reference; note
  how `MapMetadataService.CreateForTesting(...)` and `ReplayExporter` are constructed.

## Steps

1. In `OpenSage.Tools.ReplaySketch.Tests`, add a new test class `MultiPlayerAttributionTests`
   (new file `MultiPlayerAttributionTests.cs`).

2. Add one test `ThreePlayerScenario_RoutesCommandsToCorrectPlayerIndex`:
   - Build a `ReplayScenario` with three `PlayerSlotConfig` entries. Assign distinct
     `FactionIndex` values (e.g., 2, 4, 6 — USA, GLA, and any third value). Give each player
     exactly one `ActionEntry` whose `Type` is `null` and whose `Command` is
     `CommandSpec.CreateOpen(OrderType.MoveTo, new PositionCommandArgument(new NormalizedPosition(0.5f, 0.5f)))`,
     with `FixedTiming(100)`.
   - Call `ReplayExporter.Export(scenario, map, seed: 0, tempFile)` using
     `MapMetadataService.CreateForTesting(...)` (match the pattern from `ReplaySketchTests.cs`).
   - Assert the export succeeds (`error == null`).
   - Collect all `MoveTo` orders from the exported `.rep` file (use `ReplayFile.FromFileSystemEntry`
     to read it back). Assert:
     - Exactly three `MoveTo` orders exist (one per player).
     - Their `PlayerIndex` values are exactly `{2, 3, 4}` (no duplicates, no gaps, no
       cross-contamination between players).

3. Add a second test `TwoPlayerScenario_PlayerIndicesStartAtTwo`:
   - Build a two-player scenario (FactionIndex 2 and 4).
   - Assert the two exported `MoveTo` orders have `PlayerIndex` 2 and 3 respectively (not 0/1).

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Tests/MultiPlayerAttributionTests.cs` (new).

## Acceptance criteria

- [ ] `ThreePlayerScenario_RoutesCommandsToCorrectPlayerIndex` passes: three `MoveTo` orders
      with `PlayerIndex` values exactly `{2, 3, 4}`, one per player slot.
- [ ] `TwoPlayerScenario_PlayerIndicesStartAtTwo` passes: `PlayerIndex` values are `{2, 3}`.
- [ ] No existing tests broken.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change `ReplayExporter`, `PlayerSlotConfig`, or the `ownerIdx + 2` convention — this
  card only adds tests that confirm the existing behaviour.
- Do not test AI or game-file-dependent paths; these are pure export unit tests.
- If the `ReplayFile` read-back API is unavailable without game files, assert directly on the
  ordered `(frame, order)` pairs returned by `ReplayExporter.Export` instead.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Let `ActionEntry`/scenario target any player/owner" item in the
  [implementation checklist](../../implementation-checklist.md) (P0-2).
- Append a result line to repo-root `.exchange`.
