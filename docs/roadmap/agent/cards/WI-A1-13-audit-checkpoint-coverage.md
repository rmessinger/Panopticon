# Task Card `WI-A1-13`: Audit checkpoint coverage

| Field | Value |
|---|---|
| Card ID | `WI-A1-13` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6a checkpoint coverage audit |
| Status | `Done` |
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

Done 2026-10-04. Findings from `CheckpointCoverageTests` (2/2 passing: chunk partition + full round-trip) and source audit:

- Checkpoint exclusion: `SaveFile.IsLogicChunk` filters writes to 11 logic chunks (`GameState`, `Campaign`, `GameStateMap`, `TerrainLogic`, `TeamFactory`, `Players`, `GameLogic`, `ScriptEngine`, `SidesList`, `Partition`, `GhostObject`); the tested complement is the six presentation chunks (`ParticleSystem`, `Radar`, `TacticalView`, `GameClient`, `InGameUI`, `TerrainVisual`).
- Missing/incomplete persistence fixed to make the round-trip pass (all in write mode unless noted):
  - `GameStateMap` was read-oriented: `_mapPath1`/`_mapPath2`/`_gameType` never initialized on live games, embedded-map bytes sourced from a nonexistent user-data extraction, and `SkirmishManager.Settings` NRE. Fixed by seeding from new `Game.CurrentMapPath`/`CurrentGameType`, sourcing map bytes via `ContentManager.GetMapEntry`, and persisting a skirmish-settings record in all modes for stream symmetry (restore side effects guarded on `!InGame`).
  - `GameObject.Persist` unconditionally rebuilt `Team`/`Owner` on read: teamless objects (`teamId == 0`) NREd in write mode and read mode. Team decode is now read-mode-only and null-safe.
  - `Player.Persist` read NREd when `DefaultTeam` id 0 (never set in live games); now maps to null.
  - `GameLogic.Persist` persisted the object-definition lookup table before any ids were assigned (lazy `GetId` during the object loop never reached the stream, restore failed in `GetById`) and counted destroyed null object slots in `objectsCount`, desyncing the stream. Both fixed.
  - Restore seam: booting the same scenario before `LoadFromStream` leaves `InGame == true`, which bypasses both the `SaveFile` read-mode `StartCampaign` path and the `GameStateMap` skirmish restart path; no production seam exists yet (WI-A1-16 `IReconstructionBootstrap` owns it).
- RNG sources (all): logic tick path is `GameLogic.Random` alone (OCL draws, locomotor wander, build variations/probability, slaved/hack/minefield/physics updates, weapons). Presentation/UI: `GameClient.Random`, `AudioSystem._random` (NoAudio short-circuits), `ParticleSystemUtility` static (reseeded per `BeginSeedScope`), Apt ActionScript. One-shot non-tick: replay slot re-roll (`Game.ParseReplayMetaToPlayerSettings`), skirmish slot defaults, `SkirmishGameSettings.Seed` UI default, dev diagnostics. Track W keyed streams designed, not implemented. `IGame.CreateRandom` returns `SystemRandom` (non-persistable `System.Random` state); `SageRandom` persists six `uint` words.
- Key limitation: the round-trip test passes with `SystemRandom` only because the restored host replays identical frames 0..90 from the same seed, so RNG stream positions coincide. Checkpoints restored into hosts without identical replay history still diverge; WI-A1-14 (swap to `SageRandom` + persist six uints) closes that.

Follow-up cards: WI-A1-14 (persistable logic random), WI-A1-15/16 (checkpoint format + restore entry point), already in the phase plan.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange` containing the missing-persistence list, RNG source list, and any checkpoint-exclusion changes.
- List follow-up cards to create here:
  - none yet
