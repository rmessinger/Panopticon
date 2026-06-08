# Implementation Checklist

> Part of the [Large-Scale Simulation Roadmap](README.md). A single, granular, human-facing list
> of the concrete changes to make toward implementation — one flat checklist across the whole
> roadmap, ordered by dependency. This is the "what's left to build" view; the [track docs](README.md#document-map)
> hold the why/design, the [agent-guide](agent-guide/) holds bounded task cards for cheap agents,
> and this file is the running change list a human works down.

How to use & maintain

- Check an item off (`- [x]`) when it lands; keep this in sync with the phase Status in the
  track docs and any matching [agent-guide card](agent-guide/).
- Items are intentionally granular (a concrete code change / area), not strategy. Add finer items
  as a phase gets a deep dive; split anything too big to do in one focused change.
- Order: everything under Track P gates Tracks A/B/C — do P first. Within a track, items are
  roughly top-down by dependency.
- Each group links to its phase for context. Don't duplicate rationale here — link it.
- Acceptance criteria (AC): every item has an `AC:` sub-bullet stating how you know it's done —
  a verifiable check, ideally a [P0 harness](deep-dives/playable-baseline-harness.md) assertion or an
  automated test (`dotnet test src --filter Category!=Interactive`). Where a phase is still too
  ill-defined to pin down, the AC is marked `TBD` with a rough template to fill in once the
  design firms up. Tighten `TBD` ACs into concrete ones as deep dives land. Every item must build
  (`dotnet build src`) and pass formatting (`dotnet format src --verify-no-changes`) — that baseline
  is implied and not repeated per item.

Legend: `[ ]` todo · `[~]` in progress · `[x]` done · `(↗)` needs a design decision / high-effort
model before it can be done · `AC:` acceptance criteria · `TBD` AC not yet definable.

---

## Track P — Playable Baseline (prerequisite)

See [`track-p-playable-baseline.md`](track-p-playable-baseline.md). Bar: a full Generals/ZH
skirmish playable build → fight → win/lose vs. AI.

### P0 — ReplaySketch arbitrary-command harness ([deep dive](deep-dives/playable-baseline-harness.md))
- [x] Replace the closed `ActionType` enum with an open command model that can express any engine
      `OrderType` (`OpenSage.Tools.ReplaySketch.Core/Model`).
  - AC: a scenario can specify a command as `OrderType` + typed args and it exports to the matching
    engine `Order`; a unit test covers ≥1 command per `OrderArgumentType`.
  - Completion: `CommandSpec` model created with polymorphic `CommandArgument` hierarchy (5 types).
    `ActionEntry.Type` is a stored `ActionType?` field (null for open commands); `CommandSpec` holds
    only `OrderType + Arguments`. `ReplayExporter.BuildOrders` dispatches on `ActionType` enum for
    legacy actions. All 14 ReplaySketchTests pass; `alpine-assault-usa-vs-gla.json` loads correctly.
- [ ] Extract an `IPreferencesStore` interface from `MainViewModel` (currently uses `Preferences.Default`
      directly, a MAUI static); inject it so `LoadMapFromFolder`'s unconditional path-persistence is
      unit-testable without a MAUI runtime.
  - AC: `MainViewModel` accepts an `IPreferencesStore` via constructor injection (production code uses
    the `Preferences.Default` implementation; tests supply a `MemoryPreferencesStore`); a unit test
    calls `LoadMapFromFolder` with a valid directory path where map loading will fail (no game files),
    and asserts the preference was saved regardless of map load outcome.
- [ ] Remove hardcoded USA/GLA `KnownDefinitions`; resolve object definitions by name generically
      (`Services/ReplayExporter.cs`).
  - AC: a scenario referencing any object definition by name (incl. a non-USA/GLA faction) exports
    orders with the correct definition hash; no faction names remain hardcoded in `ReplayExporter`;
    test resolves a definition from a non-default faction.
- [ ] Let `ActionEntry`/scenario target any player/owner and any object definition.
  - AC: a scenario with ≥3 players of mixed factions attributes each command to the correct
    `PlayerIndex`/owner; test asserts per-player order attribution.
- [ ] Reference runtime-created objects via implicit auto-handles (keyed to the producing
      command) with opt-in explicit names; reference world/enemy objects via query selectors
      (faction/relationship/definition/proximity), resolved at dispatch.
  - AC: a scenario issues `AttackObject`/`Enter` against an object produced by an earlier command
    without the author writing an id, and another command targets an enemy structure via a selector;
    both resolve in a headless run (state assertion passes).
- [ ] Map the gameplay-relevant `OrderType` set (move, attack, construct, produce, ability) to
      exporter order emission.
  - AC: for each of move/attack/construct/produce/ability, an authored command produces the expected
    `OrderProcessor` effect in a headless run (state assertion passes).
- [ ] Add a validation/assertion layer (expected post-conditions, invariants, outcomes) runnable
      headlessly with a pass/fail result.
  - AC: assertions are authored as JSON predicates (`{path, op, value}`) over a fixed path/operator
    vocabulary spanning ≥1 outcome, ≥1 state, and ≥1 per-tick invariant type, with a code-defined
    (C#) predicate escape hatch; a headless run emits a machine-readable pass/fail report and a
    deliberately-failing assertion is reported with frame + expected/actual.
- [ ] Provide a headless run-and-assert entry point cheap agents can call (scenario/command file
      in → result out).
  - AC: one CLI/library call takes a scenario/command file and returns exit code 0/non-zero + a JSON
    report; documented invocation; runnable headlessly in CI.
- [ ] Tests: author + validate an arbitrary command set end-to-end.
  - AC: an automated test authors a multi-command scenario, runs it headlessly, and asserts
    post-conditions; passes under `dotnet test src --filter Category!=Interactive`.
- [ ] Support arbitrary command volume: programmatic/procedural generation of large order sets
      (not hand-entry) and efficient `.rep` emission, so the harness scales toward sim-scale runs.
      (Full authoring/replay scaling is finalized under A5-5.)
  - AC: a scenario with ≥10k programmatically-generated orders exports and loads within a documented
    time/memory budget; test generates the orders without per-action hand-entry.

### P1 — Skirmish setup & player relationships ([deep dive](deep-dives/skirmish-setup.md))
- [ ] Resolve `PlayerManager.OnNewGame()` `// TODO: Setup player relationships` (allies / enemies /
      neutrals) — `src/OpenSage.Game/Logic/PlayerManager.cs:45`.
  - AC: after `OnNewGame`, every player pair has a defined `RelationshipType` (no TODO/defaults left);
    a 2-player human-vs-AI skirmish reports them as mutual enemies via `GetRelationship`; unit test.
- [ ] Wire skirmish slot/team data from `SkirmishManager` into relationship setup; make ally/enemy
      wiring reciprocal (`Player.AddAlly/AddEnemy`, `Logic/Player.cs:779-786`).
  - AC: same-team slots are allies and different-team slots are enemies; `A ally/enemy B ⇒ B ally/enemy
    A` (test asserts symmetry).
- [ ] Start a single-player skirmish without networking (`LocalSkirmishManager` path).
  - AC: `LocalSkirmishManager` reaches `Started` with no network connection; a headless boot reaches
    the first logic tick.
- [ ] Unify SP/MP spawn onto one shared skirmish path (SP = one human + AI slots through the same
      start-position/building/camera code, `Game.cs:745-763`); identify the human via an explicit
      slot flag set at setup, not the `_players[2]` fallback (`PlayerManager.cs:39-46`).
  - AC: a SP skirmish spawns each non-neutral player's starting building(s) at its `Player_N_Start`
    and places the camera, identical to the MP path; `LocalPlayer` resolves from the explicit flag;
    harness asserts each player has ≥1 starting structure at its start waypoint.
- [ ] AI player slots, faction/army selection, and start positions (fill AI slots in the lobby path).
  - AC: an unset AI slot is assigned a valid playable side, a free color, and a free start position
    before `StartGame`; no slot has an unresolved faction/position at game start.
- [ ] P1-5 Object-ownership tracking (foundational): maintained per-`Player` building/unit counts +
      owned-object set, updated on create/transfer/`OnDie`/`Destroy` (decided — not an on-demand scan).
      Consumed by P2 (AI target selection) and P3 (defeat detection); friendly to the scale tracks.
  - AC: per-`Player` building/unit counts are accurate after create/death/transfer (test creates +
    kills objects and asserts counts) with O(1) per-check cost.

### P2 — Skirmish AI opponent ([deep dive](deep-dives/skirmish-ai.md))
- [ ] Add a coarse-cadence `Update(...)` decision loop to `SkirmishAIPlayer`/`AIPlayer` (currently
      persistence-only — `Logic/AI/AIPlayer.cs:8-154`, `SkirmishAIPlayer.cs:3-31`).
  - AC: `SkirmishAIPlayer.Update(...)` is invoked from the player tick at the intended cadence (test
    asserts call count over N frames); no behavior required yet.
- [ ] Actuate AI decisions by emitting `Order`s through `OrderProcessor` (decided — not direct API),
      so AI actions enter the recorded stream.
  - AC: a thin actuation adapter emits `Order`s; AI-issued actions appear in the recorded order stream
    (verified by replaying the run).
- [ ] AI base-building: consume the stranded `AIData.SkirmishBuildLists`/`BuildListInfo`
      (`Logic/AI/AIData.cs`, `Logic/Map/BuildListInfo.cs`).
  - AC: starting from a base + dozer/worker, the AI builds the build-list structures in priority order;
    a headless run shows the AI's building count rising toward the build-list target within a frame
    budget.
- [ ] AI economy management (dozers/workers + supply).
  - AC: the AI maintains ≥1 supply structure and assigns harvesters; AI `BankAccount` income is
    positive over a sampled interval in a headless run.
- [ ] AI unit production (money/prereq gated via `Player.CanProduceObject`).
  - AC: the AI produces ≥N army units gated by money/prereqs; production is blocked when unaffordable
    (balance never < 0).
- [ ] AI attack/defense behavior (target via P1 relationships) — Track P scope is a minimum legal
      opponent (build/economize/produce/≥1 attack → terminating match); structure for configurable
      difficulty tiers but implement only the minimum tier now.
  - AC: once a threshold force exists, the AI moves units toward an enemy structure (via P1
    relationships) and engages; a headless match shows AI-initiated combat and can reach a win/lose
    outcome.

### P3 — Victory & defeat conditions ([deep dive](deep-dives/victory-defeat.md))
- [ ] Add a `GameOutcome` type + a `VictorySystem : GameSystem` ticked from the logic loop.
  - AC: `GameOutcome` is queryable off `GameLogic`/`IGame` and reads `InProgress` during play;
    `VictorySystem` ticks from the logic loop (test asserts it runs).
- [ ] Player-defeat detection — real Generals rule (no command/production structures and no means to
      rebuild: no dozer/worker + insufficient resources) as the default, exposed as configurable.
  - AC: a harness scenario that eliminates a player's qualifying structures + rebuild means flags that
    player defeated within the check cadence; the rule is selectable via config.
- [ ] Death→defeat event from `GameObject.OnDie/Destroy` (shared with P6-3 cleanup).
  - AC: a death/ownership-change notification fires from `OnDie`/`Destroy` and `VictorySystem` reacts
    without a full-object scan; test asserts the event fires once per death.
- [ ] Win/lose determination + match-end flow (stop sim, surface result, allow exit).
  - AC: when all non-allies of player X are defeated, `GameOutcome.Status=Decided` with X as winner;
    the sim stops/relaxes and exit is possible; harness asserts the decided outcome.
- [ ] Handle the `EndGame` order in `OrderProcessor` (currently unhandled).
  - AC: an `EndGame` order produces a `Decided` `GameOutcome` (surrender/quit) instead of logging
    "unimplemented"; test issues `EndGame` and asserts the outcome.
- [ ] Expose match outcome as a first-class queryable result (for headless assertion).
  - AC: the P0 harness can assert on `GameOutcome` (winner/loser/reason/frame); covered by a harness
    test.

### P4 — Economy loop end-to-end ([deep dive](deep-dives/economy-production-combat.md))
- [ ] Verify/complete supply collection → `BankAccount.Deposit` cycle (`BankAccount.cs:41-60`).
  - AC: a harness scenario with a harvester shows `BankAccount` balance increasing after a supply
    dump (assert positive delta).
- [ ] Confirm/implement construction/production debit `BankAccount.Withdraw` with cancel
      refunds (`ProductionUpdate.cs:472-549`, `OrderProcessor.BuildObject`) — audit could not confirm.
  - AC: queuing a unit/structure reduces `BankAccount` by its cost; cancel refunds; insufficient funds
    blocks the action; balance never < 0 (harness asserts all four).
- [ ] Power model interactions (`Player.GetEnergy`/`_hasInsufficientPower`, `Player.cs:41,92-104`).
  - AC: TBD — verify intended effect. Rough template: when consumption > production,
    `_hasInsufficientPower` is set and has a defined effect (e.g. production slowdown/disabled
    structures); test toggles power and asserts the effect.
- [ ] Close gaps in supply-truck / worker / dozer cycles.
  - AC: worker/dozer/supply-truck complete a full gather→dump cycle without stalling over a sampled
    run (no stuck states).

### P5 — Construction & production completeness ([deep dive](deep-dives/skirmish-ai.md) §1.4/§2.4)
- [ ] Player and AI building placement (`OrderProcessor.BuildObject` + `IBuilderAIUpdate`).
  - AC: both a player order and an AI request place and construct a structure to completion at a valid
    location; harness asserts a finished building for each.
- [ ] Faction build trees with prerequisites / tech gating (`Player.CanProduceObject:357-420`).
  - AC: a unit/structure with unmet prerequisites is non-buildable and becomes buildable once the
    prereqs exist; test asserts gating for ≥1 prereq chain.
- [ ] Unit/upgrade production from structures, end-to-end (`ProductionUpdate.QueueProduction/QueueUpgrade`).
  - AC: a queued unit spawns at the rally point and a queued upgrade completes and applies its effect;
    harness asserts both.
- [ ] Locate + verify the control-bar buildability consumer of
      `GameObject.CanPurchase/CanConstructUnit/CanEnqueueUpgrade` (audit did not find the UI binding).
  - AC: the UI consumer of those methods is identified and documented; buildable/unbuildable state in
    the control bar reflects prereqs + money.

### P6 — Combat & command verification ([deep dive](deep-dives/economy-production-combat.md) P6)
- [ ] Command coverage matrix: every baseline control-bar action → `OrderType` → real
      `OrderProcessor` handler; assert each via the harness.
  - AC: each baseline control-bar action maps to a handled `OrderType`; a harness command per action
    produces the expected effect; the matrix is documented with pass/fail.
- [ ] Verify weapon / damage-type / death coverage for baseline-match units
      (`Weapon`/`DamageModule`/`ActiveBody`/`FireWeaponUpdate`).
  - AC: for baseline units, weapon fire reduces target health via `DamageModule`/`ActiveBody` and
    triggers death at 0; harness asserts a kill.
- [ ] Death cleanup: selection invalidation + owner-tracking removal (share P3-4's death event).
  - AC: a dying selected unit is removed from selection and owner tracking (reusing the P3-4 event);
    test asserts no dangling references.
- [ ] Fill gaps surfaced by the P7 match run.
  - AC: TBD — populated as the P7 run surfaces defects. Rough template: each surfaced defect gets its
    own checklist item + a regression assertion in the harness.

### P7 — End-to-end skirmish validation
- [ ] Author/record a representative full-skirmish scenario (on the P0 harness).
  - AC: a representative match scenario exists and runs on the P0 harness headlessly to completion.
- [ ] Headless re-run asserting termination + expected win/lose outcome.
  - AC: the scenario terminates with the expected win/lose `GameOutcome` headlessly; exit code reflects
    pass/fail.
- [ ] Assert invariants (no negative money, no lost/duplicated units, AI made progress).
  - AC: the run asserts no negative money, no lost/duplicated units, and AI-made-progress; any failure
    is reported with the offending frame.
- [ ] Lock as the standing "is the game still playable?" regression check.
  - AC: the scenario runs in CI (or a documented command) as the standing playability check, documented
    in the repo.

---

## Track A — Foundations

See [`track-a-foundations.md`](track-a-foundations.md). Gated on Track P (P7).

### A1 — Headless simulation host ([deep dive](deep-dives/headless-host.md))
- [ ] A1-1 Headless seam audit & approach.
  - AC: TBD — a written audit (in the headless-host deep dive) lists renderer/audio/window couplings
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

### A2 — Reproducibility & statistical stability
- [ ] Centralize all randomness through a seeded source; audit uncontrolled nondeterminism.
  - AC: all randomness flows through a seeded source; an audit lists any uncontrolled sources; two
    seeded runs match within tolerance on key metrics.
- [ ] Define the conflict-resolution rules for concurrent writes.
  - AC: TBD — rules documented; concurrent writes resolve deterministically per the rule; a test
    exercises a conflicting-write case.
- [ ] Verification harness: metric/invariant stability within tolerance (+ opt-in strict-hash mode).
  - AC: a harness re-runs a scenario and asserts metric/invariant stability within tolerance, with an
    opt-in strict-hash mode.

### A3 — Simulation/presentation seam
- [ ] Formalize the logic↔presentation boundary; ensure logic never reads render-only state.
  - AC: TBD — boundary documented; a check/test ensures logic types don't reference render-only
    state; a headless run proves logic runs with no presentation.

### A4 — Experiment framework seams & harness
- [ ] A4-1 Simulation Domain Model seam (domain-agnostic entities/topology/interactions/metrics).
  - AC: TBD — a domain-agnostic seam interface exists; the RTS domain implements it and at least the
    transport/graph domain validates it against the same host.
- [ ] Stable seams for pathfinding, partitioning, fidelity policy, storage layout, model import (see
      [seams.md](seams.md)).
  - AC: TBD — each seam has an interface + ≥1 swappable implementation selectable at runtime; all are
    listed in [seams.md](seams.md).
- [ ] Experiment harness: select/configure an implementation, run vs. B1 benchmarks, report to A5.
  - AC: TBD — the harness selects/configures an impl, runs it against B1 benchmarks, and reports to
    A5; demonstrated by swapping one seam impl and comparing results.

### A5 — Scenario authoring & visualization ([deep dive](deep-dives/visualization.md))
- [ ] A5-1 Scenario authoring & setup (declarative + capture-from-run; built on the P0 harness).
  - AC: a declarative scenario (on P0) is authored and captured-from-run, then replayed; it round-trips.
- [ ] A5-2 Experiment matrix / parameter sweeps.
  - AC: a scenario declares sweep axes that expand to one run per cell; results are keyed for comparison.
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
      round-trip works at normal map sizes; document gaps. (Early-start sub-phase — no A1/A4
      dependency; gates B4a.)
  - AC: a normal-sized map with terrain and object placement round-trips through WorldBuilder (author
    → save → load → play in the engine) without errors; functional gaps documented.

---

## Track B — Scale

See [`track-b-scale.md`](track-b-scale.md). Gated on Track P (P7).

- [ ] B1 Profiling & robust benchmarking utilities (reusable harness, statistical rigor,
      regression detection, machine-readable output).
  - AC: a reusable harness runs repeatable seeded scenarios (incl. the 10k-unit interactive
    reference workload — the [scale anchor](open-questions.md)), reports mean+variance with warmup
    discarded, detects regressions vs. stored baselines, emits machine-readable output, and is reused
    by ≥1 other track.
- [ ] B2 Allocation & hot-path cleanup (per-tick allocations, collision-pair lists, cache hot
      queries, data layout).
  - AC: per-tick allocations in the named hot paths are eliminated (allocation profiler shows ~0
    steady-state allocations for the targeted paths); B1 shows no perf regression.
- [ ] B3 Parallel & data-oriented logic (intent/command buffers + conflict resolution; incremental
      SoA storage behind the module API).
  - AC: TBD — intent/command buffers + conflict resolution land behind the module API with no
    behavior change vs. baseline (verification harness within tolerance); SoA storage is swappable.
- [ ] B4 Spatial & pathfinding scaling (hierarchical/flow-field, group movement, async path requests).
  - AC: TBD — hierarchical/flow-field pathing + group movement + async requests scale to a target
    unit count within a path-latency budget in B1; no pathing regressions on baseline maps.
- [ ] B4a Larger maps via WorldBuilder ([deep dive](deep-dives/larger-maps.md)) — region editing,
      large-map wizard, terrain streaming/LOD, chunked storage, round-trip at scale,
      large-population scenario authoring (B4a-7: bulk/procedural object placement, region-based
      multi-object edit/undo, instanced editor rendering — pairs with A5-5). Depends on A5-6.
  - AC: TBD (multi-sub-phase — see the deep dive). Rough template: very large maps author → save →
    reload → play within budgets; region edits are O(brush) not O(map area); large object populations
    are placeable/editable interactively.
- [ ] B5 Rendering at scale (GPU instancing, LOD/impostors, culling, render-bucket/shadow).
  - AC: TBD — instancing/LOD/culling render the near-term anchor of 10k units at ~30 fps in B1 (see
    the [scale-anchor Decision](open-questions.md)); the tool viewports (B4a-7/A5-5) reuse it.
- [ ] B6 Distributed simulation via spatial sharding ([deep dive](deep-dives/distributed-simulation.md)).
  - AC: TBD (see the deep dive). Rough template: a sim runs sharded across ≥2 processes with
    cross-shard consistency within tolerance.
- [ ] B7 Variable simulation fidelity ([deep dive](deep-dives/variable-fidelity.md)) — replicated-state
      fidelity for shared/distributed, view-driven single-node only.
  - AC: TBD (see the deep dive). Rough template: fidelity reduction scales unit count with bounded
    statistical error vs. a full-fidelity baseline.

---

## Track C — External Models & Animations

See [`track-c-external-assets.md`](track-c-external-assets.md). Gated on Track P (P7).

- [ ] C1 Format-agnostic import abstraction (feeds the same `Model`/`ModelInstance` runtime types).
  - AC: TBD — an import abstraction feeds the same `Model`/`ModelInstance` runtime types; a
    stub/second importer validates the seam.
- [ ] C2 Skeleton / animation retargeting.
  - AC: TBD — an external skeleton/animation retargets onto an engine unit and plays correctly.
- [ ] C3 Material mapping.
  - AC: TBD — external materials map to engine materials with acceptable visual parity.
- [ ] C4 First concrete format — static meshes + skinned skeletal animation only.
  - AC: a first concrete external format loads static + skinned skeletal meshes and renders/animates
    in-engine.
- [ ] C5 Authoring & validation tooling (extend ReplaySketch/WorldBuilder; licensing out of scope).
  - AC: TBD — tooling validates an imported asset (geometry/skeleton/material checks) and surfaces
    failures.
- [ ] C6 Extended features (deferred): morph targets, advanced PBR.
  - AC: deferred — out of scope until C1–C5 land; no AC yet.
