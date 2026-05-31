# Deep Dive: Victory & Defeat Conditions (P3)

> Expands phase **P3** of [Track P — Playable Baseline](../track-p-playable-baseline.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it lands.

**Goal:** build the **missing match-end system** — detect **player defeat**, declare **win/lose**,
drive end-of-match flow (stop the sim, surface the result, allow exit), and expose the result as a
**first-class, queryable `GameOutcome`** so headless runs (the [P0 harness](playable-baseline-harness.md))
can assert on it. This is the single biggest correctness gap in the baseline game.

---

## 1. Current code (grounded) — it's missing

- **No outcome logic.** No meaningful matches for `Victory|Defeat|GameOver|EndGame|IsDefeated|WinLose`
  in `src/OpenSage.Game` beyond order plumbing/enum references — there is no win/lose controller.
- **`OrderType.EndGame` exists but is unhandled.** It is defined in
  `Logic/Orders/OrderType.cs`, but `OrderProcessor.Process(...)`
  (`Logic/Orders/OrderProcessor.cs`) has **no `EndGame` handler** (default path logs
  `Unimplemented order type`).
- **Ownership tracking is partial.** `Player` exposes `SupplyManager`, `BankAccount`, selection sets,
  and `GetEnergy(...)` which *iterates* `allGameObjects.Objects` to sum owned energy producers
  (`Logic/Player.cs:22,73,92-104,130-135`) — but there is **no per-player owned-object list or
  building-count field**; ownership is derived by scanning all objects.
- **Death flow exists, no event hook.** `GameObject.IsDestroyed`, `Kill(...)`, `OnDie(...)`,
  `Destroy()` exist (`Logic/Object/GameObject.cs`); `OnDie` iterates death modules and plays a sound;
  `Kill()` routes into death handling. There is **no death/destroyed event** a defeat system could
  subscribe to.
- **Tick hook available.** A per-frame victory check can live in `GameLogic.Update(...)`
  (`Logic/GameLogic.cs`) or the main game tick (`Game.cs:876-959`, `Scene3D.LocalLogicTick`).

---

## 2. Design

### 2.1 A `GameOutcome` value (the queryable result)
Introduce a first-class outcome type exposed off `GameLogic`/`IGame`:

```
GameOutcome {
    OutcomeStatus Status;        // InProgress | Decided
    Player? WinningPlayer;       // or WinningTeam
    Player? LosingPlayer;        // (per defeated player)
    OutcomeReason Reason;        // Eliminated | Surrendered | AllAlliesDefeated | ...
    int DecidedOnFrame;
}
```

This is what the [P0 harness](playable-baseline-harness.md) §3.3 outcome assertions read, and what
A2's verification harness / B1's benchmarks later assert against. It is the **termination signal** the
headless run loop uses instead of today's ad-hoc "exit on enemy-CC destruction"
(`TestHarness/Program.cs:232-267`).

### 2.2 Defeat detection
A `VictorySystem` (a `GameSystem`, per the engine's subsystem pattern) runs a check at a **coarse
cadence** (not every frame) from the logic tick:

- **Defeat rule (Decision):** a player is defeated when they have **no command/production structures
  and no means to rebuild** (no dozer/worker and insufficient resources), per Generals skirmish rules
  — implemented as the **default** but exposed as a **configurable/data-driven** rule so experiments
  can swap simpler/stricter conditions. See [open-questions](../open-questions.md).
- **Ownership query (Decision):** consume the maintained per-`Player` owned-object set/counts provided by
  the foundational **[P1-5](skirmish-setup.md)** ownership tracking (incremented on create/transfer,
  decremented on `OnDie`/`Destroy`), giving **O(1)** checks rather than the all-objects scan
  (`Player.GetEnergy` pattern, `Player.cs:92-104`). The same maintained view feeds the AI (P2) and the
  scale tracks. See [open-questions](../open-questions.md).
- **Win rule:** when all players *not allied* to player X are defeated (via [P1](skirmish-setup.md)
  relationships/teams), X (and allies) win.

### 2.3 Death → defeat plumbing
Add a **death/ownership-change notification** so the `VictorySystem` (and AI, selection cleanup) can
react without polling every object every frame:
- Emit from `GameObject.OnDie(...)`/`Destroy()` (`GameObject.cs`) and from ownership transfer.
- The `VictorySystem` decrements the owner's building/unit counts and re-checks only the affected
  player. This also fixes latent gaps the audit flagged (selection invalidation on death, ownership
  cleanup).

### 2.4 End-of-match flow
On `Status → Decided`:
- Stop/relax the sim (pause logic or switch to an observer state).
- Surface the result (UI win/lose dialog for interactive play; structured `GameOutcome` for headless).
- Handle `OrderType.EndGame` in `OrderProcessor` (an explicit surrender/quit also produces a
  `GameOutcome`).
- Allow exit back to the menu (interactive) or clean run-loop termination (headless).

---

## 3. Integration surface
- **New:** `GameOutcome` type + a `VictorySystem : GameSystem`; a death/ownership-change event from
  `GameObject.OnDie/Destroy`.
- **Hook:** `GameLogic.Update(...)` (coarse-cadence check) / `Game.cs:876-959` tick.
- **Reads:** the maintained per-`Player` owned-object counts from foundational
  [P1-5](skirmish-setup.md) (not an all-objects scan, cf. `Player.cs:92-104`);
  relationships/teams from [P1](skirmish-setup.md).
- **Handle:** `OrderType.EndGame` in `OrderProcessor.Process` (currently unhandled).
- **Exposes:** `GameOutcome` off `GameLogic`/`IGame` for the [P0 harness](playable-baseline-harness.md)
  and the [A5 visualizer](visualization.md).

## 4. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| **P3-1. `GameOutcome` + `VictorySystem` scaffold** | Add the outcome type and a `GameSystem` that ticks a check from the logic loop; exposes `InProgress`. | Not started |
| **P3-2. Defeat & win rules** | Implement defeat (real Generals rule — no command/production structures + no rebuild — as the default, configurable) and win (all non-allies defeated, via P1 teams); consumes foundational [P1-5](skirmish-setup.md) ownership tracking. | Not started |
| **P3-3. Death→defeat event** | Emit a death/ownership-change notification from `GameObject.OnDie/Destroy`; `VictorySystem` reacts incrementally. | Not started |
| **P3-4. End-of-match flow** | Stop sim, surface result, handle `OrderType.EndGame`, allow exit; expose `GameOutcome` for headless. | Not started |

## 5. Sequencing & dependencies
- **P3 depends on P1** — teams/relationships decide who wins/loses, and the foundational
  **[P1-5](skirmish-setup.md)** ownership tracking feeds defeat detection.
- **P3 unblocks the [P0 harness](playable-baseline-harness.md) P0-4 outcome assertions** and is a hard
  prerequisite for **P7** (the capstone asserts the match *terminates* with the expected
  `GameOutcome`).
- The **[P1-5](skirmish-setup.md)** ownership tracking P3 consumes is shared with P2 (AI target
  selection) and is friendly to the scale tracks.

## 6. Resolved design questions
- **Ownership tracking approach** — **Decided:** maintained per-`Player` counts/owned-object set
  (§2.2), O(1) per check, reused by P2 and the scale tracks. See
  [open-questions](../open-questions.md).
- **Exact defeat rule** — **Decided:** the real Generals rule (no command/production structures + no
  means to rebuild) as the **default**, exposed as configurable. See
  [open-questions](../open-questions.md).

## 7. Related
- Track overview: [`../track-p-playable-baseline.md`](../track-p-playable-baseline.md)
- Teams it needs: [`skirmish-setup.md`](skirmish-setup.md) (P1)
- Harness that asserts the outcome: [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0)
- Combat/death paths: [`economy-production-combat.md`](economy-production-combat.md) (P6)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
