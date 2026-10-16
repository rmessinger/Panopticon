# Task Card `WI-P0-23`: TestHarness `--list-scenarios` and `--config` CLI

| Field | Value |
|---|---|
| Card ID | `WI-P0-23` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-22` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

`TestHarness --list-scenarios` prints the catalog JSON and exits 0 without a game install; `--config <path>` loads an alternate config file instead of the executable's `TestHarness.json`.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/Program.cs:22-80` (`Main`, argument overrides) and `:170-190` (`LoadConfig`).
- `src/Panopticon.Tools.TestHarness/HarnessScenarioCatalog.cs` from `WI-P0-22`.
- `src/Panopticon.Tools.TestHarness/AGENTS.md` "Running the TestHarness" section.

## Steps

1. In `Main`, before any install resolution, handle `--list-scenarios`: write `HarnessCatalogJson.Serialize(HarnessScenarioCatalog.Build())` to stdout only (no reporter prefixes), exit 0.
2. Change `LoadConfig` to take an optional path. With `--config <path>`: a missing file is an error (exit 1, no default file written); otherwise deserialize with the existing options. Without it, behavior is unchanged.
3. Keep existing `--headless`, `--scenario`, `--built-in-scenario`, `--gltf-fixture-root` overrides applying on top of the loaded config.
4. Extract the stdout write into an internal method returning the string so it is testable; add tests: output deserializes via `HarnessCatalogJson`; `LoadConfig` reads a temp file's values.
5. Document both flags in `AGENTS.md` "Running the TestHarness".

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Program.cs`
- `src/Panopticon.Tools.TestHarness/AGENTS.md`
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessScenarioCatalogTests.cs`

## Acceptance criteria

- [ ] `dotnet run --project src/Panopticon.Tools.TestHarness -- --list-scenarios` with `CNC_GENERALS_PATH` unset prints valid catalog JSON and exits 0.
- [ ] `--config /tmp/x.json` with `"Scenario": "PbrMaterialProof"` runs that scenario; the executable's `TestHarness.json` is unchanged.
- [ ] `--config` to a missing file exits 1 with a message naming the path.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
env -u CNC_GENERALS_PATH dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- --list-scenarios | python3 -m json.tool > /dev/null
```

## Out of scope / guardrails

- Do not change the default config file location or the run pipeline.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
