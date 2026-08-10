# Task Card `WI-A5-04`: Load and scrub a headless run in the ReplaySketch results view

| Field | Value |
|---|---|
| Card ID | `WI-A5-04` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring & visualization, sub-phase A5-3 |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-07` (there must be a run output to load) |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) (placeholder; must be replaced before this card is `Ready`) |

## Goal

Add the first slice of the A5-3 results replayer: load a run output written by `WI-A1-07` into
ReplaySketch, scrub its timeline, and chart its metric series and event log. Comparison and
divergence localization are `WI-A5-05`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §4, §6 (A5-3 row), §8.
- `src/OpenSage.Tools.ReplaySketch.Core/`: where the loading and timeline logic must live, per the
  ReplaySketch layering rule that logic stays in `.Core` and stays headlessly testable.
- `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md`: the MAUI-side rules, including the injectable
  seams for statics and the screenshot-mode validation requirement.
- The run-output type added by `WI-A1-07`: the artifact being loaded.

## Steps

1. Add the run-output reader and timeline model to `ReplaySketch.Core`. No MAUI type may appear in
   `.Core`.
2. Add a metric chart series and an event list derived from the run output, both as `.Core` models
   the UI binds to.
3. Add the MAUI results view binding to those models, following the existing tab and view-model
   patterns. Do not restructure existing tabs.
4. Add headless `.Core` tests covering load, timeline scrubbing to a given frame, and metric series
   extraction.
5. Validate the UI with ReplaySketch screenshot mode as
   `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md` requires, and confirm the map panel still
   renders.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/`
- `src/OpenSage.Tools.ReplaySketch.Maui/`
- `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] A run output loads and its timeline scrubs to an arbitrary frame, asserted headlessly in
      `.Core` tests.
- [ ] Metric series and the event log render in the results view.
- [ ] No MAUI type is referenced from `ReplaySketch.Core`.
- [ ] A screenshot-mode run shows the results view and leaves the map panel working.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-results.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-map.png REPLAYSKETCH_SCREENSHOT_TAB=map dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement run comparison or divergence localization. That is `WI-A5-05`.
- Do not implement streaming, LOD, or bounded-memory loading. That is A5-5.
- Do not add the fidelity overlay or headless export in this card.
- Do not put logic in the MAUI layer; `.Core` owns it and the tests prove it.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Note partial progress on A5-3 in [`implementation-checklist.md`](../../implementation-checklist.md):
  headless export and the fidelity overlay remain.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - Headless export of the results view.
  - Domain-parameterized visualization, once the A4 domain seam exists.
