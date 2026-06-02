# Deep Dive: Skirmish AI Opponent (P2)

> Expands phase P2 of [Track P — Playable Baseline](../track-p-playable-baseline.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it lands.

Goal: give `SkirmishAIPlayer`/`AIPlayer` an actual strategic decision loop so a computer
opponent plays a complete, legal skirmish: build a base from build lists, manage economy
(dozers/workers + supply), produce units, and mount attacks/defense. Difficulty need not be
tournament-grade — it must reach a terminating, winnable/losable game.

This deep dive also covers the construction/production execution surface (P5) the AI drives,
since the two are tightly coupled: P2 is the decision layer, P5 is the mechanism it actuates.

---

## 1. Current code (grounded)

### 1.1 The strategic AI is state-only (the gap)
- `AIPlayer` is persisted fields only — counters, flags, an object id, a position — and `Persist(...)`;
  no update/decision loop (`src/OpenSage.Game/Logic/AI/AIPlayer.cs:8-154`).
- `SkirmishAIPlayer` adds 4 persisted scalars + `Persist(...)`; no behavior
  (`Logic/AI/SkirmishAIPlayer.cs:3-31`).
- ⇒ There is currently no strategic AI at all, only save/load state.

### 1.2 Per-object AI exists (unit-level, not strategic)
- Base `AIUpdate` owns locomotion, target points, pathing/turret state, idle/move helpers
  (`Logic/Object/Update/AIUpdate/AIUpdate.cs:13-260`).
- Builder behavior: `DozerAIUpdate` (`.../DozerAIUpdate.cs:9-74`) and `WorkerAIUpdate`
  (`.../WorkerAIUpdate.cs:12-220`) wrap `DozerAndWorkerState` with `SetBuildTarget`/`SetRepairTarget`.
- Harvester loop: `SupplyAIUpdate` (search/approach/request/gather/pickup/dump,
  `.../SupplyAIUpdate.cs:8-258`) and the thin `SupplyTruckAIUpdate` (`.../SupplyTruckAIUpdate.cs:6-34`).
- These drive individual units; none is a player-level strategy loop.

### 1.3 Data-driven AI inputs exist but are unconsumed
- `AIData` parses the `AIData` INI block and stores `SkirmishBuildLists`
  (`Logic/AI/AIData.cs:7-260`; `SkirmishBuildList` parse `:99-100,242-245`).
- `BuildListInfo` carries AI-only fields: `ResourceGatherers`, `IsSupplyBuilding`, `DesiredGatherers`,
  `CurrentGatherers`, `PriorityBuild`, `IsBuildable` (`Logic/Map/BuildListInfo.cs:10-237`).
- `IniParser.BlockParsers.cs` parses `AIData`/`SkirmishAIData`; `SkirmishAIData.cs` exists (container).
- ⇒ No code consumes `AIData.SkirmishBuildLists` yet — the build-order data is parsed and stranded.

### 1.4 How actions are actuated (the P5 mechanism)
- Human/scripted orders run through `OrderProcessor.Process(...)`
  (`Logic/Orders/OrderProcessor.cs:96-204`):
  - Build structure → `CreateObject(...)` + `GameObject.PrepareConstruction()` +
    `(dozer?.AIUpdate as IBuilderAIUpdate)?.SetBuildTarget(...)`.
  - Upgrade → `ProductionUpdate.QueueUpgrade(...)`; Unit → `ProductionUpdate.QueueProduction(...)`;
    Repair → `SetRepairTarget(...)`.
- `ProductionUpdate` owns the queue and spawn: `Update()` advances the front job and spawns units
  (`Logic/Object/Update/ProductionUpdate.cs:59-156`); `ProduceObject(...)` calls
  `GameEngine.GameLogic.CreateObject(...)`, places the transform, and moves out to the rally point
  (`:343-395`); `QueueProduction(...)` (`:472-483`), `QueueUpgrade(...)` (`:516-525`),
  `CanProduceObject(...)` (`:271-291`), `CanEnqueue()` (`:551`).
- Construction lifecycle on `GameObject`: `PrepareConstruction()` (`:991-1034`),
  `SetIsBeingConstructed()` (`:1039-1044`), `AdvanceConstruction()` (`:1046-1058`),
  `FinishConstruction()` (`:1060-1074`), `IsBeingConstructed()` (`:1077-1082`)
  (`Logic/Object/GameObject.cs`).
- Prerequisites/tech gating: `Player.CanProduceObject(ObjectDefinition)` checks
  `Prerequisites.Objects` (AND of OR-lists) + `Prerequisites.Sciences` (`Logic/Player.cs:357-420`);
  `GameObject.CanConstructUnit(...)` (`:1186-1192`), `CanPurchase(CommandButton)` (`:1164-1182`),
  `CanEnqueueUpgrade(...)` (`:1194-1222`).
- Builder placement: `OrderProcessor.BuildObject` (`:96-121`) → `DozerAIUpdate.SetBuildTarget`
  (`DozerAIUpdate.cs:41-46`) / `WorkerAIUpdate.SetBuildTarget` (`WorkerAIUpdate.cs:48-54`); builder
  cancels if the target dies (`GameObject.cs:1298-1316`).
- Upgrades: `QueueUpgrade`/`CancelUpgrade` (`ProductionUpdate.cs:516-549`); completion via
  `GrantUpgrade(...)` (`:113-123`); `Player.AddUpgrade/HasUpgrade/HasEnqueuedUpgrade`
  (`Player.cs:423-492`); `GameObject.Upgrade(...)`/`UpdateUpgradeableModules()` (`:1232-1247`).

---

## 2. Design

### 2.1 Add a strategic decision loop to the skirmish AI
Give `SkirmishAIPlayer` (and/or `AIPlayer`) an `Update(...)` driven from the player tick (the same
place `PlayerManager`/`GameLogic` advance players each frame). The loop is a simple, legible state
machine running at a coarse cadence (every N logic frames, not every frame):

1. Economy — ensure a supply structure + enough harvesters; if income is low and affordable,
   queue another harvester/supply building.
2. Base build-out — walk the `AIData.SkirmishBuildLists` for the AI's faction in priority order;
   for the next unbuilt, affordable, prerequisite-satisfied structure, issue a build.
3. Production — at production structures, queue army units toward a target composition, gated by
   `Player.CanProduceObject(...)`.
4. Military — once a threshold force exists, select an attack target (an enemy structure via the
   relationship data from [P1](skirmish-setup.md)) and issue attack-move; otherwise hold/defend.

The loop must be idempotent and budget-aware: never queue what it can't afford
(`BankAccount`), never duplicate in-flight build/production jobs (check
`ProductionUpdate`/`IsBeingConstructed`).

### 2.2 Consume the stranded build-list data (P5)
Wire `AIData.SkirmishBuildLists` / `BuildListInfo` (`AIData.cs`, `BuildListInfo.cs`) into the base
build-out step — this is the data the original engine drives skirmish AI from, already parsed and
waiting. `BuildListInfo`'s `PriorityBuild`, `DesiredGatherers`, `IsSupplyBuilding`, `IsBuildable`
fields directly inform steps 1–2.

### 2.3 How the AI issues actions — Decision: orders via `OrderProcessor`
Two actuation styles were considered:
- (a) Via `Order`s through `OrderProcessor` — the AI emits the same `Order`s a human would
  (`BuildObject`, `CreateUnit`, `ForceAttack…`). Pro: one code path, automatically replay/record
  compatible (matches the [P0 harness](playable-baseline-harness.md) and A2 reproducibility). Con:
  must synthesize valid order arguments (positions, object ids).
- (b) Direct API calls — `GameLogic.CreateObject`, `PrepareConstruction`,
  `ProductionUpdate.QueueProduction/QueueUpgrade`, `IBuilderAIUpdate.SetBuildTarget`. Pro: simpler
  to author. Con: bypasses the order stream, so AI actions wouldn't appear in replays/recordings —
  a problem for A2/B-track reproducibility.
- Decision: use (a) — AI actions live in the same order stream the framework records and
  replays, keeping Track P's loop honest for the scaling tracks (the argument-synthesis cost is
  accepted). Settled before P2-2. See [open-questions](../open-questions.md).

### 2.4 P5 — construction/production completeness
The mechanism is largely present (§1.4). P5's work is gap-closing + proving both sides:
- Confirm construction debits `BankAccount` (the audit could not confirm production/construction
  debits money — verify in `ProductionUpdate`/build paths; add if missing).
- Confirm prerequisite/tech gating holds for AI-issued builds the same as for the player
  (`Player.CanProduceObject`, `GameObject.CanConstructUnit`).
- Confirm upgrades complete and apply (`GrantUpgrade`, `Player.AddUpgrade`,
  `GameObject.Upgrade`).
- Identify the control-bar/UI buildability binding (the audit did not locate the UI consumer of
  `CanPurchase`/`CanConstructUnit`/`CanEnqueueUpgrade`) — needed for P6 player-side parity.

---

## 3. Integration surface
- AI decision (new): an `Update(...)` on `SkirmishAIPlayer`/`AIPlayer`, ticked from the player
  loop; reads `Player.BankAccount`, `AIData.SkirmishBuildLists`, relationships (P1).
- Actuation (decided — orders): the AI emits `Order`s via `OrderProcessor.Process` (the same path a
  human uses), not the direct API (`GameLogic.CreateObject` / `GameObject.PrepareConstruction` /
  `ProductionUpdate.QueueProduction/QueueUpgrade` / `IBuilderAIUpdate.SetBuildTarget/SetRepairTarget`),
  so AI actions enter the recorded order stream (§2.3).
- Gating: `Player.CanProduceObject`, `GameObject.CanConstructUnit/CanEnqueueUpgrade`.
- Targets: consume the foundational [P1-5](skirmish-setup.md) per-`Player` owned-object set for
  enemy target selection (no all-objects scan).

## 4. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| P2-1. AI tick scaffold | Add a coarse-cadence `Update(...)` to the skirmish AI, ticked from the player loop; no behavior yet. | Not started |
| P2-2. Economy & base build-out | Consume `SkirmishBuildLists`; ensure supply + harvesters; build prerequisite-satisfied, affordable structures in priority order. (Actuation = orders — decided.) | Not started |
| P2-3. Unit production | Queue army units toward a target composition, money/prereq gated. | Not started |
| P2-4. Military behavior | Threshold force → select enemy target (P1 relationships) → attack-move; basic defense. | Not started |
| P5-1. Funding & gating verification | Confirm construction/production debit `BankAccount` and respect prerequisites for both player and AI; fix gaps. | Not started |
| P5-2. Upgrades & buildability binding | Verify upgrade completion/application; locate + verify the control-bar buildability consumer. | Not started |

## 5. Sequencing & dependencies
- P2 depends on P1 (related, spawned players + the foundational [P1-5](skirmish-setup.md)
  ownership tracking that P2-4 uses for target selection), P4 (a working economy to spend), and
  P5 (construction/production mechanism). P2-1 can scaffold in parallel; P2-2+ need P4/P5.
- Actuation = orders (§2.3, decided): the AI emits `Order`s through `OrderProcessor`, so its actions
  enter the recorded order stream — this reuses the engine order path the
  [P0 harness](playable-baseline-harness.md) drives, and P2's recorded-stream check runs through P0
  (a hard requirement for A2/B reproducibility).
- P2 feeds P7 (the AI must "make progress" — an asserted invariant in the
  [P0 harness](playable-baseline-harness.md)).

## 6. Resolved design questions & open work
- Actuation style — Decided: orders via `OrderProcessor` (§2.3), so AI actions enter the
  recorded stream. See [open-questions](../open-questions.md).
- AI competence scope — Decided: minimum legal opponent in Track P
  (build/economize/produce/≥1 attack → terminating win/lose); structured for configurable difficulty
  tiers but only the minimum tier is implemented now. Richer behavior is later expansion. See
  [open-questions](../open-questions.md).
- AI architecture depth — single state machine now; a pluggable AI policy is a candidate
  [A4](../track-a-foundations.md) experiment seam (the configurable difficulty tiers this scope
  leaves stubbed are the natural variation axis), not a P2 requirement — consistent with the scope
  decision above.
- Where `BankAccount` is debited for construction — open verification task (not a design
  choice): locate/confirm before P5-1.

## 7. Related
- Track overview: [`../track-p-playable-baseline.md`](../track-p-playable-baseline.md)
- Players/relationships it needs: [`skirmish-setup.md`](skirmish-setup.md) (P1)
- Economy it spends: [`economy-production-combat.md`](economy-production-combat.md) (P4/P5/P6)
- Order stream / harness: [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
