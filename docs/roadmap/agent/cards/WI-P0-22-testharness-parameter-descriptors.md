# Task Card `WI-P0-22`: TestHarness config parameter descriptors

| Field | Value |
|---|---|
| Card ID | `WI-P0-22` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-21` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Annotate every public `TestHarnessConfig` property with `[HarnessParameter]` and build `HarnessParameterDescriptor`s from them by reflection, enforced by a coverage test.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`: properties, XML docs (source for descriptions), defaults.
- `src/Panopticon.Tools.TestHarness/TestHarness.json`: comment block naming which knobs apply to which scenario.
- `src/Panopticon.Tools.TestHarness/HarnessScenarioCatalog.cs` from `WI-P0-21`.

## Steps

1. Add `HarnessParameterAttribute` (TestHarness project, internal, property target): `Group`, `Description`, `params string[] Scenarios` (empty = applies to all).
2. Annotate every public property. Groups: `Run` (Scenario, ScenarioPath, Headless, CaptureFrames, KeepReplay), `Install` (Game, GamePath, GltfFixtureRoot), `Display` (Renderer, Fullscreen, FullHud, camera and mouse knobs), `ValidationGround`, `MassBattle`, `EngagementCapture`. Use `nameof(BuiltInScenario.X)` for scenario names. `Scenario` is described but the UI excludes it from editable rows.
3. Add `HarnessScenarioCatalog.Parameters()`: reflect over `TestHarnessConfig`; map types to kinds (`bool`, `int`, `float`, `string` (`Path` when the name ends in `Path` or `Root`), enums and nullable enums to `Enum` with `EnumValues`, other classes to `Json`); `Default` from a `new TestHarnessConfig()` serialized with the program's JSON options.
4. Add `HarnessScenarioCatalog.Build()` returning a `HarnessCatalog` with `CurrentSchemaVersion`.
5. Extend `HarnessScenarioCatalogTests`.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/HarnessParameterAttribute.cs` (new)
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `src/Panopticon.Tools.TestHarness/HarnessScenarioCatalog.cs`
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessScenarioCatalogTests.cs`

## Acceptance criteria

- [ ] Every public `TestHarnessConfig` property has the attribute and appears once in `Parameters()`.
- [ ] Every `Scenarios` entry names a real `BuiltInScenario`.
- [ ] `Renderer` is `Enum` with the `GraphicsBackend` names; `EngagementCapture` is `Json`; `ValidationGroundBaselinePath` is `Path` with `AppliesTo = [UnitValidationGround]`.
- [ ] Defaults equal `new TestHarnessConfig()` values.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not rename, remove, or change defaults of config properties.
- Keep XML docs; the attribute description is a one-line summary, not a copy.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
