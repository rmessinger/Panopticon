# Task Card `WI-A5-20`: Export result views headlessly

| Field | Value |
|---|---|
| Card ID | `WI-A5-20` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 headless export |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-18` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Export a selected frame, metric charts, and event table to PNG and self-contained HTML without a
window, MAUI runtime, or GPU.

## Context to read (and only this)

- Export contract in the A5 implementation plan.
- Core results renderer/models from `WI-A5-04` and `WI-A5-18`.
- Existing `FrameCaptureService` and TestHarness headless CLI patterns.
- `docs/screenshots.md`.

## Steps

1. Add a Core export service and command entry point.
2. Render the selected overview frame to PNG through a CPU-capable path.
3. Write self-contained HTML containing the image, metrics, events, and run metadata.
4. Make output deterministic for identical input.
5. Add fixture-based byte/content tests and document the command.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- Narrow command-entry files
- ReplaySketch tests
- `docs/screenshots.md`

## Acceptance criteria

- [ ] One command emits PNG and self-contained HTML.
- [ ] No window, MAUI runtime, or GPU is required.
- [ ] HTML includes metric series and the event table.
- [ ] Identical input produces identical output.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ResultsExport"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No video export or interactive HTML application.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A5-3 when all replayer cards are complete.
- Append a result line to `.exchange`.
