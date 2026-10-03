# Task Card `WI-A1-13`: Audit checkpoint coverage

| Field | Value |
|---|---|
| Card ID | `WI-A1-13` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6a checkpoint coverage audit |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [A1-6 §5.3](../../deep-dives/a1-6-operational-recording.md#53-engine-checkpoint) and [A1-6 §10](../../deep-dives/a1-6-operational-recording.md#10-architecture-diagrams) |

## Goal

Add a checkpoint-coverage audit test that proves which logic state survives `SaveFile.Persist` and records the missing coverage needed before engine checkpoints become a source-edit card.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §3, §5.3, §5.5, and Appendix current-state hooks: checkpoint scope, state hashing, and current persistence hooks.
- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs:53-175`: chunk list and read/write flow.
- `src/Panopticon.Simulation/StatePersister.cs:292-424`: `StateReader` and `StateWriter`.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:65-72` and `:387-400`: logic random source and current logic persistence.
- `src/Panopticon.Simulation/IGame.cs:205`: `CreateRandom` currently returns `SystemRandom`.
- `src/Panopticon.Mathematics/IRandom.cs:45-175`: `SystemRandom` and `SageRandom` state shapes.

## Steps

1. Add `CheckpointCoverageTests` that runs a headless mocked or installed scenario to frame N, writes engine state with `SaveFile.Persist` and `StateWriter`, restores into a fresh host without starting presentation, steps both hosts, and compares per-object persisted bytes.
2. Bypass the `SaveFile.Persist` read-mode `StartCampaign` path for this test restore. Record the seam needed for the production restore path if it is not yet available.
3. Fix the checkpoint presentation-chunk exclusion list so checkpoint writes exclude presentation chunks and include only logic chunks.
4. Enumerate logic modules with missing or incomplete `Persist` state from the byte comparison and source audit.
5. Enumerate every logic RNG source. Call out that `IGame.CreateRandom` returns `SystemRandom`, whose `System.Random` state is not persistable, and that `SageRandom` persists six `uint` words.
6. Record the findings in the card completion notes and `.exchange` result line, not in a new report file.

## Files likely to change

- `test/Panopticon.Simulation.Tests/SaveLoad/CheckpointCoverageTests.cs`
- `src/Panopticon.Simulation/Data/Sav/SaveFile.cs` or the checkpoint exclusion helper it uses
- This card file, completion notes only

## Acceptance criteria

- [ ] `CheckpointCoverageTests` compares uninterrupted and restored per-object persisted bytes after at least one additional frame.
- [ ] Checkpoint persistence excludes presentation chunks by an explicit tested list.
- [ ] The completion notes list missing or incomplete logic module persistence and every logic RNG source found.
- [ ] No report file is added under `docs/roadmap/agent/`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CheckpointCoverageTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not implement the `PNCK` checkpoint format, run identity, or reconstruction cache. Do not regenerate determinism baselines. On a same-seed or golden-baseline deviation, preserve artifacts and report per the repository determinism rule.

## Result

Pending. On completion, replace this line with the missing-persistence list, checkpoint-exclusion
change summary, RNG source list, and follow-up card list.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange` containing the missing-persistence list, RNG source list, and any checkpoint-exclusion changes.
- List follow-up cards to create here:
  - none yet
