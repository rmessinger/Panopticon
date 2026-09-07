# Task Card `WI-A5-21`: Configure regional T2 detail capture

| Field | Value |
|---|---|
| Card ID | `WI-A5-21` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-4 divergence localization |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03`, `WI-A5-07`, `WI-A5-09`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Configure bounded T2 capture by frame window and stable domain region, and record the diagnostic
metadata required for later inspection.

## Context to read (and only this)

- T2 contract in the A5 implementation plan and deep dive §5.
- `src/OpenSage.Game/Recording/RunRecording.cs`.
- Scenario recording options from `WI-A5-03`.
- Domain detail projection from `WI-A5-12`.
- Existing T2 recording tests.

## Steps

1. Add scenario options for ring depth, max windows, cooldown, marked frame range, and region key.
2. Route them into `TriggeredDetailCapture`.
3. Filter detail through stable domain region keys.
4. Persist trigger reason, budgets, region, and captured quantity/system descriptors.
5. Add threshold, marked-window, regional-filter, and budget-bound tests.

## Files likely to change

- Scenario recording-option models
- `src/OpenSage.Game/Recording/RunRecording.cs`
- Harness wiring
- Focused tests

## Acceptance criteria

- [ ] Automatic and marked triggers capture the documented windows.
- [ ] Regional capture excludes unrelated entities.
- [ ] Ring/window memory limits are asserted.
- [ ] Manifest records all settings needed for inspection.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TriggeredDetailCapture"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No comparator UI or unbounded full-state trace.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
