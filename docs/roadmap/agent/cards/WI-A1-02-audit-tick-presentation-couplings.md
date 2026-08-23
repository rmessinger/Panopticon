# Task Card `WI-A1-02`: Audit presentation couplings inside the tick path

| Field | Value |
|---|---|
| Card ID | `WI-A1-02` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-1 |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none (independent of `WI-A1-01`; both feed `WI-A1-03`) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Produce the line-by-line split of `Game.Update` into the logic-only step a headless host must run
and the presentation work it must not, so blockers H3 and H5 in
[a1-headless-host.md §1](../../deep-dives/a1-headless-host.md#1-primary-blockers) have a concrete
removal plan. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-headless-host.md` §1 (H3, H5), §2.3, §2.4.
- `src/OpenSage.Game/Game.cs:914-1000`: `StartRun`, `Update`, `LocalLogicTick`, `LogicTick`,
  `GetTimeInterval`, `EnableFixedLogicTimestep`.
- `src/OpenSage.Game/Game.cs:1024-1028`: `Render`, the boundary the host must never call.
- `src/OpenSage.Game/GameEngine.cs`: which logic services `IGameEngine` already exposes
  (`GameLogic`, `Scene3D`, `Terrain`, `Navigation`, `Quadtree`, `AssetLoadContext`).
- `src/OpenSage.Tools.TestHarness/Program.cs`: the existing headless loop, the integration target
  named in the deep dive's sequencing notes.

## Steps

1. Tabulate every call made from `Game.Update`, `LocalLogicTick`, and `LogicTick`. For each record:
   `file:line`, the call, and a classification of `logic` (must run headless), `presentation` (must
   not run headless), or `mixed` (does both and needs splitting).
2. Explicitly classify the known-contentious calls: `InputMessageBuffer.PumpEvents`,
   `SkirmishManager.Update`, `Scene2D.LocalLogicTick`, `Scene3D.LocalLogicTick`, `Audio.Update`,
   `Cursors.Update`, the `Updating` event, `GameLogic.Update`, `NetworkMessageBuffer.Tick`,
   `Scene3D.LogicTick`, `PartitionCellManager.Update`, and `Scripting.ScriptingTick`.
3. Record which timing state the logic step needs (`_mapTimer`, `MapTime`, `_nextLogicUpdate`,
   `_nextScriptingUpdate`, `LogicUpdateInterval`, `_scriptingUpdateInterval`,
   `CumulativeLogicUpdateError`) versus which is render-only (`_renderTimer`, `RenderTime`).
4. Note how `EnableFixedLogicTimestep` (already used by the P9 Unit Validation Ground) relates to
   the fixed-rate stepper A1-3 needs, and whether it is sufficient or must be generalized.
5. Write the table and the timing-state split into a `## Tick-path presentation audit` section of
   `docs/roadmap/implementation/track-a-a1-headless-host.md`, below the `---` divider. Create that
   file per `WI-A1-01` step 5 if it does not yet exist.
6. Mark any call whose classification is not determinable from the listed context as `ambiguous`
   with a one-line reason.

## Files likely to change

- `docs/roadmap/implementation/track-a-a1-headless-host.md`
- `docs/roadmap/implementation/README.md` (only if `WI-A1-01` has not already added the row)

## Acceptance criteria

- [ ] Every call in `Update`, `LocalLogicTick`, and `LogicTick` appears in the table with a
      classification.
- [ ] The `mixed` calls are listed separately with the specific work that must be split out.
- [ ] The logic-versus-render timing-state split is stated explicitly by field name.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not extract the stepper or move any code. That is `WI-A1-05`.
- Do not change the TestHarness headless loop.
- Do not audit the render pipeline beyond the `Game.Render` call boundary.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A1-1 in [`implementation-checklist.md`](../../implementation-checklist.md) once both
  `WI-A1-01` and `WI-A1-02` are `Done`.
- Append a result line to repo-root `.exchange`, including the count of `mixed` and `ambiguous`
  entries.
- Follow-up cards: none new; `WI-A1-03` consumes this table.
