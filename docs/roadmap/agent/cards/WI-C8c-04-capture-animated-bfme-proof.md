# Task Card `WI-C8c-04`: Capture the animated BFME-family proof

| Field | Value |
|---|---|
| Card ID | `WI-C8c-04` |
| Roadmap phase | [Track C · C8c](../../track-c-external-assets.md): BFME-family skinning and animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-C8b-03`, `WI-C8c-01`, `WI-C8c-02`, `WI-C8c-03` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Extend `SageW3dProofScenario` with animated captures of the Gondor soldier, Mumakil, and wolf rider, proving two-bone skins and compressed clips bind with no missing targets and capture deterministically.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4.2, 5, and the proof sequence diagram.
- `src/Panopticon.Tools.TestHarness/SageW3dProofScenario.cs` from `WI-C8b-02`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`: `AnimatedCapturePlan` and the animated capture loop.
- `test/Panopticon.Simulation.Tests/Graphics/ModelSkinningTests.cs`.

## Steps

1. Add an animated plan: BFME `gumaarms_skn` with `gumaarms_skl.gumaarms_flla`, BFME `mumumakil_skn` with `mumumakil_idla` and `mumumakil_runa`, BFME2 `aucrshp_s1_skn` with `aucrshp_s1_idla`, RotWK `kuorcrider_skn` with `kuorcrider_skl.kuorcrider_gbda`. Capture each at three fixed times (0, mid, last frame).
2. Import clips through `W3dAnimationImporter` with the game's resolver and bind them by pivot name. Fail the scenario on any channel whose pivot has no matching bone.
3. Run the plan twice in one process and assert the captures are byte-identical; report any difference per the determinism rule in `AGENTS.md` instead of fixing it.
4. Add an installed test that, for each plan entry, imports the model and clip and asserts zero missing targets, and that the Mumakil `MUMAKIL_MESH` and wolf rider `BROWNWOLF01` meshes have two-bone weighted vertices (measured 2026-10-18; the Gondor soldier has none).
5. Write captures to the shared viewer directory under unique versioned names (GIF per clip, frames under `frames/<topic-version>/`) and inspect for torn or collapsed vertices at joints.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/SageW3dProofScenario.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/SageW3dProofImportTests.cs`

## Acceptance criteria

- [ ] Installed tests pass with zero missing targets.
- [ ] Both capture runs are byte-identical.
- [ ] Captures show the cycles without torn joints; paths recorded in `.exchange`.
- [ ] `W3dImportParityTests` pass unchanged with `CNC_GENERALS_PATH` set.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario SageW3dProof
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~SageW3dProof"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Channel type 15 follows the `WI-C8c-02` decision; do not decode it here.
- No unit gameplay binding or INI import.
- Do not commit game files or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, tick C8c in `docs/roadmap/implementation-checklist.md` and set C8c `Done` in the track doc (unless a `WI-C8c-05` follow-up is open).
- Append a result line to `.exchange`.
