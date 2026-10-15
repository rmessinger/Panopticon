# Task Card `WI-P0-24`: ReplaySketch harness catalog client

| Field | Value |
|---|---|
| Card ID | `WI-P0-24` |
| Roadmap phase | [Track P · P0: ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-8, see [deep dive §2.8](../../deep-dives/p0-playable-baseline-harness.md#28-testharness-launcher-ui-p0-8)) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P0-23` |
| Diagrams | [P0 §8.4-8.5](../../deep-dives/p0-playable-baseline-harness.md#84-class-diagram-testharness-launcher-ui-p0-8) |

## Goal

Add `HarnessCatalogClient` in ReplaySketch.Core that locates the sibling TestHarness build, runs `--list-scenarios`, and returns the parsed catalog or a typed error.

## Context to read (and only this)

- `src/Panopticon.Tools.ReplaySketch.Core/Services/TestHarnessPreviewClient.cs`: process launch, stderr capture, error-code style, internal test constructor.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/SiblingBuildExecutableLocator.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/IExternalModelProcess.cs` and `SystemExternalModelProcess.cs`: process seam to reuse or mirror.
- `test/Panopticon.Tools.ReplaySketch.Tests/ExternalModelToolingIntegrationTests.cs`: fake process precedent.

## Steps

1. Add `HarnessCatalogClient` with a public parameterless constructor and an internal constructor taking the locator, a process factory seam, and a timeout.
2. `LoadAsync(CancellationToken)` returns `HarnessCatalogResult` (catalog or error code + message). Codes: `HRN100` executable missing, `HRN101` start failed, `HRN102` non-zero exit (include stderr tail), `HRN103` malformed or unsupported-schema JSON.
3. Reuse the existing process seam if its shape fits; otherwise add a minimal `IHarnessProcess` seam with a `System` implementation.
4. Tests with a fake process: success, each error code, cancellation.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/HarnessCatalogClient.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/HarnessLauncher/HarnessCatalogResult.cs` (new)
- a process seam file, only if the existing one does not fit
- `test/Panopticon.Tools.ReplaySketch.Tests/HarnessCatalogClientTests.cs` (new)

## Acceptance criteria

- [ ] Fake stdout of a valid catalog yields that catalog.
- [ ] Each of `HRN100` to `HRN103` is produced by the matching fake failure.
- [ ] Cancelling the token kills the fake process and returns without hanging.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not modify `TestHarnessPreviewClient` behavior.
- No UI or view model.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching P0-8 item in the [implementation checklist](../../implementation-checklist.md) when its last card lands.
- Append a result line to repo-root `.exchange`.
