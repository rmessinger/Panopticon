# Task Card `WI-C4i-06`: Capture legacy rigid proofs

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-06` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4i-04`, `WI-C4i-05`, `WI-C4c-06` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Add deterministic TestHarness captures that prove legacy 10.0.1.0 rigid clips render through the
same path as C4c clips.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 2, 5, and 9.
- C4c capture scenario added by `WI-C4c-06`.
- `src/Panopticon.Tools.TestHarness/StandaloneModelCapture.cs`.
- `src/Panopticon.Tools.TestHarness/ExternalModels/ExternalModelSceneRenderer.cs`.

## Steps

1. Extend the C4c EE2 proof scenario with legacy rigid capture entries for guntank Idle and Walk
   at time 0 and a mid-clip time.
2. Add palisade gate Open and Close captures at time 0 and clip end.
3. Keep the capture input asset keys model-qualified with `#kf=` and archive-relative KF paths.
4. Add capture-guide checks: guntank wheels visibly rotate in Walk, Idle stays still, and palisade
   gate leaves translate in opposite directions for Open and Close.
5. Run the existing non-interactive suite and the installed TestHarness capture command.
6. Mark C4i Done only after captures and tests pass.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/ExternalModels/Ee2NifProofScenario.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`
- Capture guide files owned by the C4c proof scenario, if present.

## Acceptance criteria

- [ ] Legacy proof captures are deterministic and listed beside the C4c captures.
- [ ] Visual checks show guntank wheel motion and palisade gate translation.
- [ ] C4c Galley and Bomber captures still pass.
- [ ] C4i phase status can be marked Done after this card.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add new model formats or DDF state-machine integration.
- Do not implement skinned legacy captures.
- Do not accept unsupported legacy controller families to make captures pass.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, mark the roadmap phase C4i Done during propagation.
- Append a result line to `.exchange`.
