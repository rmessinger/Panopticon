# Task Card `WI-C12b-05`: SHOGUN 2 static proof capture

| Field | Value |
|---|---|
| Card ID | `WI-C12b-05` |
| Roadmap phase | [Track C · C12b](../../track-c-external-assets.md): SHOGUN 2 static models and parts |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C12b-04` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

A `Shogun2Proof` built-in scenario captures the castle gate piece, the Armstrong gun, the ashigaru head, and the horse body textured beside a Generals Ranger.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 7, 11 (static import sequence).
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`, `StandaloneModelCapture.cs`, `BuiltInScenario.cs`.
- `docs/screenshots.md` (capture and sharing).

## Steps

1. Add `BuiltInScenario.Shogun2Proof` and `Shogun2ProofScenario.Run(installation, config, reporter)` reading `SHOGUN2_PATH`; missing install fails with the variable name.
2. Import the four proof assets through `PackFileSystem` and `Rmv2ModelImporter`, materialize them, and capture each beside a Generals Ranger for scale with `StandaloneModelCapture`.
3. Report per asset: import time, mesh and vertex counts, bound texture roles, and missing textures (must be 0).
4. Save captures to the screenshot viewer under versioned names and record links in deep dive section 7.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Shogun2ProofScenario.cs` (new), `BuiltInScenario.cs`, `Program.cs` dispatch
- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` section 7 (results)

## Acceptance criteria

- [ ] The scenario exits 0 with four captures and no missing textures.
- [ ] Captures are deterministic across two runs (same PNG bytes, or a recorded tolerance if the renderer is not bit-stable).
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario Shogun2Proof --headless
```

## Out of scope / guardrails

- Animation and composition (C12c).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick C12b in the checklist and update the README Track C progress row.
- Append a result line to `.exchange`.
