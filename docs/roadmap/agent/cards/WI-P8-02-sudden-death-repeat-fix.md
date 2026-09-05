# Task Card `WI-P8-02`: Sudden-death repeat-elimination fix

| Field | Value |
|---|---|
| Card ID | `WI-P8-02` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-2, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P3 victory/defeat](../../deep-dives/p3-victory-defeat.md#15-sudden-death-and-time-limit-implementation-notes) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P3-05` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

Close a termination hole in `VictorySystem.EvaluatePendingPlayers`: once `Status` is `SuddenDeath`,
a second simultaneous elimination of every remaining active player has no winner to resolve to
(`TryResolveWinningPlayers` returns `null` for every candidate), so the match can get stuck in
`SuddenDeath` forever instead of ever reaching `Decided`. Make repeated mutual elimination while
already in `SuddenDeath` resolve to a `Decided` outcome through an explicit tie-break, not a
crash or infinite hang), matching the design's intent that `SuddenDeath` is always transitional.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:90-95`: sudden-death implementation notes: "the next elimination... becomes `Decided`" assumes a single subsequent elimination, not another simultaneous mutual one.
- `src/OpenSage.Game/Logic/VictorySystem.cs:128-178` (`EvaluatePendingPlayers`), `:248-256` (`EnterSuddenDeath`), `:365-377` (`ShouldEnterSuddenDeath`): the exact code path with the gap. Trace: after `EnterSuddenDeath()`, `_defeatedPlayers` is never populated for the players that triggered sudden death (see `EnterSuddenDeath` does not add to `_defeatedPlayers`); a second call to `EvaluatePendingPlayers` with all remaining players newly defeated again hits `ShouldEnterSuddenDeath` (already `true` when `Status == SuddenDeath`, so the check at line 156 is skipped) and instead falls through to `TryResolveWinningPlayers`, which returns `null` for every candidate because none has a "remaining opponent" but also none is a winner (everyone is defeated); no `TrySetDecidedOutcome` call ever fires, and the newly-defeated players are never added to `_defeatedPlayers` either since the `foreach` at line 162 executes unconditionally... trace carefully, do not assume the exact mechanism without stepping through it.
- `src/OpenSage.Game.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs`: existing sudden-death tests; the fix must not break these; they currently stagger the second round of deaths (per the P0 scenario matrix cell) rather than eliminating everyone simultaneously twice, which is exactly why this gap was never caught.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/TrackPScenarioMatrixTests.cs:477-491` (`MutualEliminationEndState` cell): shows the current test staggers the second wave of destruction one unit at a time specifically to avoid hitting this case.

## Steps

1. Reproduce the gap first: write a failing headless test that destroys every remaining active player's last structure simultaneously while `Status == SuddenDeath` (i.e., two full-mutual-elimination waves in the same or a later check window with no intervening single elimination), and assert the match still reaches `Decided` within a bounded number of `Tick` calls.
2. Fix `EvaluatePendingPlayers`/`EnterSuddenDeath` so a repeat full-mutual-elimination while in `SuddenDeath` resolves through an explicit rule; the design already allows a tie represented by a winner list (see `GameOutcome.WinningPlayers`/`ScoreMetric` tie handling in `WI-P3-06` for the established shared-victory pattern); resolve to a shared-victory `Decided` outcome among the last simultaneously-eliminated cohort, or another clearly documented rule; do not leave it able to loop `SuddenDeath` indefinitely.
3. Ensure the fix does not change behavior for the existing staggered-elimination sudden-death test path (single elimination following sudden death still resolves to `Reason = Eliminated` with a lone or team winner as today).
4. Add the new test to `src/OpenSage.Game.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs`.
5. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/VictorySystem.cs`
- `src/OpenSage.Game.Tests/Logic/Victory/SuddenDeathOutcomeTests.cs`

## Acceptance criteria

- [ ] A scenario with two full-mutual-elimination waves (second wave occurring while `Status == SuddenDeath`) reaches `GameOutcome.Status = Decided` within a bounded frame/tick count, not an infinite `SuddenDeath` hang.
- [ ] The existing single-elimination-after-sudden-death path is unchanged (still resolves to `Eliminated` with the correct winner).
- [ ] The resolution rule for a repeat mutual elimination is explicit and documented in code comments and in the Result section, such as shared victory among the last cohort.
- [ ] New/updated headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SuddenDeath"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the single-mutual-elimination sudden-death entry behavior (`WI-P3-05`'s existing, correct path).
- Do not introduce a hard process exit or unbounded loop as a "fix"; the resolution must be reached within the existing check cadence.
- Do not change `OutcomeReason` semantics established by `WI-P3-01`/`WI-P3-06` (reuse `Eliminated` or the existing shared-victory representation; do not add a new reason unless the existing ones truly cannot represent this case, and document why if you do).

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-2 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
