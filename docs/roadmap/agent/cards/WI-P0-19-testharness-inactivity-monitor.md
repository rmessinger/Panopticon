# Task Card: `WI-P0-19` — TestHarness inactivity monitor

| Field | Value |
|---|---|
| Card ID | `WI-P0-19` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P0 Architecture Diagrams](../../deep-dives/p0-playable-baseline-harness.md#8-architecture-diagrams) |

## Goal

Replace the hard `MaxReplayFrames` frame cap removed from `OpenSage.Tools.TestHarness` with an
inactivity monitor: forcibly exit only when the replay has genuinely stalled (no
`ActionDispatched`/`ActionCompleted` events and no logic-frame progress for some configurable
window), instead of a fixed frame budget that forces every scenario — long or short — to fit under
one arbitrary ceiling.

## Context to read (and only this)

- `src/OpenSage.Tools.TestHarness/Program.cs` — `RunWindowed` and `RunHeadless` main loops; both
  previously read `config.MaxReplayFrames` to force-exit after a fixed frame count (removed). Note
  the existing `runoutEndLogicFrame` pattern in `RunHeadless` (exits shortly after
  `ActionType.EnemyBaseDestroyed`) as a precedent for frame-counted exit conditions.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayActionMonitor.cs` — `ActionDispatched` /
  `ActionCompleted` events; the natural signal source for "is the replay still making progress".
- `src/OpenSage.Tools.TestHarness/TestHarnessConfig.cs` — where `MaxReplayFrames` used to live;
  add the new config knob(s) here.
- `src/OpenSage.Tools.TestHarness/TestHarness.json` — config file consumers edit; keep documented.

## Steps

1. Add config field(s) to `TestHarnessConfig` (e.g. `InactivityTimeoutLogicFrames`, defaulting to
   something generous like 3000 frames / 100 game-seconds at 30 fps) with an XML-doc explaining the
   semantics: exit only if no action dispatch/completion and no min-required frame progress happens
   within this window, not a hard cap on total run length.
2. In both `RunWindowed` and `RunHeadless`, track the last logic frame at which
   `ActionDispatched`/`ActionCompleted` fired (update it inside the existing monitor event
   handlers already wired in `Program.cs`).
3. Each loop iteration, if `currentLogicFrame - lastActivityLogicFrame > InactivityTimeoutLogicFrames`,
   log a clear `[inactivity-timeout]` message (mirroring the removed `[timeout]` message style) and
   exit (`break`/`game.Exit()` per the existing pattern in each loop).
4. Keep `0`/negative meaning "disabled" for parity with the old `MaxReplayFrames` convention, if a
   disable escape hatch is wanted for CI.
5. Add a headless test (or extend an existing `TestHarness`-adjacent test if one exists) proving a
   scenario with no further deferred actions and no dispatch/completion activity trips the monitor
   and exits, while a scenario making steady progress does not.

## Files likely to change

- `src/OpenSage.Tools.TestHarness/Program.cs`
- `src/OpenSage.Tools.TestHarness/TestHarnessConfig.cs`
- `src/OpenSage.Tools.TestHarness/TestHarness.json`
- Test project colocated with existing TestHarness/ReplaySketch tests, if a suitable harness exists
  for headless coverage; otherwise document why this remains manual-only in the card's completion
  note.

## Acceptance criteria

- [ ] A scenario that stops producing `ActionDispatched`/`ActionCompleted` events and stops making
      logic-frame progress is force-exited within `InactivityTimeoutLogicFrames`, with a clear log
      message distinguishing it from a normal completion.
- [ ] A scenario that keeps completing actions right up to its natural end is *not* cut off early
      (no regression versus the removed fixed-frame cap for the full-build scenario).
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not reintroduce a fixed total-frame cap; the point is to bound *stalls*, not total scenario
  length.
- Do not change `ReplayActionMonitor`'s event semantics — only consume its existing events.
- Do not touch the `runoutEndLogicFrame` post-`EnemyBaseDestroyed` tail-exit logic in
  `RunHeadless`; that is an unrelated, intentional short grace period after a real win condition.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the `implementation-checklist.md` P0 item this card closes.
- Append a result line to repo-root `.exchange`.
