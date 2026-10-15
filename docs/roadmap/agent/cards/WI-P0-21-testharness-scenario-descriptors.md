# Task Card `WI-P0-21`: TestHarness scenario descriptors

| Field | Value |
|---|---|
| Card ID | `WI-P0-21` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-20` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Describe every `BuiltInScenario` value with a `HarnessScenarioDescriptor` (category, summary, modes, profile input, embedded profiles, exit codes), enforced by a coverage test.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`: the enum to cover.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: per-scenario purpose, headless-only notes, and exit-code tables (source for summaries and `ExitCodes`).
- `src/Panopticon.Tools.TestHarness/Program.cs:200-290`: per-scenario dispatch (which scenarios read `ScenarioPath`).
- `src/Panopticon.Tools.ReplaySketch.Core/Services/CaptureProfileLoader.cs`: `EmbeddedCaptureProfileNames()`.
- Contract records from `WI-P0-20`.

## Steps

1. Add `src/Panopticon.Tools.TestHarness/HarnessScenarioCatalog.cs` (internal static) with `IReadOnlyList<HarnessScenarioDescriptor> Scenarios()`: one entry per enum value, `Id` = enum name.
2. Fill fields from `AGENTS.md`. Headless-only scenarios (`EngagementCapture`, `ScenarioCapture`, proof captures) declare `Modes = Headless`. `ScenarioCapture` declares `ProfileInput = FileOrEmbedded` with `EmbeddedProfiles` from `CaptureProfileLoader.EmbeddedCaptureProfileNames()`; the Alpine replay scenarios declare `File`.
3. Every scenario's `ExitCodes` includes `0`; add the documented non-zero codes with their documented meaning.
4. Add `HarnessScenarioCatalogTests` in `test/Panopticon.Tools.ReplaySketch.Tests` (TestHarness internals are visible there).

## Files likely to change

- `src/Panopticon.Tools.TestHarness/HarnessScenarioCatalog.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessScenarioCatalogTests.cs` (new)

## Acceptance criteria

- [ ] Every `Enum.GetValues<BuiltInScenario>()` value has exactly one descriptor; no extra ids.
- [ ] Every descriptor has a non-empty summary, at least one mode, and an exit-code `0` entry.
- [ ] `ScenarioCapture` lists every embedded capture profile name.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not change scenario behavior or `Program.cs` dispatch.
- Parameters belong to `WI-P0-22`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
