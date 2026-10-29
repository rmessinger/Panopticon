# Task Card `WI-C10c-04`: AoE3 animated proof capture

| Field | Value |
|---|---|
| Card ID | `WI-C10c-04` |
| Roadmap phase | [Track C · C10c](../../track-c-external-assets.md): AoE3 DE skinned animation |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C10c-01`, `WI-C10c-02`, `WI-C10c-03`, `WI-C10b-04` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

The `Aoe3GrannyProof` scenario plays musketeer march and volley clips and hussar idles from animfile resolution, with 0 missing targets and deterministic captures.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` section 5 and the animated sequence diagram.
- `Aoe3GrannyProofScenario.cs`, `StandaloneModelCapture.cs`, `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs` (animated capture pattern).
- `docs/screenshots.md`.

## Steps

1. Resolve Musketeer and Hussar through `Aoe3AnimfileResolver`, import model and clips, and bind through C2.
2. Capture fixed-time frames per clip twice and compare byte-identical; produce a GIF per clip.
3. Report missing-target counts and fail on any.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Aoe3GrannyProofScenario.cs`
- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` (result)

## Acceptance criteria

- [ ] Clips play with 0 missing targets.
- [ ] Repeated captures are byte-identical.
- [ ] GIF evidence in the screenshot viewer, linked in the PR.
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

- Falconet animation (C10d).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick C10c in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
