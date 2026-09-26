# Task Card `WI-C7c-05`: Capture the animated SC2 proof clips

| Field | Value |
|---|---|
| Card ID | `WI-C7c-05` |
| Roadmap phase | [Track C · C7c](../../track-c-external-assets.md): StarCraft II skinned M3 animation |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C7c-04` |
| Diagrams | [C7c architecture diagrams](../../deep-dives/c7c-sc2-skinned-animation.md#13-architecture-diagrams) |

## Goal

Extend `Sc2M3Proof` with deterministic fixed-time captures of the twelve section 2 clips, and confirm bind-pose parity with the static captures.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c7c-sc2-skinned-animation.md` sections 2 and 12.
- `src/Panopticon.Tools.TestHarness/Sc2M3ProofScenario.cs` from `WI-C7b-05`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`: fixed-time animated capture pattern from C4c and C4d.
- `docs/screenshots.md`.

## Steps

1. Add an animated mode to `Sc2M3ProofScenario`. For each section 2 clip, capture at 0, 25, 50, and 75 percent of its duration using fixed-time evaluation, not wall clock.
2. Capture the skinned bind pose and compare it with the C7b static capture within the harness image tolerance.
3. Run twice and assert the PNGs are byte-identical. On a mismatch, follow the determinism stop-and-report rule.
4. Inspect and record in `.exchange`: Marine and Zealot walk cycles, the Zealot blade swing, the Marine shield collapsing in `Spell`, the Zergling tail scaling in `Stand Morph Start`, and the Zealot `.m3a` clip.
5. Write captures to the shared viewer directory.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Sc2M3ProofScenario.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Sc2M3ImportTests.cs` if a parity assertion is added

## Acceptance criteria

- [ ] Captures for all twelve clips exist and are byte-identical across two runs.
- [ ] Bind-pose parity holds against the C7b capture.
- [ ] The section 12 visual checks are recorded with capture paths.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario Sc2M3Proof
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet test src --filter "FullyQualifiedName~Sc2M3"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not change conversion constants or animation rules to make a capture look right. Escalate with captures.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C7c Status to `Done` in the track doc and tick the checklist item.
- Append a result line to `.exchange`.
