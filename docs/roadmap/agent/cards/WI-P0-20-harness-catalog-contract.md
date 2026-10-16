# Task Card `WI-P0-20`: Harness catalog contract records

| Field | Value |
|---|---|
| Card ID | `WI-P0-20` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add the versioned `HarnessCatalog` contract records that the TestHarness serializes and ReplaySketch deserializes, with a JSON round-trip test.

## Context to read (and only this)

- [Deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8) and §8.4: contract shape.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelProtocolJson.cs`: precedent for a versioned JSON contract and its serializer options.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs`: record style in this project.

## Steps

1. Create `src/Panopticon.Tools.ReplaySketch.Core/Model/HarnessLauncher/`.
2. Add records `HarnessCatalog`, `HarnessScenarioDescriptor`, `HarnessParameterDescriptor` with the members in §8.4. `Default` is a `JsonNode?`.
3. Add enums `HarnessScenarioCategory` (`Replay`, `ValidationGround`, `Capture`, `Proof`, `MassBattle`, `Physics`), `HarnessRunModes` (`[Flags]`: `Headless`, `Windowed`), `HarnessProfileInput` (`None`, `File`, `FileOrEmbedded`), `HarnessParameterKind` (`Bool`, `Int`, `Float`, `String`, `Path`, `Enum`, `Json`). Small enums may share a `Types.cs` file per the one-type-per-file exception.
4. Add `HarnessCatalogJson` with `CurrentSchemaVersion = 1`, shared `JsonSerializerOptions` (string enums, camelCase), `Serialize(HarnessCatalog)`, and `Deserialize(string)` that rejects an unknown schema version with a clear exception message.
5. Add `HarnessCatalogJsonTests` in `test/Panopticon.Tools.ReplaySketch.Tests`: round trip of a two-scenario, three-parameter catalog; unknown schema version throws.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Model/HarnessLauncher/*.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessCatalogJsonTests.cs` (new)

## Acceptance criteria

- [ ] Serialize then deserialize yields equal records (including `Default` JSON values and `ExitCodes`).
- [ ] Enums serialize as strings.
- [ ] A payload with `schemaVersion: 2` throws, naming the supported version.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No TestHarness changes; no process launching; no UI.
- Do not reference `TestHarnessConfig` or `BuiltInScenario` from Core.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
