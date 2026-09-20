# Task Card `WI-C5-12`: Verify external model tooling

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-12` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4-12`, `WI-C5-01`, `WI-C5-02`, `WI-C5-03`, `WI-C5-04`, `WI-C5-05`, `WI-C5-06`, `WI-C5-07`, `WI-C5-08`, `WI-C5-09`, `WI-C5-10`, `WI-C5-11` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Lock C5 acceptance with install-free Core session and report tests, graphics-backed TestHarness
coverage, and required Model-tab and Map-tab screenshots.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 10 and 11.
- `src/OpenSage.Tools.TestHarness/AGENTS.md`: graphics and screenshot validation workflow.
- `src/OpenSage.Tools.ReplaySketch.Tests/OpenSage.Tools.ReplaySketch.Tests.csproj`.
- `src/OpenSage.Tools.ReplaySketch.Tests/ScenarioCaptureRunnerTests.cs`: conditional graphics-test
  precedent.
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelSessionTests.cs` (new, from `WI-C5-04`).
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelContractTests.cs` (new, from `WI-C5-03`).
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelHostTests.cs` (new, from `WI-C5-07`).
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelRenderingTests.cs` (new, from `WI-C5-08`).
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.gltf` (new, from
  `WI-C4-11`).
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/pbr-materials.bin` (new, from
  `WI-C4-11`).
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/base-color.png` (new, from
  `WI-C4-11`).
- `src/OpenSage.Game.Tests/Content/Importing/Gltf/Fixtures/animated-skin.glb` (new, from
  `WI-C4-11`).
- `src/OpenSage.Tools.ReplaySketch.Maui/MainPage.xaml.cs`: Model and Map screenshot selection.

## Steps

1. Link the committed C4 fixture directory into the ReplaySketch test output when card 7 has not
   already done so, and resolve fixtures from `AppContext.BaseDirectory`.
2. Complete install-free session tests for source replacement, cancellation generations, stale
   result rejection, warnings, pairing, import-option reloads, preview-only updates, artifact
   leases, and disposal.
3. Complete report tests for stable diagnostic mappings, sort and collapse behavior, eligibility,
   deterministic JSON, schema versioning, source hashes, and forbidden machine-specific fields.
4. Add one graphics-backed TestHarness integration for the static PBR fixture with its external
   relative PNG. Assert the image resolves only through the external model root and produces a
   deterministic report and PNG.
5. Add one graphics-backed TestHarness integration for the skinned fixture. Select a stable
   animation descriptor, bind through C2, sample at fixed times, and assert deterministic output.
6. Add one failure integration covering a blocked import or bind. Assert a stable diagnostic code,
   nonzero exit, preserved report, no final PNG, and complete disposal.
7. Exercise same-document and separate-document animation requests and report-only mode across the
   integration set. Graphics-backed cases must return without asserting when no Generals
   installation or headless graphics backend is available, matching
   `ScenarioCaptureRunnerTests`.
8. Capture the Model tab through its fake session and inspect source, summary, preview, options,
   eligibility, and diagnostics.
9. Capture the Map tab in the same build and inspect its layout and content for regression.
10. Run the full non-interactive suite and format check. Mark C5 complete only after every test and
   visual check passes.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelSessionTests.cs` (new, from `WI-C5-04`)
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelContractTests.cs` (new, from `WI-C5-03`)
- `src/OpenSage.Tools.ReplaySketch.Tests/OpenSage.Tools.ReplaySketch.Tests.csproj`
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelToolingIntegrationTests.cs` (new)

## Acceptance criteria

- [ ] Core session and report coverage requires no game installation or graphics device.
- [ ] Static PBR capture proves a relative external image cannot resolve from or shadow game assets.
- [ ] Skinned capture proves stable descriptor selection, C2 binding, and fixed-time sampling.
- [ ] Failure coverage preserves a structured report, publishes no partial PNG, and disposes every
      owned resource.
- [ ] Same-source, other-source, and report-only request paths are covered.
- [ ] Repeated identical graphics requests produce matching report and PNG hashes.
- [ ] Graphics-backed tests skip without failure when the installation or graphics backend is
      unavailable.
- [ ] Model screenshot matches the deterministic fake state.
- [ ] Map screenshot confirms no tab-bar or map-panel regression.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelSessionTests|FullyQualifiedName~ExternalModelContractTests|FullyQualifiedName~ExternalModelViewModelTests"
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~ExternalModelToolingIntegrationTests"
mkdir -p artifacts/replaysketch-c5
REPLAYSKETCH_SCREENSHOT_PATH="$PWD/artifacts/replaysketch-c5/model.png" \
REPLAYSKETCH_SCREENSHOT_TAB=model \
  dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
REPLAYSKETCH_SCREENSHOT_PATH="$PWD/artifacts/replaysketch-c5/map.png" \
REPLAYSKETCH_SCREENSHOT_TAB=map \
  dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not download fixtures or commit third-party model content.
- Do not weaken deterministic assertions, C2 binding rules, C3 texture isolation, or C4 import
  policy to make a fixture pass.
- Do not treat screenshots as optional or accept the Model image without the Map regression image.
- Do not add C6 morph or advanced PBR acceptance to C5.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, set C5 Status to `Done` and tick C5 in the implementation checklist.
- Append a result line to `.exchange`, including test results, artifact hashes, screenshot paths,
  and visual verdicts.
