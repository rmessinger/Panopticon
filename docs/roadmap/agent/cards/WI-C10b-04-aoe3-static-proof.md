# Task Card `WI-C10b-04`: AoE3 static proof capture

| Field | Value |
|---|---|
| Card ID | `WI-C10b-04` |
| Roadmap phase | [Track C · C10b](../../track-c-external-assets.md): AoE3 DE static Granny models |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C10b-03` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

An `Aoe3GrannyProof` TestHarness scenario renders the musketeer and hussar horse textured beside a Generals Ranger and shows the falconet failing explicitly.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 5 and 7, and the static sequence diagram.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs` (`Run`, env var handling, capture calls), `StandaloneModelCapture.cs`, `BuiltInScenario.cs`, `Program.cs` dispatch.
- `docs/screenshots.md`.

## Steps

1. Add `BuiltInScenario.Aoe3GrannyProof` and `Aoe3GrannyProofScenario.Run`; missing `AOE3DE_PATH` reports an error and a nonzero exit code.
2. Mount `BarFileSystem(AOE3DE_PATH/Game)`, import both models, materialize with `ImportedModelMaterializer`, and capture each beside a Generals Ranger with `StandaloneModelCapture`.
3. Import the falconet and assert the failure names file, section index, and Oodle1.
4. Capture under Xvfb and save uniquely versioned PNGs in the screenshot viewer directory.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Aoe3GrannyProofScenario.cs` (new), `BuiltInScenario.cs`, `Program.cs`
- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` (result)

## Acceptance criteria

- [ ] Captures show both models upright, textured, and at plausible scale next to the Ranger.
- [ ] Falconet failure message matches the expected fields.
- [ ] Evidence linked in the PR from the screenshot viewer.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario Aoe3GrannyProof
```

## Out of scope / guardrails

- Animation (`WI-C10c-04`).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10c-04` to `Ready` if `WI-C10c-01..03` are done.
- Tick C10b in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
