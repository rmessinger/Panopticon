# Task Card `WI-A5-09`: Emit complete run bundles from the harness

| Field | Value |
|---|---|
| Card ID | `WI-A5-09` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-3 replay foundation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-07` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Make one headless harness run emit a validated bundle containing the manifest, state artifact,
tiered recording metadata, and structured events.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-scenario-visualization.md`: bundle layout.
- `src/Panopticon.Tools.ReplaySketch.Core/HarnessRunner.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ReplayActionMonitor.cs`.
- `src/Panopticon.Tools.TestHarness/` action recorder and result writer.
- `src/Panopticon.Simulation/Recording/RunRecording.cs`.

## Steps

1. Add the shared bundle writer and reader.
2. Use `RunRecording`, not the T0-only recorder, for complete A5 runs.
3. Persist typed events with frame, category, player/entity identity, and detail.
4. Validate every referenced required file when loading.
5. Add complete, missing-optional-T2, missing-required-file, and portable-move tests.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/`
- Narrow shared code in `src/Panopticon.Tools.TestHarness/`
- Focused tests

## Acceptance criteria

- [ ] One invocation emits the designed directory and files.
- [ ] State and events cover the full completed run.
- [ ] Missing optional T2 data degrades explicitly; missing required data fails.
- [ ] The bundle loads after directory relocation.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RunBundle"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No Results tab or matrix aggregation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
