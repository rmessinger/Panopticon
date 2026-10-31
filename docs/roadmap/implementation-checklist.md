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
      `OrderType` (`Panopticon.Tools.ReplaySketch.Core/Model`).
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
    `Panopticon.Tools.ReplaySketch.Tests` exercise `ValidateCommand`, `ExportCommand`, `PlayInGameCommand`,
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
- [x] Check in an agent validation playbook at `src/Panopticon.Tools.ReplaySketch.Maui/AGENTS.md`: the
      documented, repeatable checklist + exact commands agents run to validate a ReplaySketch change
      (Core test suite, screenshot mode for all visual verification, `dotnet format`, the run-and-assert
      flow, and the MAUI testability seams), mirroring `Panopticon.Tools.TestHarness/AGENTS.md`.
  - AC: `ReplaySketch.Maui/AGENTS.md` exists and documents the Core-vs-MAUI split, the exact build/
    test/format commands, the screenshot mode (env vars, commands, error handling), what is
    headlessly validatable vs MAUI-only, a step-by-step validation checklist (including a screenshot
    step for visual changes), and a common-failure-mode table; it is honest about current capability
    (marks the P0-4/P0-5 run-and-assert flow as forthcoming) and is referenced from the P0 deep dive.
  - Completion: `AGENTS.md` created with all sections; screenshot mode (P0-1a) is the sole visual
    verification method; golden-image framework removed.
- [ ] Decouple replay-driven selection state from interactive UI selection state, so a human
      observer in `Panopticon.Tools.TestHarness` (or a live spectator) can drag-select units without
      the recorded replay's own `SetSelection`/`ClearSelection` orders overwriting their selection or
      `OrderGenerator.ActiveGenerator`.
  - AC: while replaying a `.rep` with `Panopticon.Tools.TestHarness`, a human click-and-drag over units
    shows the selection rectangle and yields the dragged units as selected, even on frames where the
    replay itself issues a `SetSelection`/`ClearSelection` order for the same player; a headless test
    asserts the human's `SelectionSystem` state survives a replay-issued selection order on the same
    frame.
  - Note: follow-up to the "Allow box-selecting any object's stats while watching a replay" fix
    (`SelectionSystem.MultiSelect`); see `WI-P0-18`.
- [ ] Add an inactivity monitor to `Panopticon.Tools.TestHarness` to replace the removed fixed
      `MaxReplayFrames` cap: force-exit only when the replay stalls (no
      `ActionDispatched`/`ActionCompleted` progress for a configurable window), not on total run
      length, so long scenarios (e.g. the full-build replay) aren't truncated early.
  - AC: a stalled scenario (no further action dispatch/completion) force-exits within the configured
    window with a distinct `[inactivity-timeout]` log message; a scenario making steady progress to
    its natural end is not cut off early.
  - Note: see `WI-P0-19`.
- [x] P0-8 TestHarness catalog: the TestHarness publishes its scenarios and config parameters as a
      versioned `HarnessCatalog` JSON via `--list-scenarios`, and accepts `--config <path>`.
  - AC: `--list-scenarios` with no game install exits 0 with valid catalog JSON; coverage tests fail
    when a `BuiltInScenario` value lacks a descriptor or a public `TestHarnessConfig` property lacks
    `[HarnessParameter]`; `--config` runs the given file without modifying the executable's
    `TestHarness.json`.
  - Note: see `WI-P0-20` to `WI-P0-23`.
- [x] P0-8 Launcher clients and view models: ReplaySketch.Core loads the catalog, filters scenarios,
      edits applicable parameters with validation, runs and stops the TestHarness with streamed
      output, and stores presets.
  - AC: headless tests in `Panopticon.Tools.ReplaySketch.Tests` cover catalog load errors
    (`HRN100` to `HRN103`), text/category/mode filtering, per-kind parameter validation,
    override-only launch requests, exit-code meaning lookup, cancellation, and preset round trip
    plus `TestHarness.json` import.
  - Note: see `WI-P0-24` to `WI-P0-28`.
- [x] P0-8 Harness tab: ReplaySketch.Avalonia `Harness` tab bound to the Core launcher view model.
  - AC: `REPLAYSKETCH_SCREENSHOT_TAB=harness` screenshot shows the live catalog's scenarios and
    parameter groups; the Map tab is unchanged; launching `PbrMaterialProof` from the tab reports
    exit code 0 with its meaning.
  - Note: see `WI-P0-29`.

### P1: Skirmish setup & player relationships ([deep dive](deep-dives/p1-skirmish-setup.md))
- [x] Resolve `PlayerManager.OnNewGame()` `// TODO: Setup player relationships` (allies / enemies /
      neutrals): `src/Panopticon.Simulation/Logic/PlayerManager.cs:45`.
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

P6 corrective follow-ups ([plan](plans/macos-battle-artifacts.md)):

- [ ] [WI-P6-06](agent/cards/WI-P6-06-design-jet-crash-lifecycle.md): ground the jet crash lifecycle.
  - AC: source-grounded airborne/grounded/impact/removal contract and populated diagrams reconcile the implementation card or record a concrete blocker.
- [ ] [WI-P6-07](agent/cards/WI-P6-07-implement-jet-crash-lifecycle.md): implement jet crash and final cleanup after WI-P6-06 and WI-G6-03.
  - AC: lethal damage leads to authored crash/removal, once-only lifecycle cleanup, and checkpoint continuation; physical removal is asserted separately from logical death.

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
  - AC: a real UI/exit consumer of `GameOutcome` exists for interactive play; `Panopticon.Tools.TestHarness`
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
    `src/Panopticon.Tools.TestHarness/AGENTS.md`. Today a `tty`-type shell has no `DISPLAY` and cannot
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
- [ ] A1-6 Operational recording and checkpoint re-simulation
      ([deep dive](deep-dives/a1-6-operational-recording.md)): `Operational` recording mode,
      sparse engine checkpoints, and a whole-world reconstructor.
  - [ ] A1-6a Checkpoint coverage audit (`WI-A1-13`); persisted `SageRandom` logic random (`WI-A1-14`).
  - [ ] A1-6b Engine checkpoint format (`WI-A1-15`); host restore (`WI-A1-16`).
  - A1-6c Withdrawn: run identity (`WI-A1-17`) and operational state hash (`WI-A1-18`).
  - [ ] A1-6d Recording modes and cadence (`WI-A1-19`); operational stream (`WI-A1-20`).
  - [ ] A1-6e Operational run bundle: checkpoint scheduling and manifest (`WI-A1-21`).
  - [ ] A1-6f Reconstructor and sinks (`WI-A1-22`); in-memory reconstruction cache (`WI-A1-23`).
  - [ ] A1-6g Conformance evidence (`WI-A1-24`).
  - AC: RTS and transport-graph `Operational` runs rebuild randomly chosen frames from their
    checkpoints; restore at a checkpoint frame equals a `Full` capture of that frame; rebuilt
    frames satisfy domain invariants; `Full` mode JSON and artifacts are byte-identical to the
    pre-A1-6 output.

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
  - Done: `StatisticalRunHarness` in `Panopticon.Tools.ReplaySketch.Core` aggregates per-run
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
    logic-owned; `LogicPresentationBoundaryTests` reflects over `Panopticon.Logic` types and fails
    on any presentation-type reference (empty allowlist); headless logic runs are A1's host tests
    plus the P7/P6b suites, all green.

### A4: Experiment framework seams & harness
- [x] A4-1 Simulation Domain Model seam (domain-agnostic entities/topology/interactions/metrics).
  - Done: contracts in `src/Panopticon.Simulation/Simulation/` (`ISimulationDomainModel`,
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
- [x] A4-4 Domain state projection
      ([deep dive](deep-dives/a4-4-domain-state-projection.md)): optional
      `IDomainStateProjection` with a versioned schema, keyed multi-field records, a separate
      domain-state artifact, and a schema-driven conservation check.
  - [x] A4-4a Contracts and schema validation (`WI-A4-42`).
  - [x] A4-4b Frame persistence, diff/apply, reconstruction, conservation residuals (`WI-A4-43`).
  - [x] A4-4c Recorder, artifact, `RunRecording`/`RunOutput` wiring (`WI-A4-44`).
  - [x] A4-4d Transport-graph reference projection (`WI-A4-45`).
  - AC: the transport domain records and reconstructs state records at every sampled frame with
    empty conservation residuals; RTS run-output JSON is byte-identical to the pre-A4-4 output.
- [ ] Stable seams for pathfinding, partitioning, fidelity policy, storage layout, model import (see
      [seams.md](seams.md)).
  - AC: TBD, each seam has an interface + ≥1 swappable implementation selectable at runtime; all are
    listed in [seams.md](seams.md).
  - Partial (WI-A4-31, WI-A4-36): the audit grounds all five seams and model import is registered
    with two config-selected implementations; pathfinding, partitioning, fidelity, and storage stay
    gated on B4/B3/B6/B7 contracts (WI-A4-32..35).
- [x] Experiment harness: select/configure an implementation, run vs. B1 benchmarks, report to A5.
  - AC: TBD, the harness selects/configures an impl, runs it against B1 benchmarks, and reports to
    A5; demonstrated by swapping one seam impl and comparing results.
  - Done (WI-A4-37..41): the ai-policy seam swaps rule-based and neural implementations per cell
    through one B1 scenario (`b1.ai-policy`) and one comparison report (`a4.experiment/1`); CLI:
    `--experiment <definition.json>`.

### A5: Scenario authoring & visualization ([deep dive](deep-dives/a5-visualization.md))
- [x] A5-1 Scenario authoring & setup (declarative + visual + capture-from-run; built on the P0 harness).
  (`WI-A5-03`, `WI-A5-06`, `WI-A5-10..14`, `WI-A5-23`, `WI-A5-30`, `WI-A5-32..33`)
  - AC: a declarative scenario round-trips and runs; capture-from-run round-trips its documented
    scenario-initializable projection without claiming save-state equivalence; visual editing covers
    initial population, parameters, and domain-appropriate topology; validation and bounded preview
    reject invalid scenarios before a long run.
    Scenario artifact + loader: WI-A5-03; validation: WI-A5-10; generators: WI-A5-23;
    capture-from-run: WI-A5-11; preview: WI-A5-13; population/parameter/topology editors:
    WI-A5-14/32/33; WorldBuilder binding: WI-A5-30.
- [x] A5-2 Experiment matrix / parameter sweeps (tooling/visualizer layer). (`WI-A5-15..16`)
  - AC: a scenario declares sweep axes that expand to one run per cell; results are keyed for
    comparison; the visualizer can diff two matrix runs. Note: the expansion primitive (cartesian
    product, per-cell execution, keyed JSON output) is provided by P0-5b; A5-2 adds tooling and
    visualization on top.
    Declarative typed bindings + serializable filters: WI-A5-15; keyed per-cell run-bundle sets
    with a relocatable aggregate manifest: WI-A5-16; run diffing through the WI-A5-05/22
    comparator surfaced in the Results view.
- [x] A5-3 Results replayer / visualizer (extends ReplaySketch). (`WI-A5-04`,
      `WI-A5-08..09`, `WI-A5-11..12`, `WI-A5-18..21`, `WI-A5-24..25`, `WI-A5-29`,
      `WI-A5-31`)
  - AC: the visualizer loads a run, plays/pauses/fast-forwards/scrubs the timeline, supports
    event-to-frame navigation, shows domain state plus metrics/events/fidelity, and exports
    headlessly.
    Streaming/culling + LOD batching landed in WI-A5-24/25; headless export in WI-A5-20.
- [x] A5-4 Divergence localization over the tiered recording. (`WI-A5-05`,
      `WI-A5-22`)
  - AC: given two runs, the tool localizes the first subtree exceeding tolerance; tested on a
    known-divergent pair.
    T2 inspection: `DivergenceDetailInspector` matches the first divergent locus to a captured
    window and derives entity/quantity rows (matched/missing/unarmed/incompatible states).
- [x] A5-5 Authoring & visualization at scale: procedural/large command-set & initial-condition
      generation; streaming/LOD/culling in the replay viewport (reuse B5/B7); bounded-memory run
      loading; authoring/replay throughput tracked by B1. (Pairs with B4a-7.) (`WI-A5-23`,
      `WI-A5-24..26`, `WI-A5-29`)
  - AC: authoring a large procedural scenario and replaying a long/large run stay within documented
    time/memory budgets using region-indexed state, streaming/LOD/culling, and bounded-memory
    loading; B1 tracks the throughput.
    Budgets are the `a5.*` benchmark IDs measured by B1 (see the scale-budget section in the
    [implementation plan](implementation/track-a-a5-scenario-visualization.md)); the regression
    rule is B1's baseline CI-non-overlap policy, with no standalone stopwatch gates in A5.
- [x] A5-6 WorldBuilder baseline: audit functional state; ensure basic author → save → load → play
      round-trip works at normal map sizes; document gaps. (Early-start sub-phase, no A1/A4
      dependency; gates B4a.) (`WI-A5-01..02`, `WI-A5-27..28`, `WI-A5-34..35`,
      [audit + baseline](implementation/track-a-a5-6-worldbuilder-baseline.md))
  - AC: a normal-sized map with terrain and object placement round-trips through WorldBuilder (author
    → save → load → play in the engine) without errors; functional gaps documented.
  - Done: audit landed (WI-A5-01); edit application, writable new maps, companions, and
    skirmish-playable new-map defaults repaired (WI-A5-27/28/34/35); round-trip incl. undo/redo
    and the engine play leg proven by test (WI-A5-02). B4a's prerequisite is satisfied.

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
- [x] B1-1 Benchmark hook audit and report schema (`WI-B1-01`).
  - AC: the audit cites existing hooks; the JSON record is A2-compatible.
    Done: hook table and appendix re-verified against source in
    [`b1-benchmarking.md`](deep-dives/b1-benchmarking.md); tick, system, environment, and statistic
    schema fields fixed; B1-2..B1-5 type names recorded.
- [x] B1-2 Macro harness skeleton (`WI-B1-02`).
  - AC: seeded warmup and measured runs execute through `SimulationHost`.
    Done: `Panopticon.Benchmarks.Simulation` CLI + `SimulationBenchmarkRunner`; proven on
    `b1.empty-host` (300/300 frames, ~730 ms, exit 0) with the A1 recorder attached; 15
    install-free tests.
- [x] B1-3 Tick, allocation, GC, memory, and system spans (`WI-B1-03`).
  - AC: each run records per-tick and per-system timings plus allocation and GC counts.
    Done: `BenchmarkMetricRecorder` + `LogicStepper.SystemSpan` seam (gameLogic, network,
    scene3d, partition); proven on `b1.empty-host` with 300 tick samples and 1200 system
    spans; 7 install-free tests.
- [x] B1-4 JSON run records and CSV summaries (`WI-B1-04`).
  - AC: records round-trip and carry hardware context.
    Done: `BenchmarkReportWriter` (camelCase JSON authority + derived CSV with run and
    environment columns); round-trip stable; CSV proven with 21 metric rows on
    `b1.empty-host`; 6 install-free tests.
- [x] B1-5 Median bootstrap confidence intervals and regression comparison (`WI-B1-05`).
  - AC: a seeded bootstrap is reproducible; a stored baseline flags regressions.
    Done: `BootstrapStatistics` (seeded, deterministic) + `BenchmarkStatistics` CI-overlap
    rule; `--baseline` proven live on `b1.empty-host` (changed metrics flagged, overlapping
    metric unflagged); 9 install-free tests.
- [x] B1-6 Seeded reference scenarios including the 10k-unit anchor (`WI-B1-06`).
  - AC: scenarios run repeatably; micro benchmarks use BenchmarkDotNet.
    Done: `b1.1k-move` (17.7 ms median), `b1.mixed-combat` (churn proven, 10-13 ms median),
    `b1.path-query` (3.9 ms), `b1.10k-scale-anchor` (150.5 ms median, 4.5x real-time budget);
    BenchmarkDotNet untouched in `Panopticon.Benchmarks.DataStructures`; 20 install-free tests.
- [ ] B1-7 B4a, B4b, and Western Front adapters (`WI-B1-07`).
  - AC: each consumer emits B1 records without a separate harness.
- [ ] B2 Allocation & hot-path cleanup (per-tick allocations, collision-pair lists, cache hot
      queries, and data layout).
  - AC: per-tick allocations in the named hot paths are eliminated (allocation profiler shows ~0
    steady-state allocations for the targeted paths); B1 shows no performance regression.
- [ ] B3 Parallel & data-oriented logic (intent/command buffers + conflict resolution; incremental
      SoA storage behind the module API).
  - AC: TBD, intent/command buffers + conflict resolution land behind the module API with no
    behavior change vs. baseline (verification harness within tolerance); SoA storage is swappable.
- [x] B4 Spatial & pathfinding scaling (partition hot path, hierarchical/flow-field, group
  movement, async path requests) (`WI-B4-01..06`,
  [plan](implementation/track-b-b4-spatial-scaling.md)).
  - AC: partition maintenance holds a 10 ms median `system.partition.spanMs` at contact in
    the M1 full-scale re-run, with pair-once `OnCollide` delivery and no per-tick update
    allocations; group movement scales path requests with squads; async completions are
    frame-bounded and deterministic; hierarchical/flow-field lands only on a failed
    measured latency gate; no pathing regressions on baseline maps.
  - Outcome: pair-once delivery and a 71 percent allocation cut landed (WI-B4-02); the
    10 ms contact budget is still missed at a 259 ms median and the residual is the genuine
    workload, recorded as the stop point with follow-ups (cheaper exact test, resting-contact
    throttling, B3 parallelism) left to a new card. Group movement, async frame-bounded
    completions with queue-level determinism, and the no-op hierarchical gate decision all
    hold with recorded numbers.
- [ ] B4a Larger maps via WorldBuilder ([deep dive](deep-dives/b4a-larger-maps.md)): region editing,
      large-map wizard, terrain streaming/LOD, chunked storage, round-trip at scale,
      large-population scenario authoring (B4a-7: bulk/procedural object placement, region-based
      multi-object edit/undo, instanced editor rendering, pairs with A5-5). Depends on A5-6.
  - AC: TBD (multi-sub-phase, see the deep dive). Rough template: very large maps author → save →
    reload → play within budgets; region edits are O(brush) not O(map area); large object populations
    are placeable/editable interactively.
- [ ] B4a-1 WorldBuilder large-map limits audit (`WI-B4a-02`).
  - AC: every size, memory, and format limit is cited by file and line.
- [ ] B4a-2 Region-delta editing and undo (`WI-B4a-03`..`05`).
  - AC: brush and layer edits cost O(brush) and undo restores exact values.
- [ ] B4a-3 Large-map wizard presets and 16-bit defaults (`WI-B4a-06`).
  - AC: presets create maps at each size with 16-bit heights.
- [ ] B4a-4 CDLOD terrain streaming (`WI-B4a-07`..`09`).
  - AC: quadtree selection is deterministic and only visible patches load.
- [ ] B4a-5 Runtime terrain chunk store (`WI-B4a-01`, `WI-B4a-10`).
  - AC: the store serves chunks behind the grounded contract; H3 owns the disk format.
- [ ] B4a-6 Large-map round-trip and play (`WI-B4a-11`).
  - AC: a large map authors, saves, reloads, and plays within B1 budgets.
- [ ] B4a-7 Large-population authoring (no cards yet; pairs with A5-5) (`TBD`).
  - AC: bulk placement and region multi-object edits stay interactive.
- [x] B5 Rendering at scale
      ([deep dive](deep-dives/b5-rendering-at-scale.md)): GPU instancing for repeated unit
      models through the source-agnostic `RenderItem` seam (W3D, glTF/PBR, and any C1
      importer), a distance LOD gate (small-feature culling; impostors recorded as a
      follow-up gap), sort-once-per-frame bucket filtering, and submit-overhead removals
      (index-buffer bind hoist, per-item debug-group gating), all behind render-path
      toggles; a `RenderingScaleProbe` TestHarness scenario measures render spans, draw
      counts, and instanced batches headlessly.
  - AC: at the near-term anchor of 10k repeated rigid models, the probe reports the scene
    rendering with GPU instancing (one batch per repeated mesh part, instance count near
    item count) plus the LOD gate within the ~30 fps frame budget on the dev-host GPU, or
    the miss is recorded with its measurement; instanced vs per-item output is
    pixel-identical for eligible items; skinned meshes (the b1 anchor Rangers) and the
    NormalMapped/Simple families are recorded as explicit non-instanced gaps; the tool
    viewports (B4a-7/A5-5) reuse it by rendering through the same `RenderPipeline` (no
    source-specific path).
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
- [ ] B6-1 Partition, halo, and boundary message design (`WI-B6-01`..`03`).
  - AC: contracts cover front-strip partitions, halo rules, and a versioned JSON message.
- [ ] B6-2 In-process sharding for the Western Front (`WI-B6-04`..`07`).
  - AC: the in-process coordinator runs W shards in lockstep with exactly-once handoff.
- [ ] B6-6 Compact codec, multi-process transport, fault diagnostics (`WI-B6-08`..`10`).
  - AC: the binary codec matches JSON results; transport faults report typed diagnostics.
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

- [ ] B7-1 Fidelity model, registry, replicated policy inputs, projection (`WI-B7-01`..`03`).
  - AC: `FidelityRegistry` states project into domain state and metrics.
- [ ] B7-4 Reusable group aggregation contract and error hooks (`WI-B7-04`..`05`).
  - AC: the W2 aggregation runs through the generic contract with error hooks.
- [ ] B7-6 Fidelity visualization bridge and error report (`WI-B7-06`..`07`).
  - AC: A5 shows fidelity states; the report declares transitions and error.

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
- [x] C4 First production format: static meshes + skinned skeletal animation only.
  - AC: strict SharpGLTF.Core adapters load repository-owned `.gltf` and `.glb` fixtures through
    isolated virtual roots; static nodes, split triangle primitives, sparse accessors, one skin,
    inverse binds, linear/step named animation, rigid attach nodes, and core PBR data convert into
    C1-C3 values; the one-meter `(x, y, z)` to `(z, x, y)` fixture renders at approximately
    3.28084 world units, upright and +X-forward with correct winding, and every excluded or
    required unsupported feature fails contextually.
- [x] C4a Empire Earth II NIF 10.1 proof of concept
      ([deep dive](deep-dives/ee2-gamebryo-model-storage.md#9-c4a-proof-of-concept-scope)).
  - AC: `nwg1_galley.nifcache` and `ab11_TwinEngineBomber.nifcache` load from an
    operator-supplied `graphics.zip` through C1, materialize as runtime `Model` assets, and
    produce recognizable captures; the Bomber uses its highest-detail LOD, source texture names
    survive import while rendering uses untextured diffuse materials, and no EE2 assets are
    committed.
- [x] C4b EE2 cooked texture decoding.
  - AC: texcache NiPixelData containers decode with validated contiguous DXT mip
    chains; both C4a proof captures render textured through `textures.zip` with the
    alpha-blended Bomber materials intact, and the untextured fallback still applies
    when a texcache entry is absent; `.pcpatch` stays out of scope.
- [x] C4c EE2 rigid KF animation
      ([deep dive](deep-dives/c4c-ee2-rigid-animation.md)).
  - AC: the seven DDF-bound War Galley and Twin-Engine Bomber `.kf` clips import through a
    `<model>#kf=<path>` key as bone-name, rest-relative C1 channels; unit-scale NIF nodes become
    bones with rest-pose parity to the C4a bake; XYZ Euler keys resample at 30 Hz with the
    NifSkope Hermite convention and a sub-90-degree step limit; clips bind through C2, and
    deterministic textured captures show oar sweep, a quarter propeller turn at 1/12 s, and the
    TakeOff ground-to-flight pose; the unreferenced `ab11_TwinEngineBomber.kf` fails naming
    `prop_1`; skinning, root motion, and other controllers fail explicitly.
- [x] C4d EE2 skinned character animation
      ([deep dive](deep-dives/c4d-ee2-skinned-animation.md)).
  - AC: NIF node, shape, skin, and Euler rotations use the column-vector convention, and the C4a
    and C4c descriptors and captures are regenerated once and locked again; `NiSkinInstance`,
    `NiSkinData`, and `NiSkinPartition` parse with validated references; C1, the runtime vertex
    layout, all mesh vertex shaders, and glTF import carry four influences, while W3D output is
    unchanged; `CIV1_Druid_british` imports 648 LOD0 vertices with `NiSkinData` inverse binds,
    bind-pose parity within 1e-4, and its three- and four-influence vertices intact; its five
    DDF-bound clips bind through C2, and the matching `walk01` channels have identity offsets at
    time 0; deterministic textured captures show the Druid upright and deforming; more than four
    influences and weight sums further than 0.01 from 1 fail explicitly.
- [x] C4e EE2 property animation
      ([deep dive](deep-dives/c4e-ee2-property-animation.md)).
  - AC: NIF property controller blocks decode with exact stream consumption; C1 and runtime
    animation targets cover sub-object visibility and material slots; the NIF converter emits UV,
    material color, alpha, and visibility channels for DDF-bound proof clips; runtime playback
    applies per-instance material state without mutating shared materials; deterministic
    installed-asset tests and captures prove all four families; unsupported flags, slots, morph
    mixes, missing targets, and malformed data fail with asset context.
- [x] C4f EE2 scale animation
      ([deep dive](deep-dives/c4f-ee2-scale-animation.md)).
  - AC: C1 exposes bone rest scale and scale animation channels; C4c scale rejection is replaced
    by shared C4c curve evaluation for linear and quadratic scale keys; DDF-bound skinless scale
    clips bind and sample correctly; fixed-function normals and bounds remain valid under uniform
    scale; singular rest scale reports a contextual unsupported-feature error.
- [x] C4g EE2 morph animation
      ([deep dive](deep-dives/c4g-ee2-morph-animation.md)).
  - AC: NIF 10.1 morph controller and morph data blocks parse with validated references; C6
    morph-target interchange and the request-scoped gate exist; KF-owned target data pairs into
    model-side target sets; morph weight animations bind by primitive and target names; CPU morph
    deformation runs before skinning; farm yam captures at weights 0, 0.5, and 1 show visible
    deformation without changing feature-disabled output.
- [x] C4h EE2 particle effects
      ([deep dive](deep-dives/c4h-ee2-particle-effects.md)).
  - AC: the proof `fx_*` NIFs parse to exact end of file; CPU-only imported particle effect
    values materialize onto the existing `FXParticleSystemTemplate` and `ParticleSystemManager`
    paths without a new particle runtime; DDF world or bone attachments resolve through C4c node
    names; captures are seeded deterministically; `NiPSys*`, bomb, morph, UV, texture-transform,
    mesh-emission, collision, and unsupported gravity semantics fail with contextual errors.
- [x] C4i EE2 legacy KF formats
      ([deep dive](deep-dives/c4i-ee2-legacy-kf-formats.md)).
  - AC: NIF 10.0.1.0 headers and KF blocks decode; legacy text keys, flags, and uppercase paths
    normalize; rigid proof clips bind and capture; NetImmerse 4.2.2.0, property, morph, and
    skinned legacy cases fail with owning-phase diagnostics.


- [x] C5 Authoring & validation tooling (extend ReplaySketch; licensing out of scope).
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
- [ ] C7a StarCraft II CASC storage
      ([deep dive](deep-dives/sc2-casc-m3-storage.md)).
  - AC: an in-house read-only reader decodes `.build.info`, version 7 `.idx`, BLTE `N` and `Z`,
    the encoding file, and the MNDX root with MAR name restore; `CascFileSystem` exposes
    case-insensitive `/` paths with duplicate paths deduplicated; encrypted and unknown frames,
    unsupported versions, and conflicting duplicates fail explicitly; installed tests enumerate
    675,745 entries and match the proof model sizes and content keys.
- [ ] C7b StarCraft II static M3 models
      ([deep dive](deep-dives/sc2-casc-m3-storage.md)).
  - AC: `MODL` v23 proof models import through `M3ModelImporter` with the recorded vertex and
    batch counts; opaque standard materials map DDS diffuse, normal, specular, and emissive onto
    C3 PBR through importer-side decoding to embedded images; non-opaque batches and unsupported
    layers are skipped with diagnostics; the `S(10) * Rz(+90 degrees)` conversion is validated by
    a capture beside a Generals Ranger; unsupported versions, vertex flags, material types, and
    DDS formats fail with context.
- [ ] C7c StarCraft II skinned M3 animation
      ([deep dive](deep-dives/c7c-sc2-skinned-animation.md)).
  - AC: C1 and the runtime gain per-axis bind scale, `ScaleX/Y/Z` channels, and absolute local
    clip composition without changing existing format output; the skin-bone skeleton binds from
    `IREF` with four-influence vertices; embedded sequences and `.m3a` clips (bound by animation
    id with name checks) import through C2 for the twelve proof clips; deterministic captures show
    the expected poses, and section 11 failures fire in synthetic tests.
- [ ] C8a SAGE-family W3D corpus
      ([deep dive](deep-dives/c8-sage-family-w3d.md); cards `WI-C8a-01..04`).
  - AC: installed tests parse and convert every `.w3d` in Generals, Zero Hour, BFME, BFME2, and
    RotWK (over BFME2) through the per-game resolvers; unknown chunks yield `W3D001` diagnostics and
    top-level ones roundtrip byte-exactly; files that fail to parse yield one `W3D100` diagnostic
    and a null asset; failures equal a checked-in manifest of names and reason codes; launcher
    probes accept `Generals.exe`; Generals and Zero Hour import output is unchanged.
- [ ] C8b BFME-family static meshes
      ([deep dive](deep-dives/c8-sage-family-w3d.md); cards `WI-C8b-01..04`).
  - AC: shader-material families resolve case-insensitively to `NormalMapped` or `Simple`, other
    families fall back to `Simple` with a `W3D200` diagnostic; per-face shader-material ids and
    `WsClassicEnv` convert; the manifest has no shader-family, multi-id, or `WsClassicEnv` entries;
    captures place each proof model textured beside a Generals Ranger; team color and `SHDMESH`
    each have a recorded decision.
- [ ] C8c BFME-family skinning and animation
      ([deep dive](deep-dives/c8-sage-family-w3d.md); cards `WI-C8c-01..04`).
  - AC: compressed visibility bit channels import; channel type 15 is decoded or skipped by a
    recorded decision; installed tests show well-formed channels for every converted clip,
    including adaptive-delta and motion channels; Gondor soldier, Mumakil, and wolf rider cycles
    bind with no missing targets and capture byte-identically twice; Generals animation output is
    unchanged.
- [ ] C9a Total War: THREE KINGDOMS packs
      ([deep dive](deep-dives/c9-total-war-3k-models.md)).
  - AC: a read-only `PackFileSystem` enumerates every `PFH5` pack with the recorded per-pack
    counts and case-insensitive paths; compressed or encrypted entries and unknown pack types fail
    with pack and path; the DB reader resolves the proof units through `main_units`,
    `unit_variants`, `variants`, `composed_entities`, and `mens`.
- [ ] C9b TW3K static RMV2 parts
      ([deep dive](deep-dives/c9-total-war-3k-models.md)).
  - AC: `RMV2` version 7 and 8 parts with vertex formats 0, 3, and 4 import through C1 with the
    recorded LOD, vertex, and index counts; `.wsmodel` and `.xml.material` maps land on C3 PBR from
    BC1, BC3, and BC7 DDS, with the material-map channel layout recorded; proof captures place a
    helmet part and a horse body beside a Generals Ranger; other versions and formats fail with
    context.
- [ ] C9c TW3K skeletons and animation
      ([deep dive](deep-dives/c9-total-war-3k-models.md)).
  - AC: `male01` and `horse01_3k` import as bone hierarchies with recomputed inverse binds; version
    7 clips expand static and animated tracks into C1 TRS channels; animation tables and fragments
    resolve slots to clips; spearman and horse proof clips bind with no missing targets and capture
    deterministically; other clip versions fail with context.
- [ ] C9d TW3K composite soldiers
      ([deep dive](deep-dives/c9-total-war-3k-models.md)).
  - AC: `.variantmeshdefinition` slots, references, and weapon attach points compose one model per
    seed; the same seed yields the same composition; a spearman squad renders with distinct
    variants; a cavalry rider and horse play `CHARGE` on one clip clock; imposters and
    merged-variant rendering are left to B5.
- [x] C10a AoE3 DE archives and textures
      ([deep dive](deep-dives/c10-aoe3de-granny-models.md)); cards `WI-C10a-01..03`.
  - AC: a read-only `BarFileSystem` reads all 42 `.bar` v6 tables exactly to end of file; `alz4`
    entries decompress to their recorded sizes; XMB decodes to an XML tree; `.ddt` BC1, BC2, BC3,
    BGRA, and R8 images decode to RGBA through the imported-image decoder; encrypted sound entries and unknown flags fail with
    path.
- [x] C10b AoE3 DE static Granny models
      ([deep dive](deep-dives/c10-aoe3de-granny-models.md)); cards `WI-C10b-01..04`.
  - AC: a type-tree-driven reader loads uncompressed Granny version 7 files; musketeer and hussar
    horse meshes, bind poses, and `.material.XMB` maps import through C1 and C3 with the Masks
    layout recorded; `ArtToolInfo` axes and units normalize to engine space; captures place both
    beside a Generals Ranger; compressed sections fail with file, section, and compression type.
- [x] C10c AoE3 DE skinned animation
      ([deep dive](deep-dives/c10-aoe3de-granny-models.md)); cards `WI-C10c-01..04`.
  - AC: bone bindings import as up to four-influence skins; `DaK32fC32f` transform tracks resample
    to C1 TRS channels and other curve codecs fail with context; animfile XML resolves models,
    tech variants, clips, and attachments; musketeer march and volley clips and hussar idles bind
    with no missing targets and capture deterministically.
- [ ] C10d Granny section decompression (deferred)
      ([deep dive](deep-dives/c10-aoe3de-granny-models.md#6-references-and-licensing)).
  - AC: starts only after a recorded provenance and license review of the decompressor source;
    Oodle1 and BitKnit2 sections decompress to their recorded sizes; the falconet imports and plays
    `falconet_limber`; uncompressed output is unchanged.
- [ ] C11a Rise of Legends archives
      ([deep dive](deep-dives/c11-rise-of-legends-models.md)).
  - AC: a locator finds an installed game directory by `BIGS/`; a read-only file system enumerates
    the 83 WAR-BUILDER archives with zlib payloads at their recorded sizes; the nine
    variant-header config archives fail with path; installer repacks are not read.
- [ ] C11b Rise of Legends static BH3 models
      ([deep dive](deep-dives/c11-rise-of-legends-models.md)).
  - AC: the chunk reader parses every `.bh3` tree; Assassin, Zeke, and Jaguar Knight import with
    the recorded vertex, triangle, and bone counts and rigid per-bone skins; DDS diffuse, normal,
    and team-color maps land on C3 PBR; unknown chunk ids are skipped with a diagnostic; captures
    place the proof units beside a Generals Ranger.
- [ ] C11c Rise of Legends animation and binding
      ([deep dive](deep-dives/c11-rise-of-legends-models.md)).
  - AC: chunk 51 and 50 weights import as blended skins after semantics are verified against rigid
    output; `.bha` clips resample to C1 TRS channels with the key time unit recorded; stance, LOD,
    and attachment binding come from the unit XML; Assassin and Jaguar Knight idle and walk stances
    bind with no missing targets and capture deterministically.
- [ ] C12a Total War: SHOGUN 2 packs
      ([deep dive](deep-dives/c12-total-war-shogun2-models.md)); cards `WI-C12a-01..02`.
  - AC: the shared pack file system reads every `PFHs` index exactly to end of file with the
    recorded per-pack counts and case-insensitive paths; LZ4 block entries decode to their recorded
    sizes; unknown pack types and flags fail with pack and path; the DB reader resolves the yari
    ashigaru unit and the columns leading to its animation table are recorded.
- [ ] C12b SHOGUN 2 static models and parts
      ([deep dive](deep-dives/c12-total-war-shogun2-models.md)); cards `WI-C12b-01..05`.
  - AC: `RMV2` version 0 to 2 mesh headers and vertex formats are recorded and import through C1;
    `VMPF` version 3 parts import in bind pose; base colour, MRA, normal, and colour-mask maps land
    on C3 PBR with the MRA channel order and normal swizzle recorded; captures place a castle gate
    piece, the Armstrong gun, an ashigaru head, and a horse body beside a Generals Ranger; other
    versions fail with context.
- [ ] C12c SHOGUN 2 animation and composite soldiers
      ([deep dive](deep-dives/c12-total-war-shogun2-models.md)); cards `WI-C12c-01..06`.
  - AC: version 1 clips import their embedded bone tables as runtime hierarchies and their frames
    as C1 TRS channels; `VMPF` skins import with up to four influences; animation tables and
    fragments resolve slots to clips; `.unit_variant` slots compose one model per seed, and the
    same seed yields the same composition; a yari ashigaru squad and the horse bind with no missing
    targets and capture deterministically.

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
G2 preset layer so the `Legacy` preset reproduces the original look.

- [x] G1 High-DPI drawable & resolution correctness (`ALLOW_HIGHDPI`, drawable-size swapchain,
      one pixels-versus-points convention across `GameWindow`/`RenderTarget`/input).
  - AC: on a display with a non-unity scale factor the swapchain and a captured frame match
    the window's native pixel dimensions, and cursor/input hit-testing stays correct.
  - [x] G1-1 Classify every window/panel size consumer as logical points or physical pixels
        ([`WI-G1-01`](agent/cards/WI-G1-01-audit-size-consumers.md)).
    - AC: every `ClientBounds`/`WindowScale`/`CursorScale`/`Frame`/`Viewport` read in `src/` is
      classified, and sites feeding both spaces are called out.
  - [x] G1-2 `AllowHighDpi` window creation, `SDL_GetWindowSizeInPixels` interop,
        `DrawableBounds`/`DpiScale`, swapchain sized in pixels
        ([`WI-G1-02`](agent/cards/WI-G1-02-allow-highdpi-drawable-size.md)).
    - AC: the window is created with `AllowHighDpi` via `new Sdl2Window(...)`, `WindowScale`
      enlargement applies only where `DpiScale` is 1, and `Swapchain.Resize` is fed `DrawableBounds`.
  - [x] G1-3 Render target, 3D viewport and GUI layout in pixels; ImGui framebuffer scale
        ([`WI-G1-03`](agent/cards/WI-G1-03-route-render-surfaces-to-pixels.md)).
    - AC: every window-backed `EnsureFrame` call site passes `DrawableBounds`, `Game.Viewport` and
      the WND/APT managers receive the pixel size, and ImGui renders with `DpiScale` as its
      framebuffer scale.
  - [x] G1-4 Convert mouse input from points to pixels at the `GameWindow` boundary
        ([`WI-G1-04`](agent/cards/WI-G1-04-scale-2d-and-picking.md)).
    - AC: a centre mouse position keeps its normalised position and maps to the same WND client
      point at `DpiScale` 1 and 2, and `Camera` holds no `DpiScale` reference.
- [x] G2 Graphics settings & fidelity presets (`Legacy`/`Enhanced`; settings layer, not an
      experiment seam). The parsed `StaticGameLod` assets name several knobs but have no runtime
      consumer, so G2 builds the application layer
      ([`WI-G2-01`](agent/cards/WI-G2-01-audit-render-state-knobs.md),
      [`WI-G2-02`](agent/cards/WI-G2-02-graphics-settings-type-and-presets.md),
      [`WI-G2-03`](agent/cards/WI-G2-03-wire-settings-into-render-construction.md)).
  - AC: preset round trips preserve validated settings; runtime switching rebuilds affected
    resources safely; requested and effective capability-fallback values are reported; and
    `Legacy` reproduces the pre-track captured frame on the reference host.
- [x] G3 Visual regression harness (golden-frame comparison per preset over a fixed scene set,
      with sharpness/edge-aliasing metrics; extends `FrameCapturer` + the TestHarness `ScenarioCapture` runner)
      ([`WI-G3-01`](agent/cards/WI-G3-01-audit-frame-capture-paths.md),
      [`WI-G3-02`](agent/cards/WI-G3-02-image-quality-metrics.md),
      [`WI-G3-03`](agent/cards/WI-G3-03-golden-frame-comparison.md),
      [`WI-G3-04`](agent/cards/WI-G3-04-golden-store-and-gate.md)).
  - AC: the TestHarness `VisualRegression` built-in scenario captures the fixed scene set headlessly
    under both presets, byte-identically across two runs on the reference host; a perturbed build
    exits 2 against the committed goldens; goldens change only through
    `TESTHARNESS_VR_UPDATE_GOLDENS=1`; and in every scene's designated silhouette regions the 2x
    supersampled reference scores lower on edge aliasing than the native capture.
- [x] G4 Multisample antialiasing (sample count through `GameOutputDescription`, intermediate
      colour/depth targets and every pipeline description, plus a resolve before single-sample 2D)
      ([`WI-G4-01`](agent/cards/WI-G4-01-inventory-sample-count-sites.md),
      [`WI-G4-02`](agent/cards/WI-G4-02-thread-sample-count-and-resolve.md)).
  - AC: supported 1x/2x/4x/8x modes render without attachment mismatch across 3D, terrain, water,
    roads and particles; 4x improves G3 silhouette aliasing against 1x; 2D stays single-sample;
    resize/preset switches release obsolete resources; unsupported requests report their fallback.
- [x] G5 Cutout & alpha-test clarity (alpha-to-coverage under MSAA, coverage-preserving mips for
      alpha-tested textures, baked per-mip alpha rescale, retaining the fixed `0x60` cutoff in
      `Common.h`) ([`WI-G5-01`](agent/cards/WI-G5-01-audit-alpha-test-materials.md),
      [`WI-G5-02`](agent/cards/WI-G5-02-alpha-to-coverage-and-cutout-mips.md)).
  - AC: synthetic cutout mips preserve threshold coverage within the G5 tolerance; foliage/fence
    silhouette captures improve against the supersampled reference; alpha-to-coverage requires
    effective MSAA greater than 1; blended/opaque textures remain unchanged; `Legacy` matches its golden.
- [x] G6 Gamma-correct rendering (sRGB colour-map views, linear lighting, one sRGB output transfer,
      linear-space mip filtering in `MipmapHelper`, ordered dither on the 8-bit output)
      ([`WI-G6-01`](agent/cards/WI-G6-01-audit-colour-space-handling.md),
      [`WI-G6-02`](agent/cards/WI-G6-02-linear-space-shading-and-dither.md)).
  - AC: black/white colour mips encode approximately 188 while data mips remain 128; role tests
    keep normals/masks/alpha linear; output has exactly one transfer; ordered dither is deterministic
    and zero-mean; gradient captures meet the G6 banding check; `Legacy` matches its golden.
- [ ] [WI-G6-03](agent/cards/WI-G6-03-clamp-particle-opacity.md): shared particle-opacity clamp ([corrective plan](plans/macos-battle-artifacts.md)).
  - AC: GPU opacity-boundary tests and late-life Metal captures under both presets show normal smoke without black/neon corruption; additive RGB, simulation state, and RNG consumption remain unchanged.
- [x] G7 Texture filtering & mip quality (configurable anisotropy up to 16x, mip LOD bias,
      Enhanced offline-prefiltered terrain data, preserved original fallback and Legacy generation)
      ([`WI-G7-01`](agent/cards/WI-G7-01-audit-samplers-and-mip-paths.md),
      [`WI-G7-02`](agent/cards/WI-G7-02-configurable-anisotropy-and-mip-bias.md)).
  - AC: requested anisotropy 1/2/4/8/16 and finite bias in [-1,1] validate and report effective
    values; resource sets observe sampler changes; grazing-angle and fixed-motion captures
    meet the G7 sharpness/temporal checks; terrain seams stay absent and `Legacy` matches its golden.
    Motion evidence: [terrain zoom proof](implementation/track-g-g7-texture-filtering.md#temporal-validation-follow-up).
- [x] G8 Shadow quality tier (four cascades at 2048² by default under `Enhanced`, a receiver-side
      slope-scaled term added to the constant bias; the existing normal offset is kept)
      ([`WI-G8-01`](agent/cards/WI-G8-01-audit-shadow-settings-flow.md),
      [`WI-G8-02`](agent/cards/WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md)).
  - AC: an `Enhanced` capture of the G3 `shadow-casters` scene at the default zoom shows no acne,
    no visible cascade seam and no detached contact shadows, with `VisualizeCascades` showing four
    cascades; the `Legacy` capture matches its golden byte for byte; `ShadowConstantsPS` stays
    240 bytes.
- [ ] G9 Resolution-independent 2D & UI (native-resolution 2D, proportional pixel-space layout
      from G1, role-aware WND/APT upscaling)
      ([`WI-G9-01`](agent/cards/WI-G9-01-audit-2d-ui-resolution-path.md),
      [`WI-G9-02`](agent/cards/WI-G9-02-native-resolution-2d-layer.md)).
  - AC: native 1x/2x captures use pixel-sized fonts with no duplicate DPI transform; proportional
    layout and hit rectangles remain unchanged; `Enhanced` role-aware sampling avoids atlas
    bleed and preserves pixel-art roles; `Legacy` matches its golden.
  - [x] G9-3 Pre-rendered HUD replacement: replace the control bar's 800x600 bitmaps with
        dynamically drawn equivalents (vector/procedural panels, buttons, and frames; live text)
        behind the `Enhanced` preset, `Legacy` keeps the original art
        ([`WI-G9-03`](agent/cards/WI-G9-03-dynamic-hud-chrome.md)).
    - AC: under `Enhanced` the control-bar chrome contains no legacy panel/button/frame bitmap
      draws (portraits, icons and minimap remain textures); captured frames are sharp
      at 4x native with no upscaled-art blur, and `Legacy` is pixel-identical to the pre-rework
      capture.
- [x] G10 Realistic proportions (optional scale-correct presentation: reference-scale
      documentation plus per-definition visual model-scale overrides for the undersized aircraft
      class, behind the G2 settings layer; `Geometry` collision and `AttackRange` gameplay data
      untouched).
  - [x] G10-1 Proportion audit and reference scale: measure per-class model bounds (W3D mesh
        bounds, FX meshes excluded) against INI `Geometry` and real-world counterparts; check in
        the deviation table and pin the world-units-per-meter reference (measured basis: ≈ 5
        units/m; aircraft undersized ≈ 2-2.5x, e.g. MiG ≈ 6.6 m in-game vs ≈ 17.3 m real)
        ([`WI-G10-01`](agent/cards/WI-G10-01-audit-proportions-and-reference-scale.md)).
    - AC: a checked-in table covers a representative set spanning infantry, ground vehicles,
      buildings, and aircraft, and a data test re-derives its numbers from the installed
      INI/W3D assets so a changed asset fails the test.
  - [x] G10-2 Presentation-only scale overrides: per-definition visual scale multiplier keyed to
        the proportions option, correcting the aircraft class
        ([`WI-G10-02`](agent/cards/WI-G10-02-presentation-scale-overrides.md)).
    - AC: with the option enabled only aircraft visuals change size; existing P0/P9 scenario
      assertions pass identically with the option on and off; presentation rings/culling match
      visuals while shared world/bone transforms and selection-query bounds remain unchanged.
  - [x] G10-3 Preset wiring and golden frames: register the proportions option in the G2 settings
        layer and add proportion-mode captures to the G3 golden set
        ([`WI-G10-03`](agent/cards/WI-G10-03-preset-wiring-and-golden-frames.md)).
    - AC: golden frames captured under the proportions mode catch an injected rescale regression,
      both presets default the option off, and disabled `Legacy` output matches the pre-phase capture.
- [ ] G11 Terrain impact decals (persistent scorch marks from FX, logic modules and map objects,
      drawn as a terrain overlay; design in the
      [deep dive](deep-dives/g11-terrain-impact-decals.md)).
  - [x] G11-1 Decal store and scorch FX execution
        ([`WI-G11-01`](agent/cards/WI-G11-01-terrain-decal-store-and-scorch-fx.md)).
    - AC: `TerrainScorch` nuggets add decals using client random only; the store applies the
      original cap and duplicate rule; every installed Generals and Zero Hour nugget parses.
  - [ ] G11-2 Overlay mesh and render pass
        ([`WI-G11-02`](agent/cards/WI-G11-02-terrain-decal-mesh-and-pass.md)).
    - AC: mesher matches the original geometry and UVs in tests; scorches follow slopes and sit
      under roads; existing goldens unchanged with no decals.
  - [x] G11-3 Logic and map scorch producers
        ([`WI-G11-03`](agent/cards/WI-G11-03-logic-and-map-scorch-producers.md)).
    - AC: implemented producers add original-size scorches, unimplemented ones are recorded;
      map-placed scorches load once.
  - [ ] G11-4 `Enhanced` lighting, limit and golden frames
        ([`WI-G11-04`](agent/cards/WI-G11-04-enhanced-decals-and-golden-frames.md)).
    - AC: `Enhanced` scorches are terrain-lit; `faithful` and `enhanced` goldens recorded and an
      injected regression fails.

## Track H: Map & Unit Storage Formats

See [`track-h-storage-formats.md`](track-h-storage-formats.md). H2a depends on P9 and C4d;
the storage phases are gated on Track P (P7).
Shared unit-content importing plus on-disk map and object/unit-definition storage formats, distinct
from B3 (runtime unit storage) and B4a-5 (chunked terrain runtime storage), which this track
coordinates with rather than duplicates.

- [ ] H1 Format requirements & current-state audit (`.map`/`MapFile` and INI/`ObjectDefinition`
  limitations; format-design Decisions recorded in `open-questions.md`).
  - AC: a written audit cites every current-state limitation by file:line, and each format-design
    question (read/write scope, human-editable vs. binary, shared vs. separate containers) has a
    recorded Decision.
- [ ] H1-1 Legacy map storage audit (`WI-H1-01`).
  - AC: the audit cites each `MapFile` limitation by file and line.
- [ ] H1-2 Confirm settled H3 format choices (`WI-H1-02`).
  - AC: the audit confirms or revises the directory package with raw little-endian chunks.
- [ ] H2 Format-agnostic map interchange abstraction, mirroring the C1 pattern.
  - AC: the legacy `.map` reader loads through the map-format seam with no behavior change;
    placed objects retain references to unit definitions owned by H2a.
- [ ] H2-1 Map interchange design for the package format (`WI-H2-01`).
  - AC: the design names interchange types without `MapFile` dependencies.

### H2a: Unit-content import layer

Design and card index: [unit-content import](deep-dives/h2a-unit-content-import.md).
Runs before M2 without waiting for H1/H2/H3. Each item corresponds to one card.

- [x] H2a-1 Focused loading audit (`WI-H2a-01`).
  - AC: file:line evidence covers INI defaults/inheritance, dependency loading, scoped identity, module construction, and P9 entry points.
- [x] H2a-2 Contract and architecture design (`WI-H2a-02`).
  - AC: reviewed schema, capability matrix, reference roster, and grounded class/sequence diagrams reconcile all Draft cards.
- [x] H2a-3 Engine-owned interchange contracts (`WI-H2a-03`).
  - AC: owned values cover gameplay and visual dependencies without source parser or live runtime objects; mutation and identity tests pass.
- [x] H2a-4 Inheritance resolution (`WI-H2a-04`).
  - AC: omitted versus explicit values and list replacement/merge follow the contract; missing parents and cycles report their source chain.
- [x] H2a-5 Dependency resolution (`WI-H2a-05`).
  - AC: typed cross-file references resolve within declared namespaces; missing/wrong-kind/ambiguous references block publication.
- [x] H2a-6 Capability validation and diagnostics (`WI-H2a-06`).
  - AC: unsupported gameplay blocks import; ignorable metadata is reported separately; failures identify source, record, field, and stage.
- [x] H2a-7 Body and movement materialization (`WI-H2a-07`).
  - AC: health, geometry, locomotor, and movement values produce native modules; invalid numbers fail before registration.
- [x] H2a-8 Weapon and armor materialization (`WI-H2a-08`).
  - AC: range, reload, damage, targeting, projectile references, and armor resolve from imported values without balancing defaults.
- [x] H2a-9 Ability materialization (`WI-H2a-09`).
  - AC: selected supported abilities create native modules and execute; unsupported abilities cannot silently disappear.
- [x] H2a-10 Production and upgrade materialization (`WI-H2a-10`).
  - AC: costs, build time, prerequisites, production links, and selected upgrades retain their imported semantics; unsupported economy mechanics block import.
- [x] H2a-11 Visual-state binding (`WI-H2a-11`).
  - AC: model and idle/move/attack/death references use Track C assets and native draw behavior; required missing bindings fail explicitly.
- [x] H2a-12 Scoped content publication (`WI-H2a-12`).
  - AC: a validated dependency closure publishes atomically; collision/failure/unload leaves built-in content and cached references intact.
- [x] H2a-13 Generals reference adapter (`WI-H2a-13`).
  - AC: Generals unit loading uses the shared path with legacy names, defaults, inheritance, module coverage, and override behavior preserved.
- [x] H2a-14 Generals behavior parity (`WI-H2a-14`).
  - AC: the audited reference roster passes existing archetype and P9 contracts, including abilities, production, and upgrades where supported today.
- [x] H2a-15 Bounded EE2 DDF reader (`WI-H2a-15`).
  - AC: selected records and their full inherited dependency closure parse from the operator install; syntax limits and unsupported fields are diagnosed.
- [x] H2a-16 EE2 source adapter (`WI-H2a-16`).
  - AC: one audited ground combat unit and dependencies emit shared interchange with source stats, explicit unit conversion, and DDF visual states.
- [x] H2a-17 Multi-source P9 proof and M2 handoff (`WI-H2a-17`).
  - AC: the real EE2 unit coexists with Generals and spawns/moves/attacks/dies; evidence records source and runtime stats, failures, and M2 expansion boundaries.
- [x] H2a-18 EE2 ground-unit selection probe (`WI-H2a-18`).
  - AC: one EE2 ground combat unit with resolvable stats and four importable visual states is selected, or the missing Track C capability is recorded.
- [ ] H2a-19 Staged Generals INI registration (`WI-H2a-19`).
  - AC: unit blocks reach the store only through the publisher; Generals and Zero Hour boot with zero import diagnostics and unchanged parity/P9 results.

### H2b: Metric core units

Design and card index: [metric core units](deep-dives/h2b-metric-core.md). Starts after the
complete H2a group merges. Each item corresponds to one card.

- [ ] H2b-1 Merged H2a dimensional audit (`WI-H2b-01`).
  - AC: file:line inventory of every length boundary in the merged H2a code, with conversion owner, inverse writer, and test per field.
- [ ] H2b-2 Cutover design and grounded diagrams (`WI-H2b-02`).
  - AC: every field has one normalization owner; compatibility matrix and grounded diagrams exist; source cards have exact scopes.
- [ ] H2b-3 Conversion helpers (`WI-H2b-03`).
  - AC: one helper owns `0.3048` and every dimensional variant, with round-trip tests.
- [ ] H2b-4 H2a quantities and materializers (`WI-H2b-04`).
  - AC: materialized distances and rates are metric; H2a and native INI paths agree; no double conversion.
- [ ] H2b-5 Gameplay INI lengths and rates (`WI-H2b-05`).
  - AC: inventoried gameplay fields convert once at parse time with converted-expectation tests.
- [ ] H2b-6 Presentation INI and globals (`WI-H2b-06`).
  - AC: `GameData` globals and presentation lengths convert once; gravity equals the legacy value times `0.3048`.
- [ ] H2b-7 Terrain spacing and elevation (`WI-H2b-07`).
  - AC: a 10-unit cell is 3.048 m; raw heightmap data round-trips unchanged.
- [ ] H2b-8 Map placement and WorldBuilder (`WI-H2b-08`).
  - AC: placed objects, waypoints, and triggers convert on load and invert on save byte-identically.
- [ ] H2b-9 W3D geometry and animation (`WI-H2b-09`).
  - AC: W3D bounds equal legacy bounds times `0.3048`; bind pose and animation stay aligned.
- [ ] H2b-10 Metric-native importers (`WI-H2b-10`).
  - AC: glTF and NIF meters import as meters; no importer contains `3.28084`.
- [ ] H2b-11 Physics and locomotion (`WI-H2b-11`).
  - AC: audited length literals derive from the helper; trajectories match scaled legacy expectations.
- [ ] H2b-12 Navigation, partitioning, scripts (`WI-H2b-12`).
  - AC: pathfind and terrain grids stay aligned; script distances convert at load.
- [ ] H2b-13 Cameras, rendering, audio (`WI-H2b-13`).
  - AC: camera framing, render cutoffs, and attenuation match pre-cutover captures.
- [ ] H2b-14 Orders, network, replays (`WI-H2b-14`).
  - AC: original `.rep` files play back; legacy replay writes round-trip byte-identically.
- [ ] H2b-15 Saves, checkpoints, recordings (`WI-H2b-15`).
  - AC: formats carry a unit version; no old file is silently read as meters.
- [ ] H2b-16 Tools, scenarios, docs (`WI-H2b-16`).
  - AC: tools label meters; ReplaySketch map and model panels match pre-cutover framing; developer guide has a units section.
- [ ] H2b-17 Boundary conformance suite (`WI-H2b-17`).
  - AC: every inventory row has a conformance case or exemption; stray scale literals fail a scan test.
- [ ] H2b-18 Real-content release proof (`WI-H2b-18`).
  - AC: Generals and EE2 P9 proofs match final-H2a behavior scaled by `0.3048`; staging-to-main PR open.

### Remaining storage phases

- [ ] H3 New map storage format (versioned container; terrain section reserved for B4a-5's
  chunked heightmap representation).
  - AC: a map authored in the new format round-trips (write, reload) with terrain, blend, and
    placed-object data intact.
- [ ] H3-1 Map package manifest schema (`WI-H3-01`).
  - AC: manifests validate versions, layers, and chunk layout.
- [ ] H3-2 Chunk layer reader and writer (`WI-H3-02`).
  - AC: raw little-endian chunks round-trip byte-identically.
- [ ] H3-3 Legacy-to-package importer hooks (`WI-H3-03`).
  - AC: a legacy map imports into a package through H2.
- [ ] H3-4 Map package round-trip tests (`WI-H3-04`).
  - AC: terrain, blend, and object data survive write and reload.
- [ ] H4 New object/unit-definition storage adapter over H2a (weapons/upgrades/
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

## Track R: Real-World Terrain

See [`track-r-real-world-terrain.md`](track-r-real-world-terrain.md). Formerly Track B phase B4b;
item labels and card IDs keep the B4b prefix. Design:
[deep dive](deep-dives/b4b-geospatial-map-generation.md); cards:
[plan](implementation/track-b-b4b-geospatial.md). Imports operator-supplied Google Earth KML/KMZ
plus georeferenced DEM and imagery rasters without direct Google service access; Google basemap
imagery and terrain are not supported inputs.

### R1: Geospatial import core
- [ ] B4b-1 Input contract: `Panopticon.Geospatial`, diagnostics, limits, manifest
      (`WI-B4b-01`, `WI-B4b-02`).
  - AC: the project references no engine assembly; malformed manifests fail with typed codes.
- [ ] B4b-2 Geospatial interchange values and interfaces (`WI-B4b-03`).
  - AC: interchange types reference no `MapFile`, LibTiff, or ImageSharp type.
- [ ] B4b-3 KML, KMZ, and GeoTIFF readers with synthetic fixtures (`WI-B4b-07`..`10`).
  - AC: unsafe archives and DTDs are rejected before expansion; GeoTIFF fixtures reproduce their
    georeferencing, including south-up and pixel-is-point cases.
- [ ] B4b-4 Transverse Mercator, CRS transformer, local map grid (`WI-B4b-04`..`06`).
  - AC: round trips within 1 mm; a custom-meridian grid spans UTM 31/32 and scale error over 0.1%
    fails.
- [ ] B4b-5 Sampling, terrain, imagery, and material chunks (`WI-B4b-11`..`14`).
  - AC: shared chunk edges are bit-identical; repeated runs produce identical chunk hashes.
- [ ] B4b-6 Feature clipping, mapping, and masks (`WI-B4b-15`..`17`).
  - AC: clipped geometry matches exact boundary intersections; slope and water masks follow the
    manifest thresholds.

### R2: Standalone payload and dev preview
- [ ] B4b-6a Standalone payload, GeoImport CLI, control points (`WI-B4b-18`..`20`).
  - AC: a synthetic import writes the full payload transactionally and control points pass within
    tolerance.
- [ ] B4b-6b Dev preview to a legacy `MapFile` (`WI-B4b-21`..`24`).
  - AC: a generated synthetic map loads in Generals and runs 300 logic frames.

### R3: Map package output (depends on B4a-5, H3)
- [ ] B4b-7 H3 package output through the H2 interchange (blocked on H2/H3, B4a-5).
  - AC: TBD by `WI-H2-01`.

### R4: WorldBuilder import workflow (depends on H6)
- [ ] B4b-8 WorldBuilder import workflow (blocked on H6).
  - AC: import is one transaction; cancellation leaves the session unchanged.

### R5: Validation and scale proof
- [ ] B4b-9 Validation and scale proof (blocked on B4b-7/8).
  - AC: one operator-supplied area passes control points, round-trips through WorldBuilder, and
    plays; B1 records import time and peak memory.

## Track W: Western Front Simulation

See [`track-w-western-front.md`](track-w-western-front.md). W0 depends on A4-1 and A4-4; later
phases depend on the Track A/B phases listed in the track doc.

### W0: Charter fixture ([deep dive](deep-dives/w0-charter-fixture.md))
- [ ] W0-1 Project, `InternalsVisibleTo`, stable keys, enums (`WI-W0-01`).
  - AC: key helpers round-trip every §3 format and reject malformed keys.
- [ ] W0-2 `WesternFrontSchema` version 1 (`WI-W0-02`).
  - AC: the schema validates; every conserved family has a holding and a sink.
- [ ] W0-3 Synthetic topology (`WI-W0-03`).
  - AC: counts, ownership, link owner regions, and active-region cell listing match §5.
- [ ] W0-4 Theater clock and multi-rate scheduler (`WI-W0-04`).
  - AC: cadence, drift-free tactical time, and boundary-only mode changes hold without a host.
- [ ] W0-5 State modules and formation registry (`WI-W0-05`).
  - AC: no W0 module updates per frame; keys map to object ids both ways.
- [ ] W0-6 Synthetic scenario (`WI-W0-06`).
  - AC: formation counts and the promoted platoon's split match §6.
- [ ] W0-7 Domain adapter (`WI-W0-07`).
  - AC: `SimulationHost` runs the domain unchanged; captured records pass schema checks.
- [ ] W0-8 Conservation ledger (`WI-W0-08`).
  - AC: every ledger operation leaves residuals empty; failed operations change nothing.
- [ ] W0-9 Charter fixture evidence (`WI-W0-09`).
  - AC: every [§10](deep-dives/w0-charter-fixture.md#10-evidence-phase-milestone) assertion passes.

### W1: Aggregate sector simulation ([deep dive](deep-dives/w1-aggregate-sector.md))
- [ ] W1-1 Parameters, schema version 2, seeded streams, Poisson sampler (`WI-W1-01`..`03`).
  - AC: parameters load with checked units; v2 records reconstruct; streams repeat per key.
- [ ] W1-2 Message queue, comm links, communication system (`WI-W1-04`).
  - AC: messages arrive after per-link latency, fall back on cut links, and record loss.
- [ ] W1-3 Intelligence pictures, echelon views, reactive command policies (`WI-W1-05`..`06`).
  - AC: policies decide from `EchelonView` force ratios only.
- [ ] W1-4 Movement over the road graph with stacking (`WI-W1-07`).
  - AC: formations move at parameterized rates within stacking limits.
- [ ] W1-5 Logistics allocation, shipments, interdiction, consumption (`WI-W1-08`..`09`).
  - AC: allocations are exact integers; every moved or consumed unit is ledger-recorded.
- [ ] W1-6 Platoon-level combat (both variants), morale, terrain, campaign systems (`WI-W1-10`..`13`).
  - AC: both variants conserve exactly; 1 h campaign systems update weather, production, entrenchment.
- [ ] W1-7 Offensive scenario, evidence tests, 64-seed A2 report (`WI-W1-14`..`15`).
  - AC: the 10080-frame run reconstructs, repeats per seed, and reports distributions.

### W2: Fidelity promotion and reconciliation ([deep dive](deep-dives/w2-fidelity-promotion.md))
- [ ] W2-1 Activation policy interface, references, dwell (`WI-W2-01`).
  - AC: every contested region promotes; transitions respect the 3-tick dwell.
- [ ] W2-2 Transition records and schema additions (`WI-W2-02`).
  - AC: promotion and demotion reconstruct without input replay.
- [ ] W2-3 Tactical template map (`WI-W2-03`).
  - AC: blue and red placeholder definitions resolve and validate.
- [ ] W2-4 Soldier placement (`WI-W2-04`).
  - AC: seeded posture and trench placement stays inside active cells.
- [ ] W2-5 Promotion ledger split (`WI-W2-05`).
  - AC: platoon personnel, MG, and SAA split onto soldiers exactly.
- [ ] W2-6 Casualty classification (`WI-W2-06`).
  - AC: each soldier death sinks one killed, wounded, or missing casualty.
- [ ] W2-7 Soldier ammunition module (`WI-W2-07`).
  - AC: shots decrement SAA; zero rounds block firing.
- [ ] W2-8 MG teams (`WI-W2-08`).
  - AC: MG carriers preserve and reconcile MG counts.
- [ ] W2-9 Artillery impacts in active areas (`WI-W2-09`).
  - AC: bombard orders consume rounds and apply `DamageInfo` impacts.
- [ ] W2-10 Formation objectives to orders through A4-3 (`WI-W2-10`).
  - AC: orders flow only through A4-3 and `SimulationHost.InjectOrders`.
- [ ] W2-11 Demotion, reconciliation, terrain fold-in (`WI-W2-11`).
  - AC: survivors, SAA, MG, and terrain fold back with empty residuals.
- [ ] W2-12 Scheduler mode switching (`WI-W2-12`).
  - AC: mode changes happen only on operational boundaries.
- [ ] W2-13 Whole-sector all-tactical reference run (`WI-W2-13`).
  - AC: the whole sector materializes tactically from frame zero.
- [ ] W2-14 Aggregate error report (`WI-W2-14`).
  - AC: 64 seeds per arm compare aggregate and mixed runs against all-tactical.

### W3a: Sedan 1870 ([deep dive](deep-dives/w3a-sedan-1870.md))
- [ ] W3a-1 Scale probe (`WI-W3a-01`).
  - AC: per-frame cost and peak memory at 320,000 soldier objects with long-range acquisition, and a projected reference wall-clock.
- [ ] W3a-2 Operator geospatial inputs (`WI-W3a-02`).
  - AC: manifest names DEM, vector, and digitized 1870 sources, CRS, datum offset, and uncommitted paths.
- [ ] W3a-3 Sedan payload validation (`WI-W3a-03`).
  - AC: payload check rejects wrong CRS, scale, datum, bounds, or missing W3a layers.
- [ ] W3a-4 Topology builder contract (`WI-W3a-04`).
  - AC: topology derives sectors, regions, cells, nodes, and links with source refs.
- [ ] W3a-5 Region and cell grid (`WI-W3a-05`).
  - AC: 1000 m regions and 125 m cells cover the projected extent.
- [ ] W3a-6 Vector features to topology (`WI-W3a-06`).
  - AC: links and annotations carry source feature ids; W3a layers apply their topology effects.
- [ ] W3a-7 Neutral territory (`WI-W3a-07`).
  - AC: German entry is blocked; French entry moves holdings to `interned`.
- [ ] W3a-8 Order-of-battle schema (`WI-W3a-08`).
  - AC: schema validates keys, echelons, parents, strengths, templates, and provenance.
- [ ] W3a-9 French order of battle (`WI-W3a-09`).
  - AC: every French battalion, battery, and squadron has a source; totals match the deep dive.
- [ ] W3a-10 German order of battle (`WI-W3a-10`).
  - AC: every German battalion, battery, and squadron has a source; totals match the deep dive.
- [ ] W3a-11 Organization template expansion (`WI-W3a-11`).
  - AC: expansion to the platoon floor is deterministic with stable keys.
- [ ] W3a-12 Period ledger families (`WI-W3a-12`).
  - AC: `horse`, `mitrailleuse`, `captured`, and `interned` conserve exactly.
- [ ] W3a-13 Period weapon parameters (`WI-W3a-13`).
  - AC: every value has a unit and a source.
- [ ] W3a-14 Period content definitions (`WI-W3a-14`).
  - AC: period objects and weapons load in a scope above Generals.
- [ ] W3a-15 Weapon hit resolution (`WI-W3a-15`).
  - AC: the null resolver keeps RTS behavior identical; period hits follow the table per seed.
- [ ] W3a-16 Shells and fuzes (`WI-W3a-16`).
  - AC: percussion, shrapnel, case, and La Hitte presets behave as designed.
- [ ] W3a-17 Line of sight (`WI-W3a-17`).
  - AC: fire and detection require line of sight over terrain and feature occluders.
- [ ] W3a-18 Environment schedule (`WI-W3a-18`).
  - AC: light, fog, and rain modify detection, fire, and movement per schedule.
- [ ] W3a-19 Tactical artillery (`WI-W3a-19`).
  - AC: crew minimum, limbering, ammunition flow, and capture transfer hold.
- [ ] W3a-20 Cavalry troopers (`WI-W3a-20`).
  - AC: horse holdings, dismounting, and charges behave as designed.
- [ ] W3a-21 Courier and telegraph links (`WI-W3a-21`).
  - AC: latency and disruption follow the comm-type table; telephone and wireless are disabled.
- [ ] W3a-22 Orders timeline dataset (`WI-W3a-22`).
  - AC: every order and command change has a source and resolvable keys.
- [ ] W3a-23 Order applicability (`WI-W3a-23`).
  - AC: orders issue from simulated headquarters; applicability and arrival-offset metrics record.
- [ ] W3a-24 Local surrender (`WI-W3a-24`).
  - AC: encircled, broken platoons move to `captured`; equipment transfers.
- [ ] W3a-25 Army capitulation (`WI-W3a-25`).
  - AC: capitulation sets `Decided`/`Surrendered`; window end without it records `InProgress`.
- [ ] W3a-26 Sedan scenario assembly (`WI-W3a-26`).
  - AC: the 31 August 00:00 state places every formation in its assembly area.
- [ ] W3a-27 A5 geospatial rendering (`WI-W3a-27`).
  - AC: A5 shows regions, cells, features, nodes, links, and source ids.
- [ ] W3a-28 All-tactical reference run (`WI-W3a-28`).
  - AC: 4 reference seeds complete the 60 h window in `Operational` recording mode; sampled
    tactical intervals rebuild from checkpoints.
- [ ] W3a-29 Aggregate and mixed runs (`WI-W3a-29`).
  - AC: 64 seeds per arm report error against the reference.
- [ ] W3a-30 Sedan evidence report (`WI-W3a-30`).
  - AC: the report records evidence, the historical divergence table, and provenance.
- [ ] W3a-31 Operational recording adapter (`WI-W3a-31`).
  - AC: the Western Front domain records soldiers only through `rollup` records and checkpoints;
    a reconstructed tactical interval matches a `Full` recording of the same seed.

### W3b: Somme sector ([deep dive](deep-dives/w3b-somme-sector.md))
- [ ] W3b-1 Operator geospatial inputs (`WI-W3b-01`).
  - AC: manifest template names sources, CRS, scale, datum, and uncommitted paths.
- [ ] W3b-2 Somme payload validation (`WI-W3b-02`).
  - AC: payload check rejects wrong CRS, scale, datum, bounds, or missing layers.
- [ ] W3b-3 Synthetic forces on geography (`WI-W3b-03`).
  - AC: the scenario starts with valid depots, fronts, and links.
- [ ] W3b-4 W1 evidence on the sector (`WI-W3b-04`).
  - AC: W1 evidence passes on the generated topology.
- [ ] W3b-5 W2 evidence on the sector (`WI-W3b-05`).
  - AC: W2 evidence passes on the generated topology.
- [ ] W3b-6 Somme sector report (`WI-W3b-06`).
  - AC: the report records control points, topology counts, evidence, provenance.

### W4: Command and logistics experiment ([deep dive](deep-dives/w4-command-logistics-experiment.md))
- [ ] W4-1 Matrix axis contract (`WI-W4-01`).
  - AC: A5 expands exactly 216 cells with 64 seeds.
- [ ] W4-2 Parameter binding (`WI-W4-02`).
  - AC: axes change only intended parameters and record values.
- [ ] W4-3 Information audit (`WI-W4-03`).
  - AC: a read outside `EchelonView` fails with provenance.
- [ ] W4-4 Cell result schema (`WI-W4-04`).
  - AC: results key outcomes, losses, supply, latency, activation, cost.
- [ ] W4-5 Sensitivity report (`WI-W4-05`).
  - AC: the report includes confidence intervals and a sensitivity ranking.
- [ ] W4-6 A5 comparison workspace (`WI-W4-06`).
  - AC: A5 opens, filters, compares, and scrubs W4 run sets.

### W5: Distributed regional simulation ([deep dive](deep-dives/w5-distributed-regional-simulation.md))
- [ ] W5-1 Front-strip partition (`WI-W5-01`).
  - AC: contested pairs stay inside one shard.
- [ ] W5-2 Shard slice state (`WI-W5-02`).
  - AC: owned and halo records separate by stable keys.
- [ ] W5-3 In-process JSON transport (`WI-W5-03`).
  - AC: boundary messages round-trip deterministically.
- [ ] W5-4 Lockstep coordinator (`WI-W5-04`).
  - AC: barriers match compressed and tactical cadence.
- [ ] W5-5 Cross-boundary handoff (`WI-W5-05`).
  - AC: each entity has exactly one owner through handoff.
- [ ] W5-6 Boundary effects (`WI-W5-06`).
  - AC: each boundary effect is sent, applied, and acknowledged once.
- [ ] W5-7 Sharded versus single-process evidence (`WI-W5-07`).
  - AC: shard invariants hold; outcome distributions compare with the single-process run.
- [ ] W5-8 A5 shard visualization (`WI-W5-08`).
  - AC: A5 shows shard ownership, halo age, handoffs, messages.

### W6: Long-run campaign experiment ([deep dive](deep-dives/w6-long-run-campaign.md))
- [ ] W6-1 Campaign file (`WI-W6-01`).
  - AC: binds sector, parameters, shard layout, dates, seed, checkpoint cadence.
- [ ] W6-2 Historical OOB investigation (`WI-W6-02`).
  - AC: records an accepted source format or keeps the synthetic OOB.
- [ ] W6-3 Long-run clock plan (`WI-W6-03`).
  - AC: 201600 one-minute ticks run without drift.
- [ ] W6-4 Domain snapshot plus host save (`WI-W6-04`).
  - AC: load validates frame, time, active regions, schema, key map.
- [ ] W6-5 Replay indexing (`WI-W6-05`).
  - AC: scrub latency stays bounded across the artifact.
- [ ] W6-6 Long-run metrics (`WI-W6-06`).
  - AC: tick cost, memory, artifact size, save/load, reconstruction are recorded.
- [ ] W6-7 Approximation error report (`WI-W6-07`).
  - AC: every active fidelity policy declares its error against all-tactical.
- [ ] W6-8 Campaign evidence report (`WI-W6-08`).
  - AC: covers provenance, conservation, replay, save/load, scaling.

---

## Track M: Mixed-Roster Mass Battle

See [`track-m-mass-battle.md`](track-m-mass-battle.md). M0 depends on P9 and A1; later phases
depend on the Track A/B/C phases listed in the track doc. Design:
[deep dive](deep-dives/mass-battle-1k.md).

### M0: Battle map
- [x] M0-1 Community map evaluation against the map requirements.
  - AC: each operator-supplied candidate has a record of size, slots, waypoints, passability, load
    result, and provenance; no map file is committed. Records: [`WI-M0-01`](agent/cards/WI-M0-01-map-evaluation.md)
    (official maps.big batch; operator may extend with community candidates).
- [x] M0-3 Deployment zones and reachability check as scenario data.
  - AC: both zones hold 1000 units at twice footprint spacing with airfields behind them; every
    ground locomotor reaches the opposing zone. Card: [`WI-M0-03`](agent/cards/WI-M0-03-deployment-zones.md);
    proof: [`WI-M0-03-deployment-proof.json`](agent/cards/WI-M0-03-deployment-proof.json).
- [~] M0-2 Authored fallback map with B4a: not needed, deepdivide passed every M0-3 check;
  permanently deferred in favor of the post-track programmatic map-development direction
  (see the track doc).

### M1: Capacity probe
- [x] M1-1 Generals-only 1000 vs 1000 scenario: direct-spawn two armies of ground units and
  aircraft, with pre-placed airfields, on the M0 map through the P9 direct-spawn host
  and order a general attack. (`WI-M1-01..03`,
  [plan](implementation/track-m-m1-capacity-probe.md))
  - AC: the scenario runs headless and windowed from one definition; both sides spawn 1000 units;
    aircraft take off, attack, and rearm.
- [x] M1-2 Capacity measurements through B1.
  - AC: B1 records tick cost, frame rate, memory, and path requests per second over the engagement.
- [x] M1-3 First-bottleneck report.
  - AC: the report names the first failing constraint and whether B2/B3 are needed for M5.
    First failing constraint: the partition system span (86-99 percent of every tick at contact,
    crossing the 30 fps budget from frame ~900); B2 and B3 are both required for M5, and the
    density-scaling fix belongs to B4, already an M5 dependency.

### M2: EE2 combat units
- [x] M2-1 Expand H2a's bounded DDF reader to the reference roster and inherited dependencies.
  - AC: resolves `properties` and `abilities` for every file under `db/Units/` against the
    operator-supplied EE2 install; unknown metadata is reported, and unsupported required gameplay
    blocks publication of affected units under H2a's capability policy.
  - Evidence: `Ee2InventoryTests` parses all 117 stock `db/Units` DDFs (438 UnitType, 618 UnitModel,
    184 UpgradeAbilities, 50 UpgradeRefSet; cfg scope 434/616), composes the seven-row reference
    roster from exact CSV row keys with UpgradeAbilities overrides, and reports the uncomposed patch
    layer; `Ee2RowCompositionTests` covers closure isolation and missing missile rejection.
- [x] M2-2 Expand H2a's EE2 adapter for ground combat units and aircraft; use shared materialization.
  - AC: each reference unit gets locomotor, weapon, armor, body, and draw modules from DDF values;
    aircraft get a jet or helicopter locomotor with return-to-airfield rearm.
  - Evidence: `Ee2TypedMaterializationTests` and `Ee2InventoryTests.ReferenceRosterMaterializesWithTypedModules`;
    typed JetAIUpdate construction from authored hover and cruising height, taxiing set beside
    normal (a dedicated source-qualified `.Taxi` locomotor mirroring Generals
    `BasicJetTaxiLocomotor` since WI-M2-06), rps-derived target masks, ordnance as clip size,
    missile speed as travel speed; live rearm proof lands with M2-4. Late-epoch infantry clips
    import since WI-M2-05 (0x25 controller flags accepted; clips targeting absent bones are
    skipped); all seven entries verify clean.
- [x] M2-3 Reference EE2 roster registration through H2a's scoped content publisher.
  - AC: EE2 definitions load beside Generals definitions without overriding any Generals entry.
  - Evidence: `capture-ee2-reference-roster.json` plus `Ee2RosterPublicationTests`; all seven
    reference rows publish beside Generals content with native identities unchanged (the infantry
    row publishes since WI-M2-05).
- [x] M2-4 EE2 units in the P9 validation ground.
  - AC: each reference EE2 unit spawns, moves, attacks, and dies; gaps surface as explicit failures.
  - Evidence: `Ee2ContractPhase` + `ee2-reference-validation.json` + `Ee2ContractValidationTests`.
    All seven published entries pass spawn, displacement, weapon-caused damage, and death (the
    infantry row publishes since WI-M2-05); the helicopter's return and second sortie pass; the
    fixed-wing full rearm cycle (return, refill, second damaging sortie) passes deterministically
    and is asserted after WI-M2-06 (airborne 2D friction plus a dedicated taxi locomotor).

### M3: Mixed roster normalization
- [x] M3-1 Model scale and tiles-to-world factor.
  - AC: EE2 model heights match Generals references within authored bounds; the factor is
    recorded with the manifest.
  - Evidence: [`WI-M3-01`](agent/cards/WI-M3-01-model-scale-and-factor.md); derived pair
    18.2505 units per tile and 0.36501 per model unit recorded in both reference-roster profiles
    and asserted with committed per-class bounds
    ([plan](implementation/track-m-m3-mixed-roster.md#authored-conversion-derivation-wi-m3-01));
    EE2 ground-plane weapon-range semantics normalized (`MeasuresRangeOnGround`).
- [x] M3-2 `rps` counter class and `dbcombat.csv` matrix to Generals armor and damage type table.
  - AC: every reference EE2 unit maps to an armor set and damage type; unmapped classes fail load.
  - Evidence: [`WI-M3-02`](agent/cards/WI-M3-02-rps-damage-and-armor.md); authored table in
    `Ee2ReferenceRps` and both reference-roster profiles (distinct BFME-family types no native
    armor authors), armor multipliers from the stock dbcombat rows, explicit closure-scoped
    failures ([plan](implementation/track-m-m3-mixed-roster.md#damage-class-mapping-wi-m3-02)).
- [x] M3-3 Cross-game points budget, asymmetric roster manifests, and duel matrix.
  - AC: Generals `BuildCost` and EE2 tech-tree resource costs convert to points through recorded
    rates; two different 1000-unit manifests, each with anti-air, total equal points within the
    tolerance; each EE2 versus Generals archetype duel, air included, resolves within authored
    time-to-kill bounds.
  - Evidence: [`WI-M3-03`](agent/cards/WI-M3-03-points-rosters-duels.md); `mass-battle-rosters.json`
    (rates, tolerance, both manifests, 0.056 percent apart), `RosterBudgetEvaluator`,
    `Ee2DuelPhase` + `ee2-duel-report.json` with all seven duels inside the authored bounds
    ([plan](implementation/track-m-m3-mixed-roster.md#points-budget-and-manifests-wi-m3-03)).

### M4: Army-scale AI
- [x] M4-1 Army hierarchy sizing for 1000 units over A4-3, including an air commander.
  (`WI-M4-01`, `WI-M4-03`, `WI-M4-05`,
  [plan](implementation/track-m-m4-army-ai.md))
  - AC: every unit is assigned to a squad and commander at start and after every casualty;
    aircraft fly strike, patrol, or escort missions and return to rearm.
    Full-scale army-AI run: 119/120 squads, unassigned 0 (max 0), commanderless 0 (max 0);
    27/24/24 strike/patrol/escort per side, repeat sortie cycles observed.
- [x] M4-2 Squad-level group movement orders served by B4. (`WI-M4-04`)
  - AC: path requests per tick scale with squads, not units, in the M1 scenario.
    Worst decision tick: 912 requests for 248 squads plus 664 blocked followers; bound held.
- [x] M4-3 Per-tick AI cost budget. (`WI-M4-02`, `WI-M4-06`)
  - AC: B1 shows AI decision cost within the authored budget; no unit idles longer than the stall
    threshold while enemies remain.
    `system.ai.spanMs` amortized median 0.000 (budget 1.7), decision-tick p99 18.3 (budget
    100); max idle 475/347 frames against the 600-frame threshold, 0 stalled at end.

### M5: 1000 vs 1000 battle
- [ ] M5-1 Mixed-roster battle scenario on the M0 map with M3 armies and M4 AI.
  (`WI-M5-01`..`04`, `WI-M5-08`)
  - AC: one scenario definition drives batch, replay, and live modes.
- [ ] M5-2 Seeded batch runs with a mirrored map-bias control. (`WI-M5-05`..`07`)
  - AC: every run reaches a P3 outcome; reports give per-side win rates with confidence intervals
    and kills and losses per unit type; the mirrored control's interval includes 50%.
- [ ] M5-3 Replay of batch runs in A5. (`WI-M5-08`)
  - AC: a finished run scrubs from start to outcome with all 2000 units shown.
- [ ] M5-4 Live windowed spectator view. (`WI-M5-09`, `WI-M5-10`)
  - AC: B1 records a 30 fps median with the full battle in view on the operator's machine.

## Track N: Multi-Unit Pathfinding Performance, Deadlocks, and Layered Navigation

See [`track-n-pathfinding-performance.md`](track-n-pathfinding-performance.md). N0 depends on B1,
B4, and M4. Design: [deep dive](deep-dives/n-multi-unit-pathfinding.md); cards:
[plan](implementation/track-n-pathfinding-performance.md).

### N0: Path cost baseline
- [x] N0-1 `path` span and request counters by origin. (`WI-N0-01`)
  - AC: B1 records carry the `path` span, per-origin request counts, nodes expanded, and failed
    searches; synchronous per-origin counts sum to `PathRequestCount`; waypoints unchanged.
- [x] N0-2 `PathfindingLoad` scenario and baseline report. (`WI-N0-02`)
  - AC: selections of 1 to 1000 units (squad-less and squads), an unreachable case, and the M4
    army-AI probe are recorded; the report names the dominant origin and a verdict per budget.

### N1: Search core efficiency
- [x] N1-1 Allocation-free search scratch. (`WI-N1-01`)
  - AC: the seeded path corpus returns identical waypoints; zero steady-state allocation per
    internal search; before/after per-search time recorded.
- [x] N1-2 Connected-region labels. (`WI-N1-02`)
  - AC: corpus identical including diagonal-only gaps; unreachable requests expand 0 nodes;
    labels update after passability changes; relabel time recorded.
- [x] N1-3 Versioned passability snapshot. (`WI-N1-03`)
  - AC: no `Node` allocation on submit in steady state; async equals synchronous on the corpus;
    B4 anchor before/after recorded.

### N2: Shared group requests
- [x] N2-1 Bounded landing for blocked followers. (`WI-N2-01`)
  - AC: full searches per squad order at or under squads plus fallback followers in the N0
    scenario and the M4 probe; M4 invariants hold.
- [x] N2-2 Group moves for squad-less selections behind `GroupSelectionMoves`. (`WI-N2-02`)
  - AC: option off changes nothing; option on bounds searches by clusters plus fallbacks; the
    1000-unit order-tick span is recorded against 33.3 ms.

### N3: Shared-destination fields
- [x] N3-1 Goal field gate. (`WI-N3-01`)
  - AC: the gate decision records the threshold, measured maximum shared-goal searches, and the
    budget verdict; a `Ready` implementation card exists only if the gate opened.

### N4: Off-thread order paths
- [x] N4-1 Pending path handles on `AIUpdate`. (`WI-N4-01`)
  - AC: assign, replace, reset, and destroyed-requester cases tested; no caller changes.
- [x] N4-2 Async order paths behind `AsyncOrderPaths`. (`WI-N4-02`)
  - AC: option off changes nothing; option on reduces the order-tick logic-thread `path` span to
    submission cost with waits inside the frame bound; M4 invariants hold.

### N5: Scale verification
- [x] N5-1 Verify every Track N budget at scale. (`WI-N5-01`)
  - AC: each budget in the deep dive has a Met or Missed verdict with numbers; M4 invariants and
    engagement checks pass; the default-flip recommendation is recorded.

### N6: Deadlock detection baseline
Design: [deadlock deep dive](deep-dives/n-navigation-deadlocks.md).
- [x] N6-1 Movement progress and stall attribution. (`WI-N6-01`)
  - AC: per-unit stall records with reason and blocker, and wait-for cycles, are reported; movement
    is unchanged; the M4 idle-stall failure units are attributed.
- [x] N6-2 `NavigationDeadlock` scenario and baseline. (`WI-N6-02`)
  - AC: six seeded cases reproduce their mechanisms or report why not; baseline and target
    revisions are recorded.

### N7: Path liveness
- [x] N7-1 Async and squad path liveness. (`WI-N7-01`)
  - AC: failed, thrown, and stale requests settle the unit or fall back once; failed squad leaders
    settle members; `async-no-route` ends with 0 units in `AwaitingPath`.
- [x] N7-2 Offset route validation behind `ValidateOffsetRoutes`. (`WI-N7-02`)
  - AC: option off changes nothing; option on reads 0 `impassableRetracePoints` in every case.

### N8: Blocked-unit resolution
- [x] N8-1 Blocked wait and bounded local repath behind `BlockedUnitResolution`. (`WI-N8-01`)
  - AC: option off changes nothing; local repath is window-bounded; N5 path budgets still met.
- [x] N8-2 Give-way and mutual block yield. (`WI-N8-02`)
  - AC: `head-on-pair` settles with one sidestep; 0 wait-for cycles at case end; no sidestep
    cascade.

### N9: Destination crowding
- [x] N9-1 Destination slots behind `DestinationSlots`. (`WI-N9-01`)
  - AC: option off changes nothing; slots are unique, passable, in-region, deterministic.
- [x] N9-2 Crowd arrival. (`WI-N9-02`)
  - AC: `crowd-goal-100` ends with 0 units holding routes in both slot modes.

### N10: Deadlock verification
- [x] N10-1 Verify every deadlock target. (`WI-N10-01`)
  - AC: each target has a verdict across 3 seeds; M4 stall results recorded per run; default-flip
    recommendation recorded.

### N11: Passage detection and baseline
Design: [passage deep dive](deep-dives/n-confined-passage-queueing.md).
- [x] N11-1 Passage finder and cross-section cache. (`WI-N11-01`)
  - AC: synthetic gaps and corridors return one span with the expected throat and lanes; one-sided
    walls return none; repeated scans hit the cache.
- [x] N11-2 Passage cases and option-off baseline. (`WI-N11-02`)
  - AC: five passage cases detect their gaps and record settle frames, mouth jams, cycles, and
    retrace points; movement unchanged.

### N12: Passage queues
- [ ] N12-1 Passage coordinator, queue order, and lane path behind `PassageQueueing`. (`WI-N12-01`)
  - AC: deterministic ranks, fixed ranks never change, balanced lanes, one-pitch release spacing;
    option off constructs nothing.
- [ ] N12-2 Passage hold, release, and route splice. (`WI-N12-02`)
  - AC: option off changes nothing; open ground opens 0 passages; column and converge cases settle
    with 0 mouth jams and 0 cycles.

### N13: Opposing flow and group entry
- [ ] N13-1 Opposing flow arbitration. (`WI-N13-01`)
  - AC: grants alternate within bounds; single-lane and two-lane opposing cases settle with 0
    cycles.
- [ ] N13-2 Squad and cluster passage entry. (`WI-N13-02`)
  - AC: `passage-squad-12` has 0 offset waypoints on impassable cells inside the passage; blocks
    are never interleaved; M4 squad invariants hold.

### N14: Passage verification
- [ ] N14-1 Verify every passage target. (`WI-N14-01`)
  - AC: each target has a verdict across 3 seeds and three option modes; `passage` span recorded
    against budget; default-flip recommendation recorded.

### N15: Layered navigation contract and baseline
Design: [layered navigation deep dive](deep-dives/n-layered-navigation.md).
- [ ] N15-1 Navigation plan and layer contract. (`WI-N15-01`)
  - AC: every route write goes through `NavigationPlan`; `MovementProgress.Layer` set with stall
    reasons; baselines and saves unchanged.
- [ ] N15-2 Layered cases, layer spans, and option-off baseline. (`WI-N15-02`)
  - AC: five layered cases and per-layer spans report over 3 seeds; targets confirmed or revised
    with numbers; movement unchanged.

### N16: Strategic sector graph
- [ ] N16-1 Sector graph with incremental rebuild. (`WI-N16-01`)
  - AC: synthetic portals and components as expected; edge costs exact; connectivity agrees with
    region labels; a placement rebuilds only touched sectors.
- [ ] N16-2 Strategic planner and route-quality corpus. (`WI-N16-02`)
  - AC: routes exist exactly when regions connect; abstract expansions at most 5% of full-grid
    expansions or the miss recorded; zero steady-state allocation.

### N17: Corridor main path
- [ ] N17-1 Masked corridor search and segment stitching. (`WI-N17-01`)
  - AC: unmasked search unchanged on the N1 corpus; stitched length ratio median at most 1.05 and
    p99 at most 1.20 or the miss recorded.
- [ ] N17-2 Layered unit main path behind `LayeredNavigation`. (`WI-N17-02`)
  - AC: option off unchanged; option on has 0 seam gaps, full-grid expansions per order down at
    least 80%, and replans only affected units after a placement.

### N18: Group local router
- [ ] N18-1 Navigation groups and spine following. (`WI-N18-01`)
  - AC: option off constructs nothing; 0 member waypoints in walls and median slot lag at most 2
    pitches in `layered-cross-map-8x12`; M4 squad invariants hold.
- [ ] N18-2 Width-adaptive formation and passage handoff. (`WI-N18-02`)
  - AC: squad passage and detour cases settle with 0 waypoints in walls and 0 cycles; N14 targets
    still met.
- [ ] N18-3 Crowd routing for large selections. (`WI-N18-03`)
  - AC: crowd cases settle with 0 units holding routes; assignment reproducible; 1000-unit order
    tick at or under N5.

### N19: Unit steering
- [ ] N19-1 Reciprocal velocity-obstacle steering core. (`WI-N19-01`)
  - AC: synthetic head-on, crossing, and wall cases avoid contact; results independent of neighbor
    input order; zero allocation.
- [ ] N19-2 Steering integration with N8 fallback. (`WI-N19-02`)
  - AC: option off unchanged; option on passes head-on and crossing cases with 0 give-way events
    and 0 gate stops; `steer` span median at most 1.0 ms in the M4 probe or the miss recorded.

### N20: Layered navigation verification
- [ ] N20-1 Verify every layered target. (`WI-N20-01`)
  - AC: each target has a verdict across 3 seeds and four option modes; N10 and N14 targets
    re-verified; default-flip recommendations recorded.

### N21: Structure passability coverage
- [x] N21-1 Close the through-structure holes in marking and resolution legs. (`WI-N21-01`)
  - AC: overlapping cells marked, no spliced leg crosses impassable cells, capture shows no
    tank entering a structure, NavigationDeadlock numbers re-recorded.

## Track X: Miscellaneous fixes and tweaks

Open-ended queue: [phase definitions](track-x-fixes-tweaks.md). Entries below cover authored work, not a terminal track milestone.

### X1: Imported-unit hit feedback

Dependencies: H2a, P9; cards consume the already published M2 tank from `WI-M2-03`, independently of full M2 completion. [Design](deep-dives/x1-imported-unit-hit-feedback.md), [cards](implementation/track-x-x1-hit-feedback.md).

- [x] X1-1 Audit source effects and choose an explicit compatibility policy (`WI-X1-01`).
  - AC: exact source assets, target-effect path, scope, missing-reference behavior, and implementation seams are recorded.
- [x] X1-2 Bind opt-in damage effects for imported vehicles (`WI-X1-02`).
  - AC: the selected tank binds its requested effect; unconfigured imports and native content remain unchanged; missing effects fail before publication.
- [x] X1-3 Add policy and damage-lifecycle regression coverage (`WI-X1-03`).
  - AC: binding/publication cases pass and presentation does not change health loss or death outcomes; target placement and throttle have explicit evidence.
- [x] X1-5 Specify directional tank-impact placement and trace Rebel effect playback (`WI-X1-05`).
  - AC: both hit paths are traced; scoped placement/fallback behavior and evidenced playback fixes have bounded implementation cards and grounded diagrams; X1-4 prerequisites are updated.
- [x] X1-6 Select native impact effects for opted-in imported weapons (`WI-X1-06`).
  - AC: the mapped tank weapon plays infantry impacts through `DamageFXOverride`; damage/health/death unchanged; invalid mappings fail before staging.
- [x] X1-7 Place mapped imported-target effects on the attacker-facing bounds (`WI-X1-07`).
  - AC: helper geometry tests for rotated/scaled bounds and fallbacks pass; native and unmapped targets keep origin placement; combat results unchanged.
- [x] X1-4 Produce a readable mixed-source duel capture (`WI-X1-04`).
  - AC: both full units, labels, health reduction, tank impacts on the Rebel-facing surface, and Rebel target impacts are visible; real damage assertions, GIF, and reproduction command accompany the evidence.

### X2: Capacity-probe graphics resource fixes

Dependency: M1. Independent of X1. [Evidence and cards](implementation/track-x-x2-capacity-probe-fixes.md).

- [x] X2-1 Bound temporary graphics-upload resource lifetimes (`WI-X2-01`).
  - AC: repeated uploads have bounded temporary ownership, correct destination contents, safe cleanup, and before/after D3D11 native-memory and frame-time evidence.
- [ ] X2-2 Reduce measured combat FX/model resource recreation (`WI-X2-02`).
  - AC: post-X2-1 stacks identify one recreation owner; compatible resources are reused with bounded retention, reset/disposal coverage, fewer constructions, and preserved visuals.

### X3: Cursor modernization

Dependency: none. [Plan and cards](implementation/track-x-x3-cursor-modernization.md).

- [ ] X3-1 Evaluate open-source cursor replacements and produce comparison examples (`WI-X3-01`).
  - AC: full inventory of the 52 originals; pinned sources with license verdicts; contact sheet and animated GIF in the shared directory; `CursorFile` load-check per candidate; loader assessment naming the smallest high-DPI change; follow-up cards `Draft` with the original set as default.

### X4: ReplaySketch TabControl shell

Dependency: none. [Plan and card](implementation/track-x-x4-replaysketch-tabcontrol.md).

- [ ] X4-1 Switch the ReplaySketch `MainWindow` tab strip to an Avalonia `TabControl` (`WI-X4-01`).
  - AC: no tab buttons, frame toggles, `SwitchTo*`, or `SetTabActive` remain; selection side effects and `REPLAYSKETCH_SCREENSHOT_TAB` work for all five tabs; Model after Harness shows only Model; Skia-drawn tabs keep content and state across repeated switching; versioned captures of every tab are inspected.

### X5: Render culling dispatch

Dependency: M1. [Plan and card](implementation/track-x-x5-render-culling-dispatch.md).

- [ ] X5-1 Measure and right-size parallel culling dispatch in `RenderItemCollection.FilterVisible` (`WI-X5-01`).
  - AC: CPU-sampling profile of the windowed capacity probe attributes culling cost before the change; serial path below a measured threshold and processor-sized chunks above it; serial and parallel visible lists identical in tests; before/after `shadowPass`/`forwardPass` medians with CIs recorded in the plan.

### X6: AIUpdate navigation extraction

Dependency: N11. [Plan and card](implementation/track-x-x6-aiupdate-navigation-extraction.md).

- [x] X6-1 Extract path request, route application, blocked-movement, and give-way logic from `AIUpdate` into `UnitNavigation` (`WI-X6-01`).
  - AC: `AIUpdate` holds no navigation logic and `UnitNavigation` has no `AIUpdate` reference; load/persist order unchanged; `SeededRunTests`, golden baselines, and the seed 1 `NavigationDeadlock` report match pre-refactor output.
