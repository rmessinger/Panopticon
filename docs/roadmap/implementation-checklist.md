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
- [ ] Superweapon archetype contract (P6b-6).
  - AC: a P0 harness scenario for the reference superweapon structure asserts all six contract
    points: built, charge cycle advances per tick, `SpecialPowerAtLocation` fires and produces
    area effect, cooldown state is set post-fire, structure can be sold; unimplemented handlers
    logged.
- [ ] All unimplemented special-power handlers surfaced by P6b-1 through P6b-6 are fixed or
      documented as explicit out-of-scope gaps with a follow-up item.
  - AC: every `SpecialPower*` order exercised by the six scenarios either passes its assertion or
    has a documented gap item; none silently succeed without effect.
- [ ] P6b scenario matrix: sweep archetype × faction; assert the full unit contract for every
      archetype-faction combination via the P0 harness.
  - AC: a `ScenarioMatrix` (axes: `archetype` ∈ {infantry, vehicle, aircraft, builder, support,
    superweapon}, `faction` ∈ {USA, GLA, China}; cells without a valid reference unit pruned by
    ConstraintFilter) runs one full-contract scenario per cell; each asserts all six contract points
    (produce/move/attack/abilities/die); see the deep dive matrix section.

### P7: End-to-end skirmish validation
- [ ] Author one end-state scenario per match outcome (six total).
  - AC: six scenario files exist, one per end state (player eliminated, player surrenders, AI
    surrenders, team win, mutual elimination → sudden death, time-limit → score decision); each
    runs on the P0 harness to headless completion.
- [ ] All six scenarios assert their expected `GameOutcome`.
  - AC: each scenario terminates with the correct `OutcomeStatus`, `OutcomeReason`, and
    `WinningPlayer`/`WinningTeam`/`LosingPlayer`; exit code reflects pass/fail.
- [ ] Promote P6b archetype-contract scenarios into the regression suite.
  - AC: the six P6b archetype scenarios run as part of the P7 suite and all pass; any P6b gap
    items still open are resolved before P7 is marked Done.
- [ ] Assert invariants across all scenarios (no negative money, no lost/duplicated units, AI made
      progress where applicable).
  - AC: invariant assertions (money never negative, unit counts not duplicated, AI-issued orders
    where AI participates) are attached to every scenario; failures report offending frame.
- [ ] Lock the full suite as the standing "is the game still playable?" regression check.
  - AC: all P7 scenarios (end-state + archetype-contract) run in CI (or a documented command) as
    the standing playability check, documented in the repo.
- [ ] P7 scenario matrix: sweep the full end-state × invariant space as the canonical regression
      gate.
  - AC: a `ScenarioMatrix` (axis: `endState` ∈ {PlayerEliminated, PlayerSurrenders, AISurrenders,
    TeamWin, MutualElimination, ScoreDecision}) with universal invariant assertions (money never
    negative, no unit duplication, AI progress where applicable) attached to all cells; the aggregate
    report is the standing "is the game still playable?" check; all cells pass before P7 is marked
    Done.

---

## Track A: Foundations

See [`track-a-foundations.md`](track-a-foundations.md). Gated on Track P (P7).

### A1: Headless simulation host ([deep dive](deep-dives/a1-headless-host.md))
- [ ] A1-1 Headless seam audit & approach.
  - AC: TBD, a written audit (in the headless-host deep dive) lists renderer/audio/window couplings
    in the logic tick with a removal approach per coupling.
- [ ] A1-2 Data-only asset loading (no GPU/audio device).
  - AC: core assets load with no GPU/audio device present; a headless smoke test loads them without a
    window.
- [ ] A1-3 Extract the logic tick from `Game.cs` into a renderer-free path.
  - AC: the logic tick runs via a renderer-free entry point; a headless test advances N ticks with no
    renderer constructed.
- [ ] A1-4 `SimulationHost` fixed-rate FTRT host (via the domain-model seam).
  - AC: `SimulationHost` ticks at a fixed rate decoupled from render, parameterized by the domain seam;
    a benchmark shows faster-than-real-time execution.
- [ ] A1-5 Run outputs: state snapshots/deltas, metrics/events stream, tiered divergence recording
      (T0/T1/T2), replay export.
  - AC: a run emits snapshots/deltas + metrics/events + tiered (T0/T1/T2) recording + replay export;
    artifacts are reloadable by the A5 visualizer.

### A2: Reproducibility & statistical stability
- [ ] Centralize all randomness through a seeded source; audit uncontrolled nondeterminism.
  - AC: all randomness flows through a seeded source; an audit lists any uncontrolled sources; two
    seeded runs match within tolerance on key metrics.
- [ ] Define the conflict-resolution rules for concurrent writes.
  - AC: TBD, rules documented; concurrent writes resolve deterministically per the rule; a test
    exercises a conflicting-write case.
- [ ] Verification harness: metric/invariant stability within tolerance (+ opt-in strict-hash mode).
  - AC: a harness re-runs a scenario and asserts metric/invariant stability within tolerance, with an
    opt-in strict-hash mode.

### A3: Simulation/presentation seam
- [ ] Formalize the logic↔presentation boundary; ensure logic never reads render-only state.
  - AC: TBD, boundary documented; a check/test ensures logic types don't reference render-only
    state; a headless run proves logic runs with no presentation.

### A4: Experiment framework seams & harness
- [ ] A4-1 Simulation Domain Model seam (domain-agnostic entities/topology/interactions/metrics).
  - AC: TBD, a domain-agnostic seam interface exists; the RTS domain implements it and at least the
    transport/graph domain validates it against the same host.
- [ ] A4-2 AI decision policy seam ([deep dive](deep-dives/a4-pluggable-ai.md)): extract
      `IAIDecisionPolicy` from `AIPlayer.OnDecisionTick()`'s subclass override, with
      `SkirmishAIPlayer`'s rule-based state machine as the reference implementation.
  - AC: TBD, `IAIDecisionPolicy` exists and is selectable at runtime; `SkirmishAIPlayer` runs
    behind it with no behavior change; a first neural-network policy is designed per the deep dive.
- [ ] A4-3 Decentralized hierarchical AI seam
      ([deep dive](deep-dives/a4-decentralized-hierarchical-ai.md)): general/commander/soldier
      rank hierarchy of per-unit ANNs, composed behind the existing `IAIDecisionPolicy` seam via a
      `HierarchicalAIDecisionPolicy` adapter.
  - AC: TBD, squad/commander model with death-triggered promotion exists (A4-3a); per-rank
    `ISoldierDecisionPolicy`/`ICommanderDecisionPolicy`/`IGeneralDecisionPolicy` interfaces exist
    with rule-based reference implementations (A4-3b to A4-3d); actuation stays exclusively through
    the soldier rank's `OrderProcessor` calls, matching A2/P2's actuation constraint.
- [ ] Stable seams for pathfinding, partitioning, fidelity policy, storage layout, model import (see
      [seams.md](seams.md)).
  - AC: TBD, each seam has an interface + ≥1 swappable implementation selectable at runtime; all are
    listed in [seams.md](seams.md).
- [ ] Experiment harness: select/configure an implementation, run vs. B1 benchmarks, report to A5.
  - AC: TBD, the harness selects/configures an impl, runs it against B1 benchmarks, and reports to
    A5; demonstrated by swapping one seam impl and comparing results.

### A5: Scenario authoring & visualization ([deep dive](deep-dives/a5-visualization.md))
- [ ] A5-1 Scenario authoring & setup (declarative + capture-from-run; built on the P0 harness).
  - AC: a declarative scenario (on P0) is authored and captured-from-run, then replayed; it round-trips.
- [ ] A5-2 Experiment matrix / parameter sweeps (tooling/visualizer layer).
  - AC: a scenario declares sweep axes that expand to one run per cell; results are keyed for
    comparison; the visualizer can diff two matrix runs. Note: the expansion primitive (cartesian
    product, per-cell execution, keyed JSON output) is provided by P0-5b; A5-2 adds tooling and
    visualization on top.
- [ ] A5-3 Results replayer / visualizer (extends ReplaySketch).
  - AC: the visualizer loads a run, scrubs the timeline, shows metrics/events, and exports headlessly.
- [ ] A5-4 Divergence localization over the tiered recording.
  - AC: given two runs, the tool localizes the first subtree exceeding tolerance; tested on a
    known-divergent pair.
- [ ] A5-5 Authoring & visualization at scale: procedural/large command-set & initial-condition
      generation; streaming/LOD/culling in the replay viewport (reuse B5/B7); bounded-memory run
      loading; authoring/replay throughput tracked by B1. (Pairs with B4a-7.)
  - AC: authoring a large procedural scenario and replaying a long/large run stay within documented
    time/memory budgets (streaming/LOD/bounded-memory loading); B1 tracks the throughput.
- [ ] A5-6 WorldBuilder baseline: audit functional state; ensure basic author → save → load → play
      round-trip works at normal map sizes; document gaps. (Early-start sub-phase, no A1/A4
      dependency; gates B4a.)
  - AC: a normal-sized map with terrain and object placement round-trips through WorldBuilder (author
    → save → load → play in the engine) without errors; functional gaps documented.

---

## Track B: Scale

See [`track-b-scale.md`](track-b-scale.md). Gated on Track P (P7).

- [ ] B1 Profiling & robust benchmarking utilities (reusable harness, statistical rigor,
      regression detection, machine-readable output, hardware characterization).
  - AC: a reusable harness runs repeatable seeded scenarios (incl. the 10k-unit interactive
    reference workload, the [scale anchor](open-questions.md)), reports mean+variance with warmup
    discarded, detects regressions vs. stored baselines, emits machine-readable output, and is reused
    by ≥1 other track.
- [ ] B1 hardware characterization: probe host machine and include context in benchmark report.
  - AC: each benchmark run emits a `hardware` section in the machine-readable output containing CPU
    model/core count/frequency, system memory and measured bandwidth, and GPU device/VRAM; a
    `capacity_estimate` field derives the sustainable unit count at the target frame rate for that
    machine from the per-subsystem timing results; reports from different machines are unambiguously
    tagged so results can be compared across hardware.
- [ ] B1 benchmark matrix: sweep unitCount × scenario × subsystem; produce keyed benchmark results
      for regression comparison.
  - AC: a `ScenarioMatrix`-style benchmark matrix (axes: `unitCount` ∈ {1000, 5000, 10000},
    `scenario` ∈ {allMove, allCombat, mixedActions}, `subsystem` ∈ {pathfinding, combat, economy})
    runs each cell via the B1 harness; per-cell mean+variance is keyed for side-by-side comparison
    and regression detection.
- [ ] B2 Allocation & hot-path cleanup (per-tick allocations, collision-pair lists, cache hot
      queries, data layout).
  - AC: per-tick allocations in the named hot paths are eliminated (allocation profiler shows ~0
    steady-state allocations for the targeted paths); B1 shows no perf regression.
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
- [ ] B5 Rendering at scale (GPU instancing, LOD/impostors, culling, render-bucket/shadow).
  - AC: TBD, instancing/LOD/culling render the near-term anchor of 10k units at ~30 fps in B1 (see
    the [scale-anchor Decision](open-questions.md)); the tool viewports (B4a-7/A5-5) reuse it.
- [ ] B6 Distributed simulation via spatial sharding ([deep dive](deep-dives/b6-distributed-simulation.md)).
  - AC: TBD (see the deep dive). Rough template: a sim runs sharded across ≥2 processes with
    cross-shard consistency within tolerance.
- [ ] B6 scenario matrix: sweep shardCount × scenario × partitionStrategy; assert cross-shard
      consistency for every configuration.
  - AC: a `ScenarioMatrix` (axes: `shardCount` ∈ {1, 2, 4}, `scenario` ∈ {largeBattle,
    multiRegion}, `partitionStrategy` ∈ {spatial-grid, spatial-graph-cut}; invalid combos pruned)
    runs each cell; each asserts unit count matches the single-shard reference and aggregate metrics
    are within tolerance; see the deep dive matrix section.
- [ ] B7 Variable simulation fidelity ([deep dive](deep-dives/b7-variable-fidelity.md)): replicated-state
      fidelity for shared/distributed, view-driven single-node only.
  - AC: TBD (see the deep dive). Rough template: fidelity reduction scales unit count with bounded
    statistical error vs. a full-fidelity baseline.
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

- [ ] C1 Format-agnostic import abstraction (feeds the same `Model`/`ModelInstance` runtime types).
  - AC: TBD, an import abstraction feeds the same `Model`/`ModelInstance` runtime types; a
    stub/second importer validates the seam.
- [ ] C2 Skeleton / animation retargeting.
  - AC: TBD, an external skeleton/animation retargets onto an engine unit and plays correctly.
- [ ] C3 Material mapping.
  - AC: TBD, external materials map to engine materials with acceptable visual parity.
- [ ] C4 First concrete format: static meshes + skinned skeletal animation only.
  - AC: a first concrete external format loads static + skinned skeletal meshes and renders/animates
    in-engine.
- [ ] C5 Authoring & validation tooling (extend ReplaySketch/WorldBuilder; licensing out of scope).
  - AC: TBD, tooling validates an imported asset (geometry/skeleton/material checks) and surfaces
    failures.
- [ ] C6 Extended features (deferred): morph targets, advanced PBR.
  - AC: deferred, out of scope until C1–C5 land; no AC yet.

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
[deep dive](deep-dives/e1-e2-naval-warfare.md). Gated on Track P (P7) only.

- [ ] E1 Water/ground terrain passability (`TerrainLogic`/`Pathfinder` stub replacement, wiring
  `Locomotor.Surfaces` and `AIData.WadeWaterDepth`/`DeepWaterDepth`).
  - AC: TBD, a water-only locomotor cannot move onto dry land, a ground-only unit cannot enter
    deep water but can wade shallow water, and `OverWater` triggers correctly.
- [ ] E2 Ship combat (broadside weapon slots, ram/collision for `SHIP_SUICIDE`, `SHIP_TRANSPORT`
  verification).
  - AC: TBD, two ships exchange broadside fire and destroy each other; a suicide ship rams and
    destroys a target; a transport loads/unloads passengers while moving on water.
- [ ] E3 Naval production (`PORT` building placement/production via existing production-queue
  pattern).
  - AC: TBD, a `PORT` building can only be placed adjacent to water, produces a ship, and the
    ship's rally point resolves correctly on water.
- [ ] E4 Reference naval scenario (port + ships fighting, via ReplaySketch).
  - AC: TBD, a ReplaySketch-authored scenario builds a ship from a port and wins a water
    engagement, asserted headlessly per the P0 harness pattern.

## Track F: Deformable Terrain & Destruction

See [`track-f-terrain-destruction.md`](track-f-terrain-destruction.md). Gated on Track P (P7);
coordinates with B4a (chunked terrain storage), B5 (rendering at scale), and B7 (variable
fidelity, for destruction physics cost). Destruction is physics-based (rigid-body debris/
collapse), not the existing canned model-swap rubble/damage-state system.

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
  - AC: TBD, a rigid-body test scene (e.g. falling/stacking debris) runs identically windowed and
    headless and is measured by the B1 benchmark harness.
- [ ] F5 Physics-based structural destruction (replaces canned `RubbleRiseUpdate`/`ToppleUpdate`
      model swap for at least one building type).
  - AC: TBD, a destroyed building collapses into simulated rigid-body debris that comes to rest
    via physics, rather than swapping to a single pre-baked rubble model.
- [ ] F6 Destruction-terrain interaction (settled debris/craters block pathing & placement).
  - AC: TBD, a destroyed building's settled debris/crater blocks pathing and prevents
    construction on the same footprint until cleared.
- [ ] F7 Persistence, replay & determinism (`StatePersister` + ReplaySketch/P0 capture of
      deformation and physics debris state).
  - AC: TBD, a save/load round-trip preserves terrain deformation and debris resting state; a
    ReplaySketch scenario with an explosive weapon and a destroyed building asserts the resulting
    crater and debris state headlessly within tolerance.
- [ ] F8 Authoring & validation tooling (extend ReplaySketch; no dedicated viewer).
  - AC: TBD, ReplaySketch screenshot mode shows deformed terrain and settled debris, and a
    headless assertion validates crater radius/depth and debris bounds against the triggering
    weapon/building.
