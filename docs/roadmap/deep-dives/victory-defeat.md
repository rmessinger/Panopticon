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

- **Defeat rule (baseline):** a player is defeated when they have **no production/command structures
  and no means to rebuild** (no dozer/worker and insufficient resources), per Generals skirmish rules.
  Start simple — *no buildings and no units* ⇒ defeated — then refine toward the real rule.
- **Ownership query:** since there's no owned-object list, either (a) add a maintained per-`Player`
  owned-object set (incremented on create/transfer, decremented on `OnDie`/`Destroy`), or (b) reuse
  the existing all-objects scan (`Player.GetEnergy` pattern, `Player.cs:92-104`) at the coarse
  cadence. **(a) is preferred** — it's O(1) per check and is also useful for AI (P2) and scale tracks;
  flag as a design choice (↗).
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
- **Reads:** per-`Player` owned-object counts (new) or all-objects scan (`Player.cs:92-104`);
  relationships/teams from [P1](skirmish-setup.md).
- **Handle:** `OrderType.EndGame` in `OrderProcessor.Process` (currently unhandled).
- **Exposes:** `GameOutcome` off `GameLogic`/`IGame` for the [P0 harness](playable-baseline-harness.md)
  and the [A5 visualizer](visualization.md).

## 4. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| **P3-1. `GameOutcome` + `VictorySystem` scaffold** | Add the outcome type and a `GameSystem` that ticks a check from the logic loop; exposes `InProgress`. | Not started |
| **P3-2. Ownership tracking** | Maintain per-`Player` building/unit counts via create/death/transfer (preferred) or coarse all-objects scan. *(↗ which approach.)* | Not started |
| **P3-3. Defeat & win rules** | Implement defeat (no structures + no rebuild) and win (all non-allies defeated, via P1 teams). | Not started |
| **P3-4. Death→defeat event** | Emit a death/ownership-change notification from `GameObject.OnDie/Destroy`; `VictorySystem` reacts incrementally. | Not started |
| **P3-5. End-of-match flow** | Stop sim, surface result, handle `OrderType.EndGame`, allow exit; expose `GameOutcome` for headless. | Not started |

## 5. Sequencing & dependencies
- **P3 depends on P1** (teams/relationships decide who wins/loses).
- **P3 unblocks the [P0 harness](playable-baseline-harness.md) P0-4 outcome assertions** and is a hard
  prerequisite for **P7** (the capstone asserts the match *terminates* with the expected
  `GameOutcome`).
- P3-2's ownership tracking is reused by P2 (AI target selection) and is friendly to the scale tracks.

## 6. Open design questions (↗)
- **Ownership tracking approach** — maintained per-player counts vs. on-demand scan (§2.2). Affects
  per-tick cost at scale; recommend maintained counts.
- **Exact defeat rule** — strict "no command center" vs. "no buildings and no units" vs. configurable.
  Start with the simplest terminating rule and refine.

## 7. Related
- Track overview: [`../track-p-playable-baseline.md`](../track-p-playable-baseline.md)
- Teams it needs: [`skirmish-setup.md`](skirmish-setup.md) (P1)
- Harness that asserts the outcome: [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0)
- Combat/death paths: [`economy-production-combat.md`](economy-production-combat.md) (P6)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
