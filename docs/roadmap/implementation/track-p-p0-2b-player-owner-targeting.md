# Implementation Plan: Track P · P0-2 (cont.): Player/Owner and Definition Targeting

> Track: [Track P: Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0: ReplaySketch arbitrary-command harness](../deep-dives/p0-playable-baseline-harness.md), sub-phase P0-2: Generic definition/player resolution (player half)
> Checklist item: [implementation-checklist.md](../implementation-checklist.md), P0 item "Let `ActionEntry`/scenario target any player/owner and any object definition"
> Companion plan: [track-p-p0-2-generic-definition-resolution.md](track-p-p0-2-generic-definition-resolution.md) covers the definition half, removing `KnownDefinitions`; this plan covers the player/owner half.
>
> Audience: human overview of the implementation plan. Agent context sits below the divider.

## Problem

The exporter attributes every command to a player positionally and assumes a two-player layout:

- The owner player index is `ownerIdx + 2` (`ReplayExporter.cs:75,81,91`), i.e. it assumes scenario
  player 0 maps to engine `PlayerIndex` 2, player 1 to 3, and so on, with no explicit owner field.
- The "enemy" used to build attack positions is hardcoded `enemyIdx = ownerIdx == 0 ? 1 : 0`
  (`ReplayExporter.cs:58`), which only makes sense for exactly two mutually-hostile players.

A baseline skirmish needs ≥3 slots (e.g. one human + two AI, or 2v2) with arbitrary
faction/relationship layouts, and each authored command must be attributable to a specific owner.
Today a third player's commands would be mis-numbered and its "enemy" target would be wrong.

Checklist item AC:
- A scenario with ≥3 players of mixed factions attributes each command to the correct
  `PlayerIndex`/owner.
- A test asserts per-player order attribution.

## Design decision

How an `ActionEntry` names its owner, and how "enemy"-relative positions are resolved with ≥3
players. Options considered:

- A (chosen): Owner is implicit from the `PlayerSlotConfig` that holds the action list (each player's
  `Actions` already belong to that slot), but make the engine `PlayerIndex` mapping explicit and
  centralized rather than the inline `ownerIdx + 2`. Resolve "enemy"-relative position context
  against the first player whose relationship to the owner is `Enemy` (falling back to the existing
  behavior when relationships are absent), not a hardcoded index.
- B: Add an explicit `OwnerPlayerIndex` to every `ActionEntry`. Rejected : redundant with the slot
  that already owns the action list, and invites inconsistency (an action attributed to a slot it
  doesn't live under).
- C: Leave positional mapping; only generalize the enemy lookup. Rejected : does not satisfy the
  ≥3-player attribution AC and leaves the `+2` assumption undocumented.

Rationale: ownership is already structurally encoded by which `PlayerSlotConfig.Actions` list an
action sits in; the real gaps are (1) a single, documented owner→`PlayerIndex` mapping and (2) a
relationship-driven enemy selection. Both are small, testable, and faction/relationship-agnostic.

## Approach

### Centralize the owner → engine PlayerIndex mapping

- Introduce `internal static int ToEnginePlayerIndex(int scenarioPlayerIndex)` (or a small
  `PlayerIndexMap` helper) encoding the current `+2` offset in one place, with a comment explaining
  the offset (engine indices 0/1 are reserved : confirm against `PlayerManager`/replay slot
  numbering during implementation and document the reason). Replace the three inline `ownerIdx + 2`
  uses (`:75,:81,:91`).
- This keeps behavior identical for the existing two-player scenarios while making the mapping
  explicit and unit-testable, and the single source of truth if the reserved-slot count changes.

### Relationship-driven enemy resolution for position context

- Replace `enemyIdx = ownerIdx == 0 ? 1 : 0` (`:58`) with a lookup that picks the enemy used for
  `TerrainValidator.BuildContextPublic(...)` from the scenario's relationships: the first other
  player marked hostile to the owner (using `PlayerSlotConfig.Team` and/or an explicit relationship
  field : see the P1 relationship model in [skirmish-setup.md](../deep-dives/p1-skirmish-setup.md)),
  falling back to "the other player" only when no relationship data exists (preserving today's
  two-player behavior).
- Keep this scoped to choosing the position-context enemy for landmark-relative authoring; full
  relationship semantics in-engine are P1's responsibility, not the exporter's.

### Per-player export loop

- The existing `for (ownerIdx ...)` loop already iterates all `scenario.Players`; once the mapping
  and enemy lookup are generalized, the loop handles N players without further change. Verify the
  frame-sort tie-break (`:103-107`) still orders deterministically across >2 players (it sorts by
  frame then `PlayerIndex`, which is fine).

### Tests (`OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs`)

- `Export_ThreePlayers_AttributesOrdersToCorrectPlayerIndex`: build a 3-player scenario (mixed
  factions), each with one distinguishable command; export; assert each emitted `Order.PlayerIndex`
  matches the expected engine index for its owning slot.
- `ToEnginePlayerIndex_MapsScenarioSlotToEngineIndex`: unit-test the mapping helper directly.
- `EnemyContext_PicksHostilePlayer_NotHardcodedIndex`: with a 3-player relationship layout, assert
  the position context for an owner's landmark-relative action is built against the hostile player,
  not slot 0/1 positionally.

---

> Agent implementation details : the sections below list exact files, methods, and
> tests for agents executing this plan. Human readers can stop here.

## Files to change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | Centralize owner→`PlayerIndex` mapping; relationship-driven enemy lookup; replace `+2` and `enemyIdx` literals |
| `src/OpenSage.Tools.ReplaySketch.Core/Services/TerrainValidator.cs` | If `BuildContextPublic` assumes a single enemy index, accept the resolved enemy (no behavior change for 2-player) |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Add the three tests above |

## Out of scope (later P0 items)

- Removing `KnownDefinitions` / generic definition hashing → companion plan
  [track-p-p0-2-generic-definition-resolution.md](track-p-p0-2-generic-definition-resolution.md).
- Runtime object references (auto-handles / selectors) → [track-p-p0-3-runtime-object-references.md](track-p-p0-3-runtime-object-references.md).
- In-engine player relationship wiring (the `OnNewGame` TODO) → P1
  ([skirmish-setup.md](../deep-dives/p1-skirmish-setup.md)); this plan only consumes relationship data
  for authoring-time enemy selection.
