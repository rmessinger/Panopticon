# Task Card `WI-A5-13`: Add a bounded short scenario preview

| Field | Value |
|---|---|
| Card ID | `WI-A5-13` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 pre-run preview |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-10`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Validate, render frame zero, and step a configurable small frame budget through the same domain
projection used by replay.

## Context to read (and only this)

- Preview flow in the A5 implementation plan.
- Scenario validator from `WI-A5-10`.
- Domain projection from `WI-A5-12`.
- `src/Panopticon.Simulation/SimulationHost.cs`.
- Existing `MapPreviewRenderer` and ReplaySketch view-model seams.

## Steps

1. Add a headless preview service with a strict frame/time budget.
2. Validate before constructing the host.
3. Return frame-zero and final projected state plus diagnostics.
4. Bind a minimal preview action without moving logic into MAUI.
5. Add valid, invalid, timeout, and frame-count tests.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- Narrow MAUI view/view-model files
- ReplaySketch tests

## Acceptance criteria

- [x] Invalid scenarios start no host.
  - `ScenarioValidator.Validate(scenario, null)` runs structurally before any installation or
    host work; field-path diagnostics returned (`InvalidScenario_ReportsFieldPaths_BeforeAnyInstallationWork`).
- [x] Valid scenarios return frame-zero and bounded final state.
  - Install-backed run: 45-frame budget, frame-zero and final projections with entity counts
    and region keys (`ValidScenario_ReturnsFrameZeroAndFinalState`).
- [x] Preview and replay use the same projection.
  - `RecordedStateProjector.Project` over the preview bundle, identical to the Results view path.
- [x] Timeout/budget exhaustion is explicit.
  - `HitFrameBudget` / `ExceededTimeBudget` plus diagnostic lines; format covered by
    `FormatSummary_CoversNotRun_FrameZero_Final_AndBudgetLines`.
- [x] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ScenarioPreview"
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-preview.png dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No long run, full 3-D playback, or scenario editing.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
