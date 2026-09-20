# Implementation Checklist

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the dependency-ordered
> implementation checklist for the roadmap. The [track docs](README.md#document-map) hold design
> and rationale, [agent](agent/) holds bounded task cards, and this file is the work list.
>
> Audience: human progress checklist.

### Maintenance

- Check an item off (`- [x]`) when it lands. Keep it aligned with the phase Status in the track
  docs and any matching [agent card](agent/).
- Keep items granular: each one should be a concrete code change or area, not a strategy. Split
  anything too large for one focused change.
- Keep order intact. Track P gates Tracks A/B/C, so P items come first. Within a track, items are
  roughly top-down by dependency.
- Keep rationale in the linked docs instead of repeating it here. Each item carries an `AC:`
  sub-bullet with a verifiable completion check, ideally a [P0 harness](deep-dives/p0-playable-baseline-harness.md)
  assertion or an automated test (`dotnet test src --filter Category!=Interactive`). If a phase is
  still too undefined, mark the AC as `TBD` and tighten it once the deep dive lands.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done · `(↗)` needs a design decision / high-effort
model before it can be done · `AC:` acceptance criteria · `TBD` AC not yet definable.

---

## Track P: Playable Baseline (prerequisite)

See [`track-p-playable-baseline.md`](track-p-playable-baseline.md). Success bar: a full
Generals/ZH skirmish playable build → fight → win/lose vs. AI.

### P0: ReplaySketch arbitrary-command harness ([deep dive](deep-dives/p0-playable-baseline-harness.md))
- [x] Replace the closed `ActionType` enum with an open command model that can express any engine
      `OrderType` (`OpenSage.Tools.ReplaySketch.Core/Model`).
  - AC: a scenario can specify a command as `OrderType` + typed args and it exports to the matching
    engine `Order`; a unit test covers ≥1 command per `OrderArgumentType`.
  - Completion: `CommandSpec` model created with polymorphic `CommandArgument` hierarchy (5 types).
    `ActionEntry.Type` is a stored `ActionType?` field (null for open commands); `CommandSpec` holds
    only `OrderType + Arguments`. `ReplayExporter.BuildOrders` dispatches on `ActionType` enum for
    legacy actions. All 14 ReplaySketchTests pass; `alpine-assault-usa-vs-gla.json` loads correctly.
- [x] Add screenshot-and-exit mode to `MainPage` so agents can capture the MAUI-layer UI without
      manual app interaction.
  - AC: setting `REPLAYSKETCH_SCREENSHOT_PATH=/tmp/out.png` before `dotnet run` causes the app to
    render, wait for the configured settle delay, capture via `Screenshot.Default.CaptureAsync()`,
    save a PNG, and exit; `REPLAYSKETCH_SCREENSHOT_TAB=map` switches to the Map tab before capture;
    `REPLAYSKETCH_SCREENSHOT_DELAY=N` overrides the 3-second default; an `.error` file is written
    on failure; `AGENTS.md` documents the complete workflow.
  - Completion: implemented in `MainPage.OnLoaded`; `AGENTS.md` updated with screenshot section,
    validation checklist step, and failure-mode table rows. Build confirmed (0 errors).
- [x] Extract an `IPreferencesStore` interface from `MainViewModel` (currently uses `Preferences.Default`
      directly, a MAUI static); inject it so `LoadMapFromFolder`'s unconditional path-persistence is
      unit-testable without a MAUI runtime.
  - AC: `MainViewModel` accepts an `IPreferencesStore` via constructor injection (production code uses
    the `Preferences.Default` implementation; tests supply a `MemoryPreferencesStore`); a unit test
    calls `LoadMapFromFolder` with a valid directory path where map loading will fail (no game files),
    and asserts the preference was saved regardless of map load outcome.
  - Completion: `IPreferencesStore` + `MemoryPreferencesStore` in Core; `MauiPreferencesStore` adapter
    in MAUI; `MainViewModel` constructor takes optional `IPreferencesStore` (defaults to
    `MauiPreferencesStore`); both `Preferences.Default` call sites replaced. `MemoryPreferencesStore`
    headless tests added to `PreferencesStoreTests.cs`. The `LoadMapFromFolder` behavioral test is
    blocked by TFM mismatch (test project is `net10.0`; MAUI project is `net10.0-maccatalyst`).
    Tracked as follow-up in P0-1c. All 56 tests pass; formatting clean.
- [x] Abstract the remaining MAUI statics `MainViewModel` binds to: the `MainThread` dispatcher
      (background-load completion, `MainViewModel.cs:180-190`) and `Process.Start`/launcher
      (`ExecutePlayInGame`, `MainViewModel.cs:250-273`) through small injectable seams.
  - AC: `MainViewModel` takes a dispatcher seam (runs the continuation synchronously in tests) and a
    process-launcher seam (records the request instead of spawning); unit tests in
    `OpenSage.Tools.ReplaySketch.Tests` exercise `ValidateCommand`, `ExportCommand`, `PlayInGameCommand`,
    and the apply-results half of the background load headlessly (no MAUI runtime), asserting
    command-state transitions and that `PlayInGame` records the expected launcher invocation.
- [x] Remove hardcoded USA/GLA `KnownDefinitions`; resolve object definitions by name generically
      (`Services/ReplayExporter.cs`).
  - AC: a scenario referencing any object definition by name (incl. a non-USA/GLA faction) exports
    orders with the correct definition hash; no faction names remain hardcoded in `ReplayExporter`;
    test resolves a definition from a non-default faction.
- [x] Let `ActionEntry`/scenario target any player/owner and any object definition.
  - AC: a scenario with ≥3 players of mixed factions attributes each command to the correct
    `PlayerIndex`/owner; test asserts per-player order attribution.
- [x] Reference runtime-created objects via implicit auto-handles (keyed to the producing
      command) with opt-in explicit names; reference world/enemy objects via query selectors
      (faction/relationship/definition/proximity), resolved at dispatch.
  - AC: a scenario issues `AttackObject`/`Enter` against an object produced by an earlier command
    without the author writing an id, and another command targets an enemy structure via a selector;
    both resolve in a headless run (state assertion passes).
- [x] Map the gameplay-relevant `OrderType` set (move, attack, construct, produce, ability) to
      exporter order emission.
  - AC: for each of move/attack/construct/produce/ability, an authored command produces the expected
    `OrderProcessor` effect in a headless run (state assertion passes).
- [x] Add a validation/assertion layer (expected post-conditions, invariants, outcomes) runnable
      headlessly with a pass/fail result.
  - AC: assertions are authored as JSON predicates (`{path, op, value}`) over a fixed path/operator
    vocabulary spanning ≥1 outcome, ≥1 state, and ≥1 per-tick invariant type, with a code-defined
    (C#) predicate escape hatch; a headless run emits a machine-readable pass/fail report and a
    deliberately-failing assertion is reported with frame + expected/actual.
- [x] Provide a headless run-and-assert entry point cheap agents can call (scenario/command file
      in → result out).
  - AC: one CLI/library call takes a scenario/command file and returns exit code 0/non-zero + a JSON
    report; documented invocation; runnable headlessly in CI.
- [x] Tests: author + validate an arbitrary command set end-to-end.
  - AC: an automated test authors a multi-command scenario, runs it headlessly, and asserts
    post-conditions; passes under `dotnet test src --filter Category!=Interactive`.
- [x] Scenario matrix runner: accept a `ScenarioMatrix` file with named sweep axes; expand to
      one run per cartesian-product cell; emit a per-cell keyed result and aggregate JSON report.
  - AC: a matrix file with ≥2 axes (e.g. faction × map) produces one run per cell; per-cell
    results are keyed by axis combination; the aggregate report is valid JSON with
    `totalCells/passed/failed/cells`; exit code 0 iff all cells pass; a matrix with one
    deliberately-failing cell exits non-zero and reports which cell failed with assertion detail.
  - Note: this is the expansion primitive A5-2 (experiment matrix tooling/visualizer) builds on.
- [x] Support arbitrary command volume: programmatic/procedural generation of large order sets
      (not hand-entry) and efficient `.rep` emission, so the harness scales toward sim-scale runs.
      (Full authoring/replay scaling is finalized under A5-5.)
  - AC: a scenario with ≥10k programmatically-generated orders exports and loads within a documented
    time/memory budget; test generates the orders without per-action hand-entry.
- [x] Check in an agent validation playbook at `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md`: the
      documented, repeatable checklist + exact commands agents run to validate a ReplaySketch change
      (Core test suite, screenshot mode for all visual verification, `dotnet format`, the run-and-assert
      flow, and the MAUI testability seams), mirroring `OpenSage.Tools.TestHarness/AGENTS.md`.
  - AC: `ReplaySketch.Maui/AGENTS.md` exists and documents the Core-vs-MAUI split, the exact build/
    test/format commands, the screenshot mode (env vars, commands, error handling), what is
    headlessly validatable vs MAUI-only, a step-by-step validation checklist (including a screenshot
    step for visual changes), and a common-failure-mode table; it is honest about current capability
    (marks the P0-4/P0-5 run-and-assert flow as forthcoming) and is referenced from the P0 deep dive.
  - Completion: `AGENTS.md` created with all sections; screenshot mode (P0-1a) is the sole visual
    verification method; golden-image framework removed.
- [ ] Decouple replay-driven selection state from interactive UI selection state, so a human
      observer in `OpenSage.Tools.TestHarness` (or a live spectator) can drag-select units without
      the recorded replay's own `SetSelection`/`ClearSelection` orders overwriting their selection or
      `OrderGenerator.ActiveGenerator`.
  - AC: while replaying a `.rep` with `OpenSage.Tools.TestHarness`, a human click-and-drag over units
    shows the selection rectangle and yields the dragged units as selected, even on frames where the
    replay itself issues a `SetSelection`/`ClearSelection` order for the same player; a headless test
    asserts the human's `SelectionSystem` state survives a replay-issued selection order on the same
    frame.
  - Note: follow-up to the "Allow box-selecting any object's stats while watching a replay" fix
    (`SelectionSystem.MultiSelect`); see `WI-P0-18`.
- [ ] Add an inactivity monitor to `OpenSage.Tools.TestHarness` to replace the removed fixed
      `MaxReplayFrames` cap: force-exit only when the replay stalls (no
      `ActionDispatched`/`ActionCompleted` progress for a configurable window), not on total run
      length, so long scenarios (e.g. the full-build replay) aren't truncated early.
  - AC: a stalled scenario (no further action dispatch/completion) force-exits within the configured
    window with a distinct `[inactivity-timeout]` log message; a scenario making steady progress to
    its natural end is not cut off early.
  - Note: see `WI-P0-19`.

### P1: Skirmish setup & player relationships ([deep dive](deep-dives/p1-skirmish-setup.md))
- [x] Resolve `PlayerManager.OnNewGame()` `// TODO: Setup player relationships` (allies / enemies /
      neutrals): `src/OpenSage.Game/Logic/PlayerManager.cs:45`.
  - AC: after `OnNewGame`, every player pair has a defined `RelationshipType` (no TODO/defaults left);
    a 2-player human-vs-AI skirmish reports them as mutual enemies via `GetRelationship`; unit test.
  - Done: `WI-P1-02` (commit d5acbbb7). Team-derived relationships wired in `OnNewGame`; covered by
    `PlayerManagerTests`.
- [x] Wire skirmish slot/team data from `SkirmishManager` into relationship setup; make ally/enemy
      wiring reciprocal (`Player.AddAlly/AddEnemy`, `Logic/Player.cs:779-786`).
  - AC: same-team slots are allies and different-team slots are enemies; `A ally/enemy B ⇒ B ally/enemy
    A` (test asserts symmetry).
  - Done: `WI-P1-02`/`WI-P1-03` (commit d5acbbb7). Reciprocity + symmetry covered by
    `PlayerRelationshipTests`.
- [x] Start a single-player skirmish without networking (`LocalSkirmishManager` path).
  - AC: `LocalSkirmishManager` reaches `Started` with no network connection; a headless boot reaches
    the first logic tick.
  - Done: `SkirmishManager.Update()` now guards its lobby `PopWindow()` against an empty window
    stack so the path boots headlessly (no menu window); `LocalSkirmishManager` already uses an
    `EchoConnection` (no network). Covered by `LocalSkirmishManagerBootTests` (headless boot to
    `Started` + first logic tick, skips on CI without a game install) and the always-on
    `LocalSkirmishManagerStartTests` (start transition + `EchoConnection`, no game files).
- [x] Unify SP/MP spawn onto one shared skirmish path (SP = one human + AI slots through the same
      start-position/building/camera code, `Game.cs:745-763`); identify the human via an explicit
      slot flag set at setup, not the `_players[2]` fallback (`PlayerManager.cs:39-46`).
  - AC: a SP skirmish spawns each non-neutral player's starting building(s) at its `Player_N_Start`
    and places the camera, identical to the MP path; `LocalPlayer` resolves from the explicit flag;
    harness asserts each player has ≥1 starting structure at its start waypoint.
  - Done: `WI-P1-04` (commit d5acbbb7). `LocalPlayer` now resolves from `IsLocalForMultiplayer`;
    SP skirmish already used `GameType.Skirmish` so the shared spawn/camera path applied without a
    guard change. No dedicated harness assertion for the "≥1 starting structure per player" clause
    yet; see the open P1-4 boot-validation item below.
- [x] AI player slots, faction/army selection, and start positions (fill AI slots in the lobby path).
  - AC: an unset AI slot is assigned a valid playable side, a free color, and a free start position
    before `StartGame`; no slot has an unresolved faction/position at game start.
  - Done: `WI-P1-05` (commit 677b7bc9), with a start-position collision bug fixed by `WI-P1-09`
    (commit 583d09de). Shared `SkirmishSlotDefaults.ApplyDefaults(...)` helper used by both paths.
- [x] P1-4 Boot validation: a human + ≥1 AI skirmish boots headlessly via the P0 harness with
      correct relationships, spawn positions, and faction assignments asserted.
  - AC: `P1BootValidationTests` passes (or skips cleanly on CI without game files); predicates for
        symmetric relationships, non-empty spawn, and `LocalPlayer` identification all pass.
  - Done: `WI-P1-06`. `P1BootValidationTests` registers three `CodeAssertion` predicates and runs
        them through the P0 `HarnessRunner` (skips when no playable install); predicate logic is
        additionally covered headlessly by `P1BootValidationPredicateTests` (mocked game, CI-runnable).
- [x] P1-5 Object-ownership tracking (foundational): maintained per-`Player` building/unit counts +
      owned-object set, updated on create/transfer/`OnDie`/`Destroy` (decided, not an on-demand scan).
      Consumed by P2 (AI target selection) and P3 (defeat detection); friendly to the scale tracks.
  - AC: per-`Player` building/unit counts are accurate after create/death/transfer (test creates +
    kills objects and asserts counts) with O(1) per-check cost.
  - Done: `WI-P1-07` (commit 8518ed49) + `WI-P1-11` (commit 122ed1eb, owner-transfer tracking).
    Covered by `PlayerOwnershipTests`.
- [x] P1 scenario matrix: sweep playerCount × teamConfig × factionMix; assert relationships and
      faction assignment are correct for every cell.
  - AC: a `ScenarioMatrix` (axes: `playerCount` ∈ {2,3,4,6,8}, `teamConfig` ∈ {FFA, 2v2, 3v1, 4v4},
    `factionMix` ∈ {mirror, mixed, all-factions}; invalid axis combinations pruned by ConstraintFilter)
    expands to one boot-and-assert run per cell; every cell asserts correct ally/enemy relationships
    and faction assignments; see the deep dive matrix section.
  - Done: `P1_ScenarioMatrixTests` in `TrackPScenarioMatrixTests.cs`. The 60-cell cartesian space
    prunes to 26 valid cells; each cell boots a mocked game via `PlayerManager.OnNewGame` and asserts
    reciprocal relationships (same-team allies, different-team enemies), faction match per slot, and
    neutral/civilian neutrality. CI-runnable (no install).

### P2: Skirmish AI opponent ([deep dive](deep-dives/p2-p5-skirmish-ai.md))
- [x] Add a coarse-cadence `Update(...)` decision loop to `SkirmishAIPlayer`/`AIPlayer` (currently
      persistence-only, `Logic/AI/AIPlayer.cs:8-154`, `SkirmishAIPlayer.cs:3-31`).
  - AC: `SkirmishAIPlayer.Update(...)` is invoked from the player tick at the intended cadence (test
    asserts call count over N frames); no behavior required yet.
- [x] Actuate AI decisions by emitting `Order`s through `OrderProcessor` (decided, not direct API),
      so AI actions enter the recorded stream.
  - AC: a thin actuation adapter emits `Order`s; AI-issued actions appear in the recorded order stream
    (verified by replaying the run).
- [x] AI base-building: consume the stranded `AIData.SkirmishBuildLists`/`BuildListInfo`
      (`Logic/AI/AIData.cs`, `Logic/Map/BuildListInfo.cs`).
  - AC: starting from a base + dozer/worker, the AI builds the build-list structures in priority order;
    a headless run shows the AI's building count rising toward the build-list target within a frame
    budget.
- [x] AI economy management (dozers/workers + supply).
  - AC: the AI maintains ≥1 supply structure and assigns harvesters; AI `BankAccount` income is
    positive over a sampled interval in a headless run.
- [x] AI unit production (money/prereq gated via `Player.CanProduceObject`).
  - AC: the AI produces ≥N army units gated by money/prereqs; production is blocked when unaffordable
    (balance never < 0).
- [x] AI attack/defense behavior (target via P1 relationships): Track P scope is a minimum legal
      opponent (build/economize/produce/≥1 attack → terminating match); structure for configurable
      difficulty tiers but implement only the minimum tier now.
  - AC: once a threshold force exists, the AI moves units toward an enemy structure (via P1
    relationships) and engages; a headless match shows AI-initiated combat and can reach a win/lose
    outcome.
- [x] P2 scenario matrix: sweep humanFaction × aiFaction × mapSize; assert the AI runs a legal
      game for every faction and map combination.
  - AC: a `ScenarioMatrix` (axes: `humanFaction` × `aiFaction` ∈ {USA, GLA, China}, `mapSize` ∈
    {small, medium}) runs each of the 18 cells; each asserts AI built ≥1 structure, produced ≥1
    unit, issued ≥1 attack order, and kept `BankAccount >= 0`; see the deep dive matrix section.

### P3: Victory & defeat conditions ([deep dive](deep-dives/p3-victory-defeat.md))
- [x] Add a `GameOutcome` type + a `VictorySystem : GameSystem` ticked from the logic loop.
  - AC: `GameOutcome` is queryable off `GameLogic`/`IGame` and reads `InProgress` during play;
    `VictorySystem` ticks from the logic loop (test asserts it runs).
- [x] Player-defeat detection: real Generals rule (no command/production structures and no means to
      rebuild: no dozer/worker + insufficient resources) as the default, exposed as configurable.
  - AC: a harness scenario that eliminates a player's qualifying structures + rebuild means flags that
    player defeated within the check cadence; the rule is selectable via config.
- [x] Death→defeat event from `GameObject.OnDie/Destroy` (shared with P6-3 cleanup).
  - AC: a death/ownership-change notification fires from `OnDie`/`Destroy` and `VictorySystem` reacts
    without a full-object scan; test asserts the event fires once per death.
- [x] Win/lose determination + match-end flow (stop sim, surface result, allow exit).
  - AC: when all non-allies of player X are defeated, `GameOutcome.Status=Decided` with X as winner;
    the sim stops/relaxes and exit is possible; harness asserts the decided outcome.
- [x] Handle the `EndGame` order in `OrderProcessor` (currently unhandled).
  - AC: an `EndGame` order produces a `Decided` `GameOutcome` (surrender/quit) instead of logging
    "unimplemented"; test issues `EndGame` and asserts the outcome.
- [x] Expose match outcome as a first-class queryable result (for headless assertion).
  - AC: the P0 harness can assert on `GameOutcome` (winner/loser/reason/frame); covered by a harness
    test.
- [x] Mutual elimination → sudden-death handling (P3-5).
  - AC: a scenario where all remaining active players' last structures are destroyed in the same
    check window transitions `GameOutcome.Status` to `SuddenDeath` and does not terminate the match;
    play continues until the next elimination, which sets `Status=Decided` with `Reason=Eliminated`.
- [x] Time-limit → score decision (P3-6).
  - AC: a scenario with a configured frame-count limit expires and sets `Status=Decided`,
    `Reason=ScoreDecision`, with `WinningPlayer`/`WinningTeam` set to the highest-score side per the
    configured metric; test covers both a clear winner and a tie (shared victory).
- [x] P3 scenario matrix: sweep endState × playerCount; assert the correct `GameOutcome` is
      produced for every end-state and player-count combination.
  - AC: a `ScenarioMatrix` (axes: `endState` ∈ {PlayerEliminated, PlayerSurrenders, AISurrenders,
    TeamWin, MutualElimination, ScoreDecision}, `playerCount` ∈ {2, 3, 4}; invalid combinations
    pruned) runs one scripted scenario per cell; each asserts `GameOutcome.Status`,
    `GameOutcome.Reason`, and winning/losing sides; see the deep dive matrix section.

### P4: Economy loop end-to-end ([deep dive](deep-dives/p4-p6-economy-production-combat.md))
- [x] Verify/complete supply collection → `BankAccount.Deposit` cycle (`BankAccount.cs:41-60`).
  - AC: a harness scenario with a harvester shows `BankAccount` balance increasing after a supply
    dump (assert positive delta).
- [x] Confirm/implement construction/production debit `BankAccount.Withdraw` with cancel
      refunds (`ProductionUpdate.cs:472-549`, `OrderProcessor.BuildObject`). Audit could not confirm.
  - AC: queuing a unit/structure reduces `BankAccount` by its cost; cancel refunds; insufficient funds
    blocks the action; balance never < 0 (harness asserts all four).
- [x] Power model interactions (`Player.GetEnergy`/`_hasInsufficientPower`, `Player.cs:41,92-104`).
  - AC: TBD, verify intended effect. Rough template: when consumption > production,
    `_hasInsufficientPower` is set and has a defined effect (e.g. production slowdown/disabled
    structures); test toggles power and asserts the effect.
- [x] Close gaps in supply-truck / worker / dozer cycles.
  - AC: worker/dozer/supply-truck complete a full gather→dump cycle without stalling over a sampled
    run (no stuck states).
- [x] P4 scenario matrix: sweep faction × supplyDensity × startingMoney; assert the economy
      loop runs correctly for every combination.
  - AC: a `ScenarioMatrix` (axes: `faction` ∈ {USA, GLA, China}, `supplyDensity` ∈ {sparse, normal,
    rich}, `startingMoney` ∈ {low, normal}) runs 18 cells; each asserts `BankAccount >= 0` at all
    ticks, income increased at least once, and ≥1 unit was produced; see the deep dive matrix section.

### P5: Construction & production completeness ([deep dive](deep-dives/p2-p5-skirmish-ai.md) §1.4/§2.4)
- [x] Player and AI building placement (`OrderProcessor.BuildObject` + `IBuilderAIUpdate`).
  - AC: both a player order and an AI request place and construct a structure to completion at a valid
    location; harness asserts a finished building for each.
- [x] Faction build trees with prerequisites / tech gating (`Player.CanProduceObject:357-420`).
  - AC: a unit/structure with unmet prerequisites is non-buildable and becomes buildable once the
    prereqs exist; test asserts gating for ≥1 prereq chain.
- [x] Unit/upgrade production from structures, end-to-end (`ProductionUpdate.QueueProduction/QueueUpgrade`).
  - AC: a queued unit spawns at the rally point and a queued upgrade completes and applies its effect;
    harness asserts both.
- [x] Locate + verify the control-bar buildability consumer of
      `GameObject.CanPurchase/CanConstructUnit/CanEnqueueUpgrade` (audit did not find the UI binding).
  - AC: the UI consumer of those methods is identified and documented; buildable/unbuildable state in
    the control bar reflects prereqs + money.
- [x] P5 scenario matrix: sweep faction × buildTreeDepth × playerType; assert prerequisite gating
      and production work across all combinations.
  - AC: a `ScenarioMatrix` (axes: `faction` ∈ {USA, GLA, China}, `buildTreeDepth` ∈ {tier1, tier2,
    tier3}, `playerType` ∈ {human, AI}) runs 18 cells; each asserts the gated unit/upgrade is
    unavailable before prereqs and available after; tier3 cells issue the full prerequisite chain;
    see the deep dive matrix section.

### P6: Combat & command verification ([deep dive](deep-dives/p4-p6-economy-production-combat.md) P6)
- [x] Command coverage matrix: every baseline control-bar action → `OrderType` → real
      `OrderProcessor` handler; assert each via the harness.
  - AC: each baseline control-bar action maps to a handled `OrderType`; a harness command per action
    produces the expected effect; the matrix is documented with pass/fail.
- [x] Verify weapon / damage-type / death coverage for baseline-match units
      (`Weapon`/`DamageModule`/`ActiveBody`/`FireWeaponUpdate`).
  - AC: for baseline units, weapon fire reduces target health via `DamageModule`/`ActiveBody` and
    triggers death at 0; harness asserts a kill.
- [x] Death cleanup: selection invalidation + owner-tracking removal (share P3-4's death event).
  - AC: a dying selected unit is removed from selection and owner tracking (reusing the P3-4 event);
    test asserts no dangling references.
- [ ] Fill gaps surfaced by the P7 match run.
  - AC: TBD, populated as the P7 run surfaces defects. Rough template: each surfaced defect gets its
    own checklist item + a regression assertion in the harness.
- [x] P6 scenario matrix: sweep orderType × faction; assert each command type produces the correct
      `OrderProcessor` effect for every faction.
  - AC: a `ScenarioMatrix` (axes: `orderType` ∈ {MoveTo, AttackObject, BuildObject, CreateUnit,
    SpecialPower}, `faction` ∈ {USA, GLA, China}) runs 15 cells; each asserts the observable effect
    for that faction's reference unit; see the deep dive matrix section.

### P6b: Unit-contract validation ([deep dive](deep-dives/p6b-unit-contract-validation.md))
- [x] Infantry archetype contract (P6b-1).
  - AC: a P0 harness scenario for the reference infantry unit (e.g. USA Ranger) asserts all six
    contract points: produced, moves, garrison via `Enter`, basic attack reduces target health,
    every `SpecialPowerType` in its command set produces an observable effect, dies with ownership
    cleanup; unimplemented special-power handlers are logged as explicit gaps.
- [x] Vehicle archetype contract (P6b-2).
  - AC: a P0 harness scenario for the reference vehicle unit asserts all six contract points:
    produced, moves, basic attack, weapon-mode upgrade activates the expected `WeaponSlot`, dies;
    unimplemented handlers logged.
- [x] Aircraft archetype contract (P6b-3).
  - AC: a P0 harness scenario for the reference aircraft unit asserts all six contract points:
    produced, moves (airborne), attack, land/takeoff cycle, special ability (if applicable), dies;
    unimplemented handlers logged.
- [x] Builder archetype contract (P6b-4).
  - AC: a P0 harness scenario for the reference dozer/builder asserts all six contract points:
    produced, moves, constructs a building to completion, repairs a structure, dies; no production
    or construction order left unhandled.
- [x] Support archetype contract (P6b-5).
  - AC: a P0 harness scenario for the reference support unit asserts all six contract points:
    produced, moves, primary special ability produces an observable effect (health restored,
    hacking applied, ECM active, etc.), dies; unimplemented handlers logged.
- [x] Superweapon archetype contract (P6b-6).
  - AC: a P0 harness scenario for the reference superweapon structure asserts all six contract
    points: built, charge cycle advances per tick, `SpecialPowerAtLocation` fires and produces
    area effect, cooldown state is set post-fire, structure can be sold; unimplemented handlers
    logged.
- [x] All unimplemented special-power handlers surfaced by P6b-1 through P6b-6 are fixed or
      documented as explicit out-of-scope gaps with a follow-up item.
  - AC: every `SpecialPower*` order exercised by the six scenarios either passes its assertion or
    has a documented gap item; none silently succeed without effect.
- [x] P6b scenario matrix: sweep archetype × faction; assert the full unit contract for every
      archetype-faction combination via the P0 harness.
  - AC: a `ScenarioMatrix` (axes: `archetype` ∈ {infantry, vehicle, aircraft, builder, support,
    superweapon}, `faction` ∈ {USA, GLA, China}; cells without a valid reference unit pruned by
    ConstraintFilter) runs one full-contract scenario per cell; each asserts all six contract points
    (produce/move/attack/abilities/die); see the deep dive matrix section.

### P6c: Special-power implementation ([plan](plans/validation-ground-gap-closure.md))
- [x] Cleanup-area location power (P6c-1).
  - AC: `SpecialPowerAtLocation` with `SPECIAL_CLEANUP_AREA` heals friendly objects within the
    power's radius of the target location (observable health increase on a damaged friendly); the
    Ambulance's P6b support contract asserts the effect instead of reporting a gap.
- [x] Object-target hack powers (P6c-2).
  - AC: `SpecialPowerAtObject` implements `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING` (target building
    ownership transfers to the caster's player), `SPECIAL_HACKER_DISABLE_BUILDING` and
    `SPECIAL_BLACKLOTUS_DISABLE_VEHICLE_HACK` (target disabled for the ability's effect duration),
    `SPECIAL_BLACKLOTUS_STEAL_CASH` (cash transfer mirroring `CashHackSpecialPower`), and
    infantry building capture; each has an observable-effect assertion.
- [x] Hijacker contact hijack (P6c-3).
  - AC: `HijackerUpdate` transfers an enemy vehicle's ownership to the hijacker's player on
    contact and consumes the hijacker; the validation ground's hijacker completes on ownership
    transfer.
- [x] Validation-ground measurement of the ability units (P6c-4).
  - AC: the Medic, Black Lotus, Hacker, and Hijacker each perform their ability in the validation
    ground with a per-power completion predicate (heal observed, ownership changed, target
    disabled, vehicle hijacked) and carry baseline entries.

### P7: End-to-end skirmish validation
- [x] Author one end-state scenario per match outcome (six total).
  - AC: six scenario files exist, one per end state (player eliminated, player surrenders, AI
    surrenders, team win, mutual elimination → sudden death, time-limit → score decision); each
    runs on the P0 harness to headless completion.
- [x] All six scenarios assert their expected `GameOutcome`.
  - AC: each scenario terminates with the correct `OutcomeStatus`, `OutcomeReason`, and
    `WinningPlayer`/`WinningTeam`/`LosingPlayer`; exit code reflects pass/fail.
- [x] Promote P6b archetype-contract scenarios into the regression suite.
  - AC: the six P6b archetype scenarios run as part of the P7 suite and all pass; any P6b gap
    items still open are resolved before P7 is marked Done.
- [x] Assert invariants across all scenarios (no negative money, no lost/duplicated units, AI made
      progress where applicable).
  - AC: invariant assertions (money never negative, unit counts not duplicated, AI-issued orders
    where AI participates) are attached to every scenario; failures report offending frame.
- [x] Lock the full suite as the standing "is the game still playable?" regression check.
  - AC: all P7 scenarios (end-state + archetype-contract) run in CI (or a documented command) as
    the standing playability check, documented in the repo.
- [x] P7 scenario matrix: sweep the full end-state × invariant space as the canonical regression
      gate.
  - AC: a `ScenarioMatrix` (axis: `endState` ∈ {PlayerEliminated, PlayerSurrenders, AISurrenders,
    TeamWin, MutualElimination, ScoreDecision}) with universal invariant assertions (money never
    negative, no unit duplication, AI progress where applicable) attached to all cells; the aggregate
    report is the standing "is the game still playable?" check; all cells pass before P7 is marked
    Done.

### P8: Implementation delta remediation ([deep dive](deep-dives/p8-implementation-delta-remediation.md))
- [x] Data-driven victory configuration seam (P8-1).
  - AC: `VictorySystem.FrameLimit`/`ScoreMetric` are settable through a real skirmish/scenario
    configuration path, not only via direct `internal` property assignment in test code; default
    behavior is unchanged; a headless test starts a match through the configuration path and
    asserts the configured values took effect.
- [x] Sudden-death repeat-elimination fix (P8-2).
  - AC: a scenario where the elimination check immediately following a `SuddenDeath` transition
    also mutually eliminates all remaining active players resolves to `Decided` rather than hanging
    in `SuddenDeath` indefinitely; test covers the repeat-elimination case.
- [x] End-of-match UI and `TestHarness` integration (P8-3).
  - AC: a real UI/exit consumer of `GameOutcome` exists for interactive play; `OpenSage.Tools.TestHarness`
    exits through the same `GameOutcome`-driven path rather than its pre-`GameOutcome` ad hoc exit.
- [x] Real gather-cycle harness verification (P8-4).
  - AC: the P4 scenario matrix's economy assertion drives a harvester through the real
    `SupplyAIUpdate` state machine (`SearchingForSupplySource → Approaching → Requesting →
    Gathering → DumpingSupplies`) via `Scene3D`, not a synthetic dump/debit shortcut.
- [x] Real per-faction build trees and production assertions (P8-5).
  - AC: the P5 scenario matrix exercises real per-faction (`USA`/`GLA`/`China`) build trees instead
    of a reflection-built synthetic prerequisite chain; each cell asserts an actual unit/upgrade
    spawn, not only `CanProduceObject`; the `human` `playerType` cell routes through
    `OrderProcessor` like the `AI` cell.
- [x] Control-bar live dispatch verification (P8-6).
  - AC: the P6 command-coverage matrix dispatches through `GeneralsControlBar` end to end rather
    than asserting only the `OrderType` mapping.
- [x] `FireWeaponUpdate` and real observable-effect assertions (P8-7).
  - AC: `FireWeaponUpdate.Update()` is a real implementation, not a no-op stub; the 9 of 15 P6
    scenario matrix cells that previously asserted a disclosed "GAP" message now assert a real
    observable effect.

### P9: Unit validation ground ([deep dive](deep-dives/p9-unit-validation-ground.md))
- [x] Primary-action model (P9-1).
  - AC: every `UnitCatalog` entry resolves to exactly one `PrimaryAction` (attack, build, gather,
    special power, or the move fallback); a test fails if any catalog entry is unresolved, so adding
    a unit to the catalog without deciding its action cannot silently drop it from the ground.
- [x] Scenario and fixed layout builder (P9-2).
  - AC: `ValidationGroundBuilder.Build` returns an identical `SpawnPlan` (unit positions, target
    positions, target assignment by index, order sequence) for the same scenario and seed across
    repeated invocations, and has no dependency on `IGame` or `Scene3D`.
- [x] Direct-spawn host (P9-3).
  - AC: the whole roster plus the destructible-target cluster and supply sources exist in
    `GameLogic` after the spawn stage, created through `GameLogic.CreateObject` at the planned
    positions with terrain-sampled height, and the settle stage runs for the configured frame count
    before any order is issued.
- [x] Per-unit action timer (P9-4).
  - AC: each unit's duration is recorded in logic frames derived from `MapTime`/`MsPerLogicFrame`
    (never wall-clock), starting on the dispatch frame and stopping on the first observed completion
    signal for its action kind; exceeding the unit's frame budget records a timeout that is a
    distinct result from a regression.
- [x] TestHarness wiring for both run modes (P9-5).
  - AC: a new built-in scenario value runs the ground both windowed and headless from one scenario
    definition, spawn host, and timer; the existing replay-driven scenarios still run unchanged.
- [x] Timing report output (P9-6).
  - AC: the run emits a per-unit stdout table and a JSON artifact listing unit, faction, action,
    measured frames, baseline frames, delta, and verdict; unimplemented handlers and
    spawn-versus-production misbehaviour appear as named gaps rather than passes.
- [x] Golden baseline and regression gate (P9-7).
  - AC: a baseline with per-entry tolerance (percentage plus a minimum absolute frame floor) is
    checked in, captured only through an explicit refresh mode and never rewritten by a normal run;
    headless exits non-zero when any unit regresses beyond its tolerance.
- [x] Validation-ground test coverage (P9-8).
  - AC: install-free `[Fact]` tests cover layout rules, roster completeness, action-mapping
    totality, and baseline comparison including the tolerance floor; an install-gated
    `[GameFact(SageGame.CncGenerals)]` test runs the ground end to end and asserts every unit either
    completes within tolerance or is a reported gap.
- [ ] Windowed visual validation without a physical display.
  - AC: the TestHarness windowed mode (and the validation ground's non-headless path) runs in a
    headless/CI/agent shell by providing a virtual display (e.g. `Xvfb`), so a window is not
    required to exercise the renderer and capture frames. Document the exact command in
    `src/OpenSage.Tools.TestHarness/AGENTS.md`. Today a `tty`-type shell has no `DISPLAY` and cannot
    authorize against the host's X server, so only the headless path runs there.

---

## Track A: Foundations

See [`track-a-foundations.md`](track-a-foundations.md). Gated on Track P (P7).

### A1: Headless simulation host ([deep dive](deep-dives/a1-headless-host.md))
- [x] A1-1 Headless seam audit & approach.
  - AC: TBD, a written audit (in the headless-host deep dive) lists renderer/audio/window couplings
    in the logic tick with a removal approach per coupling.
  - Done: audits in [`implementation/track-a-a1-headless-host.md`](implementation/track-a-a1-headless-host.md)
    (`WI-A1-01`, `WI-A1-02`); grounded diagrams in the deep dive (`WI-A1-03`).
- [x] A1-2 Data-only asset loading (no GPU/audio device).
  - AC: core assets load with no GPU/audio device present; a headless smoke test loads them without a
    window.
  - Done: `WI-A1-04`; `DataOnlyAssetLoadingTests` resolves object/weapon/locomotor INI with null
    graphics, non-Interactive.
- [x] A1-3 Extract the logic tick from `Game.cs` into a renderer-free path.
  - AC: the logic tick runs via a renderer-free entry point; a headless test advances N ticks with no
    renderer constructed.
  - Done: `WI-A1-05`; `LogicStepper` extracted from `Game.Update`, `LogicStepperTests`.
- [x] A1-4 `SimulationHost` fixed-rate FTRT host (via the domain-model seam).
  - AC: `SimulationHost` ticks at a fixed rate decoupled from render, parameterized by the domain seam;
    a benchmark shows faster-than-real-time execution.
  - Done: `WI-A1-06`; `SimulationHostTests`. The benchmark clause belongs to B1; the domain seam is
    parameterized as the A4 comment-seam pending the domain-model phase.
- [x] A1-5 Run outputs: state snapshots/deltas, metrics/events stream, tiered divergence recording
      (T0/T1/T2), replay export.
  - AC: a run emits snapshots/deltas + metrics/events + tiered (T0/T1/T2) recording + replay export;
    artifacts are reloadable by the A5 visualizer.
  - Done: `WI-A1-07` (T0 metrics/events + terminal `GameOutcome`), `WI-A1-09` (T1 tolerance-quantized
    spatial-cell signature trees), `WI-A1-10` (T2 ring-buffer triggered detail + pre-marked windows),
    `WI-A1-11` (snapshot/delta binary artifact with frame reconstruction), `WI-A1-08` (TestHarness
    headless + P0 `HarnessRunner` onto `SimulationHost`). Replay input recording is the existing
    `.rep` path. Milestone test: headless run to a decided outcome with all tiers written and
    reloadable (`TieredRecordingTests.HeadlessRun_ReachesMatchEnd_AndWritesAllTiers`).

### A2: Statistical validation
- [x] Add seed controls and audit randomness sources.
  - AC: experiments can select a seed and the audit classifies simulation and presentation sources.
  - Done: audits and conversion in [`implementation/track-a-a2-reproducibility.md`](implementation/track-a-a2-reproducibility.md);
    `Game.StartGame` seeds `GameLogic.Random`/`GameClient.Random` from the run seed; the two
    simulation-affecting uncontrolled sites converted. Remaining presentation-only sources stay
    uncontrolled by design (listed in the audit). This is an available control, not a
    reproducibility requirement.
- [x] Verification harness: multi-sample metrics and per-run invariants.
  - AC: a harness runs a scenario sample set, checks each run's invariants, and reports aggregate
    distributions without requiring runs to agree.
  - Done: `StatisticalRunHarness` in `OpenSage.Tools.ReplaySketch.Core` aggregates per-run
    assertion reports and A1-5 T0 run outputs (mean, sample variance, Student-t 95% CI, Tukey
    outliers); `HarnessRunner.RunAndAssert` gained an optional T0 run-output callback. Tests in
    `A2StatisticalHarnessTests`: mocked-host default-N runs (non-interactive), install-backed
    P0-harness runs (Interactive), higher N gated behind the `StatisticalHighN` trait. No
    cross-run equality anywhere.

### A3: Simulation/presentation seam
- [x] Formalize the logic↔presentation boundary; ensure logic never reads render-only state.
  - Done: boundary defined in [deep-dives/a3-sim-presentation-seam.md](deep-dives/a3-sim-presentation-seam.md);
    logic reaches presentation only through `IGameObjectClient` commands and asset-derived bone
    queries, plus the one-way audio/FX/tracer services. All 15 audited read clusters removed
    ([audit](implementation/track-a-a3-sim-presentation-seam.md)); `ModelConditionFlags` are
    logic-owned; `LogicPresentationBoundaryTests` reflects over `OpenSage.Logic` types and fails
    on any presentation-type reference (empty allowlist); headless logic runs are A1's host tests
    plus the P7/P6b suites, all green.

### A4: Experiment framework seams & harness
- [x] A4-1 Simulation Domain Model seam (domain-agnostic entities/topology/interactions/metrics).
  - Done: contracts in `src/OpenSage.Game/Simulation/` (`ISimulationDomainModel`,
      `DomainEntitySummary`, metrics/topology/interaction/fidelity types);
      [design](deep-dives/a4-simulation-domain-model.md) and
      [audit](implementation/track-a-a4-simulation-domain-model.md). `SimulationHost` takes a
      required domain model and drives its lifecycle; the recording tiers (world summary, T1
      signature bucketing, T0 metrics) are routed through the contracts with byte-identical
      RTS outputs (equivalence tests). `RtsSimulationDomainModel` is the reference adapter;
      `TransportGraphDomainModel` (6 nodes/7 edges, vehicles on the shared GameObject runtime,
      tail-owner authority, edge-throughput/node-occupancy metrics) validates the seam against
      a graph topology through the same host (`SimulationDomainContractTests`; host constructs
      no concrete adapter, source-checked).
- [x] A4-2 AI decision policy seam ([deep dive](deep-dives/a4-pluggable-ai.md)): extract
      `IAIDecisionPolicy` from `AIPlayer.OnDecisionTick()`'s subclass override, with
      `SkirmishAIPlayer`'s rule-based state machine as the reference implementation.
  - Done: `IAIDecisionPolicy`/`AIDecisionContext` (`Logic/AI/`), reference
      `RuleBasedAIDecisionPolicy` extracted from `SkirmishAIPlayer` with no behavior change
      (P2 tests pass unmodified); `AIDecisionPolicyType` + `AIDecisionPolicyFactory`
      selection (default `RuleBased`, threaded via `PlayerSetting.AIPolicy`); first
      neural-network policy designed
      ([neural-network-ai-policy.md](deep-dives/neural-network-ai-policy.md)) and implemented
      inference-only (`NeuralNetworkAIDecisionPolicy`, 125-float genome, weights supplied at
      construction). Factory-only construction enforced by a source-scan test. The phase
      milestone's benchmarked comparison clause remains open with the B1-dependent
      harness item below.
- [x] AI policy evolution follow-on: carry genome payloads through player construction,
      evaluate populations over headless match samples, score from `GameOutcome` plus existing T0
      metrics, apply configured selection/variation, and emit per-generation reports.
  - Done: WI-A4-11..16. Design
      ([implementation/track-a-a4-ai-policy-evolution.md](implementation/track-a-a4-ai-policy-evolution.md)):
      `AIDecisionPolicyPayload` carries 125-float genomes `PlayerSetting` -> factory;
      ReplaySketch.Core gains deterministic `NeuralEvolutionOperators` (seeded
      init/elitism/tournament/mutation, byte-identical per seed), `AIFitnessScorer`
      (outcome ordering win/sudden-death/frame-cap/loss plus economy/army/speed shaped terms from
      T0 only, explicit rejection of incomplete outputs), `NeuralPolicyEvolutionRunner` with
      `INeuralPolicyMatchEvaluator` (exactly population x seeds evaluations, failure-explicit
      per-generation JSON report with ancestry + diversity), and the real
      `HeadlessNeuralMatchEvaluator` (genome slot vs rule-based reference, `SimulationHost`
      stepping, T0 capture). Interactive smoke: a 2-genome generation over real 900-frame
      matches. Variation operators are same-input deterministic; simulation samples are
      scored statistically (fitness means), never compared cross-run.
- [x] A4-3 Decentralized hierarchical AI seam
      ([deep dive](deep-dives/a4-decentralized-hierarchical-ai.md)): general/commander/soldier
      rank hierarchy of per-unit ANNs, composed behind the existing `IAIDecisionPolicy` seam via a
      `HierarchicalAIDecisionPolicy` adapter.
  - Done: a..f via WI-A4-17..30. Squad model + version-gated persistence; per-rank
      interfaces with rule-based references; adapter with documented cadences and a single
      actuation path; frozen per-rank neural contracts
      ([design](deep-dives/hierarchical-neural-network-policies.md), soldier 75 / commander 156 /
      general 116 weights) implemented and factory-selectable via `HierarchicalRankWeights`;
      contract tests for repair, dispatch, and save identity; alternating round-robin
      co-evolution runner (one rank per generation, deterministic partners, shared outcome
      fitness, rank-stamped reports) with an install-backed smoke test. Actuation stays
      exclusively through the soldier rank's orders into `OrderProcessor`.
      ([deep dive](deep-dives/a4-decentralized-hierarchical-ai.md)): general/commander/soldier
      rank hierarchy of per-unit ANNs, composed behind the existing `IAIDecisionPolicy` seam via a
      `HierarchicalAIDecisionPolicy` adapter.
  - AC: squad/commander model with death-triggered promotion exists and persists (A4-3a); per-rank
    `ISoldierDecisionPolicy`/`ICommanderDecisionPolicy`/`IGeneralDecisionPolicy` interfaces exist
    with rule-based reference implementations (A4-3b to A4-3d); actuation stays exclusively through
    the single documented soldier/order path, matching A2/P2's actuation constraint; fixed-contract
    per-rank neural policies run end to end (A4-3e); three populations advance through the
    documented co-evolution schedule with rank-aware reports (A4-3f).
- [ ] Stable seams for pathfinding, partitioning, fidelity policy, storage layout, model import (see
      [seams.md](seams.md)).
  - AC: TBD, each seam has an interface + ≥1 swappable implementation selectable at runtime; all are
    listed in [seams.md](seams.md).
- [ ] Experiment harness: select/configure an implementation, run vs. B1 benchmarks, report to A5.
  - AC: TBD, the harness selects/configures an impl, runs it against B1 benchmarks, and reports to
    A5; demonstrated by swapping one seam impl and comparing results.

### A5: Scenario authoring & visualization ([deep dive](deep-dives/a5-visualization.md))
- [ ] A5-1 Scenario authoring & setup (declarative + visual + capture-from-run; built on the P0 harness).
  - AC: a declarative scenario round-trips and runs; capture-from-run round-trips its documented
    scenario-initializable projection without claiming save-state equivalence; visual editing covers
    initial population, parameters, and domain-appropriate topology; validation and bounded preview
    reject invalid scenarios before a long run.
- [ ] A5-2 Experiment matrix / parameter sweeps (tooling/visualizer layer).
  - AC: a scenario declares sweep axes that expand to one run per cell; results are keyed for
    comparison; the visualizer can diff two matrix runs. Note: the expansion primitive (cartesian
    product, per-cell execution, keyed JSON output) is provided by P0-5b; A5-2 adds tooling and
    visualization on top.
- [ ] A5-3 Results replayer / visualizer (extends ReplaySketch).
  - AC: the visualizer loads a run, plays/pauses/fast-forwards/scrubs the timeline, supports
    event-to-frame navigation, shows domain state plus metrics/events/fidelity, and exports
    headlessly.
- [ ] A5-4 Divergence localization over the tiered recording.
  - AC: given two runs, the tool localizes the first subtree exceeding tolerance; tested on a
    known-divergent pair.
- [ ] A5-5 Authoring & visualization at scale: procedural/large command-set & initial-condition
      generation; streaming/LOD/culling in the replay viewport (reuse B5/B7); bounded-memory run
      loading; authoring/replay throughput tracked by B1. (Pairs with B4a-7.)
  - AC: authoring a large procedural scenario and replaying a long/large run stay within documented
    time/memory budgets using region-indexed state, streaming/LOD/culling, and bounded-memory
    loading; B1 tracks the throughput.
- [ ] A5-6 WorldBuilder baseline: audit functional state; ensure basic author → save → load → play
      round-trip works at normal map sizes; document gaps. (Early-start sub-phase, no A1/A4
      dependency; gates B4a.)
  - AC: a normal-sized map with terrain and object placement round-trips through WorldBuilder (author
    → save → load → play in the engine) without errors; functional gaps documented.

---

## Track B: Scale

See [`track-b-scale.md`](track-b-scale.md). Gated on Track P (P7).

- [ ] B1 Profiling & robust benchmarking utilities (reusable harness, statistical rigor,
      regression detection, machine-readable output, hardware characterization, and Bepu
      step/contact/sync counters).
  - AC: a reusable harness runs repeatable seeded scenarios (incl. the 10k-unit interactive
    reference workload, the [scale anchor](open-questions.md)), reports mean+variance with warmup
    discarded, detects regressions vs. stored baselines, emits machine-readable output including
    Bepu step/contact/transform-sync counters for physics scenarios, and is reused by ≥1 other
    track.
- [ ] B1 hardware characterization: probe host machine and include context in benchmark report.
  - AC: each benchmark run emits a `hardware` section in the machine-readable output containing CPU
    model/core count/frequency, system memory and measured bandwidth, and GPU device/VRAM; a
    `capacity_estimate` field derives the sustainable unit count at the target frame rate for that
    machine from the per-subsystem timing results; reports from different machines are unambiguously
    tagged so results can be compared across hardware.
- [ ] B1 benchmark matrix: sweep unitCount × scenario × subsystem; produce keyed benchmark results
      for regression comparison.
  - AC: a `ScenarioMatrix`-style benchmark matrix (axes: `unitCount` ∈ {1000, 5000, 10000},
    `scenario` ∈ {allMove, allCombat, mixedActions}, `subsystem` ∈ {pathfinding, physics, combat,
    economy})
    runs each cell via the B1 harness; per-cell mean+variance is keyed for side-by-side comparison
    and regression detection.
- [ ] B2 Allocation & hot-path cleanup (per-tick allocations, collision-pair lists, cache hot
      queries, and data layout).
  - AC: per-tick allocations in the named hot paths are eliminated (allocation profiler shows ~0
    steady-state allocations for the targeted paths); B1 shows no performance regression.
- [ ] B3 Parallel & data-oriented logic (intent/command buffers + conflict resolution; incremental
      SoA storage behind the module API).
  - AC: TBD, intent/command buffers + conflict resolution land behind the module API with no
    behavior change vs. baseline (verification harness within tolerance); SoA storage is swappable.
- [ ] B4 Spatial & pathfinding scaling (hierarchical/flow-field, group movement, async path requests).
  - AC: TBD, hierarchical/flow-field pathing + group movement + async requests scale to a target
    unit count within a path-latency budget in B1; no pathing regressions on baseline maps.
- [ ] B4a Larger maps via WorldBuilder ([deep dive](deep-dives/b4a-larger-maps.md)): region editing,
      large-map wizard, terrain streaming/LOD, chunked storage, round-trip at scale,
      large-population scenario authoring (B4a-7: bulk/procedural object placement, region-based
      multi-object edit/undo, instanced editor rendering, pairs with A5-5). Depends on A5-6.
  - AC: TBD (multi-sub-phase, see the deep dive). Rough template: very large maps author → save →
    reload → play within budgets; region edits are O(brush) not O(map area); large object populations
    are placeable/editable interactively.
- [ ] B4b Geospatial map generation
      ([deep dive](deep-dives/b4b-geospatial-map-generation.md)): import Google Earth KML/KMZ plus
      georeferenced DEM/imagery rasters from operator-authorized Earth Engine datasets or other
      sources into WorldBuilder through the map interchange boundary. Project, crop, resample, and
      preserve source/projection metadata without direct Google service access. Google basemap
      imagery and terrain are not supported inputs. Depends on B4a and H6.
  - AC: a synthetic fixture and one operator-supplied real-world area generate maps whose extent,
    elevations, imagery, and named vector landmarks match source control points within declared
    tolerances; each map saves, reloads in WorldBuilder, and plays.
- [ ] B5 Rendering at scale (GPU instancing, LOD/impostors, culling, render-bucket/shadow).
  - AC: TBD, instancing/LOD/culling render the near-term anchor of 10k units at ~30 fps in B1 (see
    the [scale-anchor Decision](open-questions.md)); the tool viewports (B4a-7/A5-5) reuse it.
- [ ] B6 Distributed simulation via spatial sharding after F4a
      ([deep dive](deep-dives/b6-distributed-simulation.md)): one Bepu world per RTS shard,
      kinematic halo proxies, and handle-free body handoff.
  - AC: a simulation runs across at least two processes with no object/body loss or duplication,
    and a boundary contact applies impulses and gameplay effects exactly once.
- [ ] B6 scenario matrix: sweep shardCount × scenario × partitionStrategy; assert cross-shard
      consistency for every configuration.
  - AC: a `ScenarioMatrix` (axes: `shardCount` ∈ {1, 2, 4}, `scenario` ∈ {largeBattle,
    multiRegion}, `partitionStrategy` ∈ {spatial-grid, spatial-graph-cut}; invalid combos pruned)
    runs each cell; each asserts unit count matches the single-shard reference and aggregate metrics
    are within tolerance; see the deep dive matrix section.
- [ ] B7 Variable simulation fidelity ([deep dive](deep-dives/b7-variable-fidelity.md)):
      after F4a, use replicated-state fidelity for shared/distributed and view-driven single-node
      only, with physical objects reduced through sleep/kinematic/aggregate modes.
  - AC: fidelity reduction scales object count with bounded error versus full fidelity, and every
    reduced physical object restores full Bepu state before decisive contact.
- [ ] B7 scenario matrix: sweep fidelityLevel × unitCount × importanceConfig; assert each technique
      reduces cost while staying within bounded error.
  - AC: a `ScenarioMatrix` (axes: `fidelityLevel` ∈ {full, temporal-lod, simplified-behavior,
    aggregated}, `unitCount` ∈ {1000, 5000, 10000}, `importanceConfig` ∈ {all-high, mixed,
    all-low}; cells requiring unitCount < 5000 for `aggregated` pruned) runs each cell; each asserts
    lower mean tick time than `full` baseline and aggregate metrics within tolerance; see the deep
    dive matrix section.

---

## Track C: External Models & Animations

See [`track-c-external-assets.md`](track-c-external-assets.md). Gated on Track P (P7).

- [x] C1 Format-agnostic import abstraction (feeds the same `Model`/`ModelInstance` runtime types).
  - AC: CPU-only `IModelImporter` / `IAnimationImporter` contracts feed engine-owned runtime
    materializers; W3D is the selected reference implementation; representative static, skinned,
    hierarchy-referenced, material, and animation assets retain their runtime descriptors; no
    runtime graphics type accepts a W3D parser type.
- [x] C2 Skeleton / animation retargeting.
  - AC: named channels bind case-insensitively to a differently ordered runtime hierarchy and play
    through an existing gameplay animation state; missing and duplicate named targets fail before
    playback; out-of-range direct targets are omitted once, preserving W3D behavior.
- [x] C3 Material mapping.
  - AC: synthetic static and skinned meshes render the core metallic-roughness matrix, including
    external and embedded images, UV0/UV1, samplers, color space, alpha modes, double-sided state,
    and generated tangents; validation and cache-key coverage precede GPU allocation.
- [ ] C4 First production format: static meshes + skinned skeletal animation only.
  - AC: strict SharpGLTF.Core adapters load repository-owned `.gltf` and `.glb` fixtures through
    isolated virtual roots; static nodes, split triangle primitives, sparse accessors, one skin,
    inverse binds, linear/step named animation, rigid attach nodes, and core PBR data convert into
    C1-C3 values; the one-meter `(x, y, z)` to `(z, x, y)` fixture renders at approximately
    3.28084 world units, upright and +X-forward with correct winding, and every excluded or
    required unsupported feature fails contextually.
- [ ] C4a Empire Earth II NIF 10.1 proof of concept
      ([deep dive](deep-dives/ee2-gamebryo-model-storage.md#9-c4a-proof-of-concept-scope)).
  - AC: `nwg1_galley.nifcache` and `ab11_TwinEngineBomber.nifcache` load from an
    operator-supplied `graphics.zip` through C1, materialize as runtime `Model` assets, and
    produce recognizable captures; the Bomber uses its highest-detail LOD, source texture names
    survive import while rendering uses untextured diffuse materials, and no EE2 assets are
    committed.
- [ ] C5 Authoring & validation tooling (extend ReplaySketch; licensing out of scope).
  - AC: the ReplaySketch Model tab inspects an isolated external glTF root, lists clips and
    structured C1-C4 diagnostics, pairs an optional animation through C2, and applies explicit
    preview-only transforms; a versioned TestHarness request produces a deterministic PNG and JSON
    report, stale sessions cannot replace current results, and source replacement releases
    filesystem, artifact, and GPU lifetimes.
- [ ] C6 Extended features (deferred): morph targets, advanced PBR.
  - AC: implementation starts only from a named experiment with a reduced fixture and request-scoped
    feature gate; morph targets use separate bounded delta arrays and weight tracks with
    morph-before-skin deformation and conservative bounds, while each advanced PBR family uses
    typed payloads and normalized shader keys; every gate defaults off, required unsupported
    semantics fail, optional core-preserving fallbacks warn, and feature-disabled C1-C5 output is
    unchanged.

## Track D: Gameplay Content Expansion

See [`track-d-gameplay-expansion.md`](track-d-gameplay-expansion.md) and the
[deep dive](deep-dives/d1-d2-tech-tree-era-progression.md). Gated on Track P (P7); D3/D4 also gated on
Track C (C1) and the standalone [ModCollector tool](plans/mod-collector.md).

- [ ] D1 Tech tree / prerequisite graph generalization (`TechNode`/`TechTreeState`, adapter over
  existing `UpgradeTemplate`).
  - AC: TBD, a branching tech tree (a node with two alternative prerequisite paths) is authored
    in INI and gates build/produce availability; existing built-in content is unaffected.
- [ ] D2 Era/age progression mechanic (`EraDefinition`/`EraState`, gated age-up order).
  - AC: TBD, an age-up order validates against cost/prerequisites, resolves after its time gate,
    and era-gated tech/build options become available immediately after.
- [ ] D3 External content ingestion pipeline (ModCollector-staged content through `AssetStore`
  scope layering).
  - AC: TBD, a ModCollector-staged content pack's models and INI-authored tech nodes load and
    render alongside built-in content with no engine change per mod.
- [ ] D4 Reference non-SAGE scenario (small tech tree + one era transition, via ReplaySketch).
  - AC: TBD, a ReplaySketch-authored scenario builds through a branching tech node and one era
    transition, asserted headlessly per the P0 harness pattern.

## Track E: Naval Warfare

See [`track-e-naval-warfare.md`](track-e-naval-warfare.md) and the
[deep dive](deep-dives/e1-e2-naval-warfare.md). E1/E3 are gated on Track P (P7); E2 also depends
on F4a's shared Bepu ship/contact path.

- [ ] E1 Water/ground terrain passability (`TerrainLogic`/`Pathfinder` stub replacement, wiring
  `Locomotor.Surfaces` and `AIData.WadeWaterDepth`/`DeepWaterDepth`).
  - AC: TBD, a water-only locomotor cannot move onto dry land, a ground-only unit cannot enter
    deep water but can wade shallow water, and `OverWater` triggers correctly.
- [ ] E2 Ship combat (broadside weapon slots, F4a Bepu hull/contact ram for `SHIP_SUICIDE`, and
  `SHIP_TRANSPORT` body-transition verification).
  - AC: two Bepu-backed ships exchange broadside fire without interpenetrating; one shared contact
    record triggers ram damage and self-destruction exactly once; a transport loads/unloads
    passengers while moving on water with correct body suspension/restoration.
- [ ] E3 Naval production (`PORT` building placement/production via existing production-queue
  pattern).
  - AC: TBD, a `PORT` building can only be placed adjacent to water, produces a ship, and the
    ship's rally point resolves correctly on water.
- [ ] E4 Reference naval scenario (port + ships fighting, via ReplaySketch).
  - AC: TBD, a ReplaySketch-authored scenario builds a ship from a port and wins a water
    engagement, asserted headlessly per the P0 harness pattern.

## Track F: Object Physics, Deformable Terrain & Destruction

See [`track-f-terrain-destruction.md`](track-f-terrain-destruction.md). Gated on Track P (P7);
coordinates with Track E for watercraft and with B2/B4/B7 for collision ownership, pathing, and
physics fidelity. Every instantiated object receives an explicit physics participation result;
non-physical objects do not receive bodies.

- [ ] F1 Terrain deformation data model (deltas layered on the chunked heightmap).
  - AC: TBD, a deformation delta applies to and is queryable from a chunked heightmap region in
    isolation from rendering.
- [ ] F2 Runtime mesh & rendering update for deformed regions.
  - AC: TBD, a runtime deformation is visible within one frame budget, rebuilding only affected
    `TerrainPatch`es.
- [ ] F3 Weapon/combat-driven terrain deformation (explosive weapons via `DamageModule`).
  - AC: TBD, an explosive weapon impact produces a crater of the expected radius/depth; non-
    explosive weapons are unaffected.
- [ ] F4 Physics engine integration (pluggable rigid-body seam, headless-capable).
  - AC: a falling/stacking scene runs windowed and headless within the same authored bounds, and
    the B1 harness reports `Timestep` cost at 100/1k/10k bodies.
- [ ] F4a-1 Physics participation resolver and cross-game definition audit.
  - AC: every instantiated definition in each supported `SageGame` resolves to dynamic, kinematic,
    static, sensor, or none with a reported reason; the audit fails on an unclassified physical
    definition.
- [ ] F4a-2 Body registry, transform authority, and lifecycle transitions.
  - AC: backend selection is injected per `GameEngine`; parallel engines do not leak selection;
    create/remove/teleport, construction, deploy/pack, contain/release, death, and resurrection
    preserve pose/state and leave registry/body counts exact with no stale body.
- [ ] F4a-3 Compound shape/material/filter mapping and Bepu contact dispatch.
  - AC: all authored geometry shapes, offsets, and scale map through cached seam shapes; one solid
    or sensor pair emits one filtered gameplay contact per frame, and Bepu-managed pairs are not
    dispatched again by the legacy partition collision pass.
- [ ] F4a-4 Loose dynamics: debris, hulks, crates, salvage, rocks, and thrown objects.
  - AC: one representative of each available role can be tossed, collide, settle, sleep, wake, and
    be removed within authored bounds.
- [ ] F4a-5 Ground movers: infantry/cavalry/monsters, vehicles/builders, and siege.
  - AC: representatives move, stop, turn, traverse slopes, and push apart without tunneling or
    incidental tumbling; pathfinding and locomotors remain route/motion-command authorities.
- [ ] F4a-6 Aircraft, drones, parachutes, carrier decks, and airborne deaths.
  - AC: active flight stays controlled, freefall releases constraints, and landing, bounce, fall
    damage, deck support, and cleanup consume Bepu contacts.
- [ ] F4a-7 Static and kinematic structures, bridges, walls/gates, props, platforms, and deployables.
  - AC: dynamic objects contact each surface, and every geometry/state transition replaces the
    collider without stale obstruction or a one-frame origin collider.
- [ ] F4a-8 Projectile policy for ballistic, thrown, guided, authored-arc, and hitscan weapons.
  - AC: each representative resolves to dynamic sensor, kinematic sensor, or no body; physical
    impact dispatches its warhead exactly once and ignores owner/container pairs.
- [ ] F4a-9 Ships, boats, and amphibious objects using E1 water data.
  - AC: representative hulls receive water support and filtered contacts, transition at shore as
    authored, contain/release passengers, and expose one contact record with the data E2 needs for
    ram damage.
- [ ] F4a-10 Bepu default cutover and legacy comparison mode.
  - AC: the category scenario matrix and cross-game audit pass with Bepu as the default backend;
    selecting legacy requires an explicit per-engine test/debug setting, and no world mixes
    backends.
- [ ] F5 Physics-based structural destruction (replaces canned `RubbleRiseUpdate`/`ToppleUpdate`
      model swap for at least one building type).
  - AC: a destroyed building replaces its static body with simulated rigid-body debris that comes
    to rest, without a stale collider or single pre-baked rubble model.
- [ ] F6 Destruction-terrain interaction (settled debris/craters block pathing & placement).
  - AC: TBD, a destroyed building's settled debris/crater blocks pathing and prevents
    construction on the same footprint until cleared.
- [ ] F7 Persistence & replay (`StatePersister` + ReplaySketch/P0 capture of deformation and all
      runtime object-physics state).
  - AC: a save/load round-trip reconstructs static bodies and preserves participation mode,
    transform, velocities, sleep/constraint state, terrain deformation, and debris state without
    serializing Bepu handles; resumed scenarios stay within authored bounds.
- [ ] F8 Authoring & validation tooling (extend ReplaySketch; no dedicated viewer).
  - AC: ReplaySketch reports resolved participation/collider/contact state, asserts contact and
    settle bounds headlessly, and shows deformed terrain and settled debris in screenshot mode.

## Track G: Rendering Clarity & Image Quality

See [`track-g-rendering-clarity.md`](track-g-rendering-clarity.md). Not gated on Track P
(presentation only, no simulation behaviour change). Coordinates with B5 (rendering at scale, same
pipeline-description plumbing) and A3 (sim/presentation seam). Every visual change lands behind the
G2 preset layer so the `Faithful` preset reproduces the original look.

- [ ] G1 High-DPI drawable & resolution correctness (`ALLOW_HIGHDPI`, drawable-size swapchain,
      one pixels-versus-points convention across `GameWindow`/`RenderTarget`/input).
  - AC: on a display with a non-unity scale factor the swapchain and a captured frame match
    the window's native pixel dimensions, and cursor/input hit-testing stays correct.
  - [ ] G1-1 Classify every window/panel size consumer as logical points or physical pixels
        ([`WI-G1-01`](agent/cards/WI-G1-01-audit-size-consumers.md)).
    - AC: every `ClientBounds`/`WindowScale`/`CursorScale`/`Frame`/`Viewport` read in `src/` is
      classified, and sites feeding both spaces are called out.
  - [ ] G1-2 `AllowHighDpi` window creation, `SDL_GetWindowSizeInPixels` interop,
        `DrawableBounds`/`DpiScale`, swapchain sized in pixels
        ([`WI-G1-02`](agent/cards/WI-G1-02-allow-highdpi-drawable-size.md)).
    - AC: the window is created with `AllowHighDpi` via `new Sdl2Window(...)`, no `WindowScale`
      pre-multiplication remains, and `Swapchain.Resize` is fed `DrawableBounds`.
  - [ ] G1-3 Render target and 3D viewport in pixels, GUI layout on points
        ([`WI-G1-03`](agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md)).
    - AC: all three `EnsureFrame` call sites pass `DrawableBounds`, `Game.Viewport` is in pixels,
      and the WND/APT managers receive a logical size.
  - [ ] G1-4 Scale 2D drawing and mouse picking by `DpiScale`
        ([`WI-G1-04`](agent/cards/WI-G1-04-scale-2d-and-picking.md)).
    - AC: picking resolves to the same world position at `DpiScale` 1 and 2, and the GUI occupies
      the same fraction of the frame at both scales.
- [ ] G2 Graphics settings & fidelity presets (`Faithful`/`Enhanced`; settings layer, not an
      experiment seam). The parsed `StaticGameLod` assets name several knobs but have no runtime
      consumer, so G2 builds the application layer
      ([`WI-G2-01`](agent/cards/WI-G2-01-audit-render-state-knobs.md),
      [`WI-G2-02`](agent/cards/WI-G2-02-graphics-settings-type-and-presets.md),
      [`WI-G2-03`](agent/cards/WI-G2-03-wire-settings-into-render-construction.md)).
  - AC: TBD, switching presets at runtime changes active pipeline state, and `Faithful`
    reproduces the pre-track captured frame.
- [ ] G3 Visual regression harness (golden-frame comparison per preset over a fixed scene set,
      with sharpness/edge-aliasing metrics; extends `FrameCapturer` + ReplaySketch screenshot mode)
      ([`WI-G3-01`](agent/cards/WI-G3-01-audit-frame-capture-paths.md),
      [`WI-G3-02`](agent/cards/WI-G3-02-image-quality-metrics.md),
      [`WI-G3-03`](agent/cards/WI-G3-03-golden-frame-comparison.md)).
  - AC: TBD, a headless run compares the fixed scene set against stored goldens per preset and
    fails on regression; the metrics separate an aliased frame from an antialiased one.
- [ ] G4 Multisample antialiasing (sample count through `GameOutputDescription`, intermediate
      colour/depth targets and every pipeline description, plus a resolve before the final blit)
      ([`WI-G4-01`](agent/cards/WI-G4-01-inventory-sample-count-sites.md),
      [`WI-G4-02`](agent/cards/WI-G4-02-thread-sample-count-and-resolve.md)).
  - AC: TBD, 4x MSAA renders and resolves across the 3D, terrain, water, road and particle
    pipelines, and the G3 edge-aliasing metric improves against the same frame at 1x.
- [ ] G5 Cutout & alpha-test clarity (alpha-to-coverage under MSAA, coverage-preserving mips for
      alpha-tested textures, per-mip threshold rescale, replacing the fixed `0x60` cutoff in
      `Common.h`) ([`WI-G5-01`](agent/cards/WI-G5-01-audit-alpha-test-materials.md),
      [`WI-G5-02`](agent/cards/WI-G5-02-alpha-to-coverage-and-cutout-mips.md)).
  - AC: TBD, minified alpha-tested foliage and fencing show no dissolve-to-dots at distance under
    `Enhanced`, while `Faithful` still produces the original hard-cutoff result.
- [ ] G6 Gamma-correct rendering (sRGB colour-map views, linear lighting, sRGB/tonemapped output,
      linear-space mip filtering in `MipmapHelper`, ordered dither on the 8-bit output)
      ([`WI-G6-01`](agent/cards/WI-G6-01-audit-colour-space-handling.md),
      [`WI-G6-02`](agent/cards/WI-G6-02-linear-space-shading-and-dither.md)).
  - AC: TBD, a fog/sky gradient shows no visible banding under `Enhanced` and mip chains no longer
    darken with distance; `Faithful` retains gamma-space shading.
- [ ] G7 Texture filtering & mip quality (configurable anisotropy up to 16x, mip LOD bias, higher
      quality terrain texture-array resample/mip path in `Terrain.cs`)
      ([`WI-G7-01`](agent/cards/WI-G7-01-audit-samplers-and-mip-paths.md),
      [`WI-G7-02`](agent/cards/WI-G7-02-configurable-anisotropy-and-mip-bias.md)).
  - AC: TBD, terrain stays legible at grazing angles at 16x with no shimmer under camera motion,
    measured by the G3 metrics.
- [ ] G8 Shadow quality tier (four cascades at 2048² by default under `Enhanced`, slope-scaled/
      normal-offset bias replacing the constant bias in `ShadowSettings`)
      ([`WI-G8-01`](agent/cards/WI-G8-01-audit-shadow-settings-flow.md),
      [`WI-G8-02`](agent/cards/WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md)).
  - AC: TBD, shadow edges show no acne speckle or cascade blockiness at the default camera zoom,
    with cascade transitions not visible in the captured frame.
- [ ] G9 Resolution-independent 2D & UI (native-resolution 2D, resolution-independent layout
      anchored in logical points, higher-quality WND/APT upscaling)
      ([`WI-G9-01`](agent/cards/WI-G9-01-audit-2d-ui-resolution-path.md),
      [`WI-G9-02`](agent/cards/WI-G9-02-native-resolution-2d-layer.md)).
  - AC: TBD, the control bar and HUD text stay sharp at native resolution on a scaled display with
    layout and hit regions unchanged relative to the pre-track build.
  - [ ] G9-3 Pre-rendered HUD replacement: replace the control bar's 800x600 bitmaps with
        dynamically drawn equivalents (vector/procedural panels, buttons, and frames; live text)
        behind the `Enhanced` preset, `Faithful` keeps the original art.
    - AC: under `Enhanced` the control bar contains no HUD bitmap draws; captured frames are sharp
      at 4x native with no upscaled-art blur, and `Faithful` is pixel-identical to the pre-rework
      capture.
- [ ] G10 Realistic proportions (optional scale-correct presentation: reference-scale
      documentation plus per-definition visual model-scale overrides for the undersized aircraft
      class, behind the G2 settings layer; `Geometry` collision and `AttackRange` gameplay data
      untouched).
  - [ ] G10-1 Proportion audit and reference scale: measure per-class model bounds (W3D mesh
        bounds, FX meshes excluded) against INI `Geometry` and real-world counterparts; check in
        the deviation table and pin the world-units-per-meter reference (measured basis: ≈ 5
        units/m; aircraft undersized ≈ 2-2.5x, e.g. MiG ≈ 6.6 m in-game vs ≈ 17.3 m real).
    - AC: a checked-in table covers a representative set spanning infantry, ground vehicles,
      buildings, and aircraft, and a data test re-derives its numbers from the installed
      INI/W3D assets so a changed asset fails the test.
  - [ ] G10-2 Presentation-only scale overrides: per-definition visual scale multiplier keyed to
        the proportions option, correcting the aircraft class.
    - AC: with the option enabled only aircraft visuals change size; existing P0/P9 scenario
      assertions pass identically with the option on and off, and selection rings still match the
      visible models.
  - [ ] G10-3 Preset wiring and golden frames: register the proportions option in the G2 settings
        layer and add proportion-mode captures to the G3 golden set.
    - AC: golden frames captured under the proportions mode catch an injected rescale regression,
      and `Faithful` output is identical to the pre-phase capture.

## Track H: Map & Unit Storage Formats

See [`track-h-storage-formats.md`](track-h-storage-formats.md). Gated on Track P (P7) only.
Persistence/schema-design track: on-disk map and object/unit-definition storage formats, distinct
from B3 (runtime unit storage) and B4a-5 (chunked terrain runtime storage), which this track
coordinates with rather than duplicates.

- [ ] H1 Format requirements & current-state audit (`.map`/`MapFile` and INI/`ObjectDefinition`
  limitations; format-design Decisions recorded in `open-questions.md`).
  - AC: a written audit cites every current-state limitation by file:line, and each format-design
    question (read/write scope, human-editable vs. binary, shared vs. separate containers) has a
    recorded Decision.
- [ ] H2 Format-agnostic interchange abstraction (`IMapFormat`/`IObjectTemplateFormat`-style seam
  behind `AssetStore`/`OnDemandAssetLoadStrategy`, mirroring the C1 pattern).
  - AC: the legacy `.map` reader and INI `ObjectDefinition` parser load entirely as one
    implementation of the new format seam, with no behavior change.
- [ ] H3 New map storage format (versioned container; terrain section reserved for B4a-5's
  chunked heightmap representation).
  - AC: a map authored in the new format round-trips (write, reload) with terrain, blend, and
    placed-object data intact.
- [ ] H4 New object/unit-definition storage format (`ObjectDefinition` plus weapons/upgrades/
  locomotors and F4a engine-level physics metadata; INI stays a permanent legacy read path).
  - AC: a unit authored in the new format loads with full parity to its INI-authored equivalent,
    including participation/material/filter overrides; no Bepu-specific type or handle is serialized,
    and a new schema field does not break loading older content.
- [ ] H5 Legacy migration & compatibility tooling (`.map`/INI → new formats, round-trip
  validator).
  - AC: a converted legacy map and unit roster load through the new formats and play identically
    (per the P0 assertion layer) to the original `.map`/INI content.
- [ ] H6 Authoring & pipeline integration (WorldBuilder read/write; ModCollector/`AssetStore`
  scope layering).
  - AC: WorldBuilder authors, saves, and reloads a map in the new format, and a ModCollector-
    staged content pack's object definitions load through the new format alongside built-in
    content.
- [ ] H7 Validation & round-trip regression (automated round-trip suite; ReplaySketch scenario on
  new-format content asserted headlessly).
  - AC: a regression suite catches a round-trip mismatch for maps or object definitions, and a
    ReplaySketch scenario using new-format content passes the same headless assertions as its
    legacy-format equivalent.
