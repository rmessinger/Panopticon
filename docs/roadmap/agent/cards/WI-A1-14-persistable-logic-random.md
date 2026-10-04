# Task Card `WI-A1-14`: Make logic random persistable

| Field | Value |
|---|---|
| Card ID | `WI-A1-14` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): A1-6a persistable logic random |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A1-13` |
| Diagrams | [A1-6 §3](../../deep-dives/a1-6-operational-recording.md#3-reconstruction-fidelity) and [A1-6 §5.3](../../deep-dives/a1-6-operational-recording.md#53-engine-checkpoint) |

## Goal

`GameLogic.Random` becomes a persistable `SageRandom`, with its six-word state included in logic persistence and no change to the public random contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §3 and §5.3: deterministic random and checkpoint requirements.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:65-72` and `:387-400`: random ownership and persistence.
- `src/Panopticon.Simulation/IGame.cs:205`: `CreateRandom` factory.
- `src/Panopticon.Mathematics/IRandom.cs:74-175`: `SageRandom` state and `Persist` method.
- `test/Panopticon.Simulation.Tests/Reproducibility/SeededRunTests.cs`: same-seed guardrail.

## Steps

1. Change the logic random created for `GameLogic.Random` to `SageRandom` or a persistable wrapper exposing the same `IRandom` calls.
2. Seed it from the run seed through the existing A2-b seed path.
3. Persist and restore the six `uint` words through `GameLogic.Persist` or the checkpoint random section seam.
4. Add a test that advances random draws, persists state, restores it, and proves the next draws match.
5. Run same-seed and golden-baseline guards; stop before regenerating any baseline if output changes.

## Files likely to change

- `src/Panopticon.Simulation/Logic/GameLogic.cs`
- `src/Panopticon.Simulation/IGame.cs`
- `src/Panopticon.Mathematics/IRandom.cs` if a small API helper is needed
- `test/Panopticon.Simulation.Tests/Reproducibility/PersistableLogicRandomTests.cs`

## Acceptance criteria

- [ ] `GameLogic.Random` state round-trips and produces identical subsequent draws after restore.
- [ ] The run seed is the only source of the initial logic random seed.
- [ ] `UnitValidationGround` baseline, `BaselineGoldenFrames_*`, and `SeededRunTests` are treated as stop-and-report guardrails if they change.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PersistableLogicRandom|SeededRunTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not regenerate golden baselines. If a golden or same-seed artifact differs, stop, preserve outputs, and report the failing assertion plus the likely random-stream cause.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A1-15`.
- List follow-up cards to create here:
  - none

## Result

- `IGame.CreateRandom()` now returns `SageRandom` (was `SystemRandom`); `GameClient.Random` inherits the same change (presentation-only consumer). Run seed remains the only initial source via the existing A2-b `Initialize(runSeed)` path in `Game.StartGame`.
- `SageRandom` gained `StateWordCount = 6`, `CopyState(Span<uint>)`, `RestoreState(ReadOnlySpan<uint>)`; base seed is deliberately not restored (reinitialization stays with the seed path).
- `GameLogic.Persist` version bumped 10 -> 11: six random words persisted after `currentFrame`; write mode throws `InvalidStateException` when `Random` is not `SageRandom`; read mode stages the words in `internal uint[]? PendingRandomStateWords` and `ApplyPendingRandomState()` applies them. Application is deferred to the end of `SaveFile` restore because object re-creation during the chunk read itself consumes draws (BuildVariations et al.), which shifted the stream when restored in place.
- Tests: `PersistableLogicRandomTests` (state round-trip through words; logic checkpoint restored into a fresh host produces identical next draws, and the restored draws differ from pre-checkpoint draws so the test is non-vacuous).
- Guardrails: `SeededRunTests` green; `CheckpointCoverageTests` green; `ValidationGroundTests` JSON gates 6/6; live headless validation-ground runs (5 with the swap, 2 parent-code controls) show identical outcomes to parent code. Pre-existing findings recorded, none caused by this card: `ChinaJetMIG` golden 34 vs stable measured 48 (stale golden on parent code too), `GLAInfantryAngryMobNexus` bimodal 25/2 across processes independent of the swap (golden captured the rare fast path), four headless timeouts (Tomahawk, NukeLauncher, Terrorist, SCUDLauncher) and the TroopCrawler gap reproduce identically on parent code. No baseline regenerated.

## Revision: storage-first A1-6 (#147)

- Determinism is no longer a requirement; persisted `SageRandom` stays so a restored host continues from the recorded generator state.
