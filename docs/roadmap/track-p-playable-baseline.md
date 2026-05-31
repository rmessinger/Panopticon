# Track P — Playable Baseline (prerequisite)

> Part of the [Large-Scale Simulation Roadmap](README.md). **The hard prerequisite for every
> other track.** Before the engine is worth scaling, sharding, or measuring, the *reference game*
> must be **fully playable end-to-end**: start a skirmish against an AI opponent, build a base,
> run an economy, produce and command units, fight, and reach a **win/lose** outcome. Everything in
> Tracks A/B/C assumes a correct, complete simulation underneath — this track delivers it.
>
> **Bar:** a full Command & Conquer: Generals / Zero Hour skirmish match is playable end-to-end
> (build → fight → win/lose) against a computer opponent.
>
> See [`current-state.md`](current-state.md) for the architecture these phases modify.

## Why this comes first

A scaling/experimentation framework built on a half-working game would measure and distribute the
wrong thing. The [headless host (A1)](deep-dives/headless-host.md), [reproducibility (A2)](track-a-foundations.md),
and the [benchmarks (B1)](track-b-scale.md) all need a *complete* match loop as their unit of
work — a run that actually starts, progresses, and **terminates with a result**. Track P makes that
loop real and correct; the rest of the roadmap then scales and pluginizes it.

## Status from the current code (audit)

A targeted audit of `src/` (May 2026) found the match loop **partial, not yet end-to-end**. The
strongest gaps, in priority order:

- **Victory/defeat — missing.** No skirmish win/lose / match-end controller found in
  `src/OpenSage.Game/Logic`. Object death exists (`GameObject.Kill()`/`OnDie()`,
  `Logic/Object/GameObject.cs`) but nothing turns "all your buildings/units are gone" into a
  **player-defeated** and **match-over** result.
- **AI opponent — stubbed.** `AIPlayer` (`Logic/AI/AIPlayer.cs`) and `SkirmishAIPlayer`
  (`Logic/AI/SkirmishAIPlayer.cs`) are essentially persisted state with **no decision loop** — no
  base-building, economy, or attack behavior.
- **Skirmish setup & player relationships — partial.** `SkirmishManager` /
  `LocalSkirmishManager` (`Network/SkirmishManager.cs`) build player settings and start a game via
  an `EchoConnection`, but `PlayerManager.OnNewGame()` still has `// TODO: Setup player
  relationships` (`Logic/PlayerManager.cs`) — allies/enemies/teams aren't wired.
- **Economy / construction / production / combat / command — present but unproven.** Money
  (`Logic/BankAccount.cs`), supply logistics (`.../AIUpdate/Supply*Update.cs`), construction
  lifecycle and production queues (`GameObject` construction methods, `Logic/Object/Update/ProductionUpdate.cs`),
  selection/orders (`Logic/SelectionSystem.cs`, `Logic/OrderGeneratorInputHandler.cs`), the control
  bar (`Mods.Generals/Gui/GeneralsControlBar.cs`), and combat death all exist, but none are verified
  to work **together for a whole match**. No skirmish/AI/victory tests exist in `OpenSage.Game.Tests`.

## Phases

> The **Milestone** column states the single demonstrable outcome that marks each phase *done* (its
> definition-of-done); the granular steps to get there live in
> [`implementation-checklist.md`](implementation-checklist.md).

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| **P0. ReplaySketch arbitrary-command harness** | Finalize [ReplaySketch](deep-dives/visualization.md) into a general command-driven **authoring + validation** harness. Today its scenario model is a closed 5-value vocabulary (`ActionType`: Construct/GatherResources/RecruitBasicUnit/AttackEnemyBase/EnemyBaseDestroyed) over hardcoded USA/GLA object definitions (`KnownDefinitions`), exported as a `.rep` of engine `Order`s. Generalize it to express **any engine `Order`** (move/attack/build/ability/…) over **any object definition and player**, and add a **validation/assertion layer** (expected post-conditions, invariants, outcomes) so an *arbitrary* command set can be run headlessly and checked pass/fail — **at arbitrary command volume** (programmatic/procedural generation of large order sets, not hand-entry; the harness's own scaling is finalized under [A5-5](deep-dives/visualization.md)). This is the substrate every other P phase — and later [A5](deep-dives/visualization.md) — validates through. **Detailed design:** [deep-dives/playable-baseline-harness.md](deep-dives/playable-baseline-harness.md). | An arbitrary, procedurally-generated order set over any object definition and player runs headlessly and is asserted pass/fail against post-conditions. | Not started | — |
| **P1. Skirmish setup & player relationships** | Make starting a single-player skirmish vs. AI work without networking: AI player slots, faction/army selection, start positions, and — critically — **player relationships/teams** (resolve the `PlayerManager.OnNewGame()` TODO: allies, enemies, neutrals). Result: a match boots with a human + ≥1 AI player correctly related. **Detailed design:** [deep-dives/skirmish-setup.md](deep-dives/skirmish-setup.md). | A skirmish boots offline with a human + ≥1 AI player and correct ally/enemy/neutral relationships. | Not started | — |
| **P2. Skirmish AI opponent** | Give `SkirmishAIPlayer`/`AIPlayer` an actual decision loop: build base from build lists, manage economy (dozers/workers + supply), produce units, and mount attacks/defense. Difficulty need not be tournament-grade — it must play a *complete, legal* game. **Detailed design:** [deep-dives/skirmish-ai.md](deep-dives/skirmish-ai.md). | An AI opponent autonomously builds a base, runs its economy, produces units, and attacks/defends through a full match. | Not started | P1, P4, P5 |
| **P3. Victory & defeat conditions** | The missing match-end system: detect **player defeat** (no production/command structures and no way to rebuild, per game rules), declare **win/lose**, and drive end-of-match flow (stop the sim, surface the result, allow exit). Make the result a **first-class, queryable outcome** (so headless runs can assert it). **Detailed design:** [deep-dives/victory-defeat.md](deep-dives/victory-defeat.md). | A match terminates with a first-class, queryable win/lose result when a player is eliminated. | Not started | P1 |
| **P4. Economy loop end-to-end** | Verify and complete the full money loop: harvest/supply collection → deposit to `BankAccount` → fund construction and production → spend. Close any gaps in supply-truck/worker/dozer cycles and power. **Detailed design:** [deep-dives/economy-production-combat.md](deep-dives/economy-production-combat.md). | A worker/supply cycle funds construction and production end-to-end with a non-negative `BankAccount`. | Not started | P1 |
| **P5. Construction & production completeness** | Player **and AI** building placement, full faction **build trees with prerequisites/tech gating**, and unit/upgrade production from structures. Ensure prerequisites and `BuildList`-style data drive both sides. **Detailed design:** [deep-dives/skirmish-ai.md](deep-dives/skirmish-ai.md) (§1.4/§2.4, P5-1/P5-2). | Player and AI both construct prerequisite-gated faction build trees and produce units/upgrades from structures. | Not started | P1, P4 |
| **P6. Combat & command verification** | Confirm the player can select units and issue move/attack/build/ability orders through the control bar end-to-end, and that weapon/damage-type/death paths cover the units a baseline match uses. Fill gaps surfaced by P7's match run. **Detailed design:** [deep-dives/economy-production-combat.md](deep-dives/economy-production-combat.md) (P6). | A player drives selection plus move/attack/build/ability orders through the control bar, with combat and death resolving correctly. | Not started | P1 |
| **P7. End-to-end skirmish validation** | Capstone: drive a **whole skirmish to a win/lose result** and lock it as a regression scenario. Primary vehicle is **[ReplaySketch](deep-dives/visualization.md)** built on **P0's command/validation harness** — record/author a representative match and re-run it headlessly, asserting the match terminates with the expected outcome and key invariants (no negative money, no lost/duplicated units, AI made progress). Becomes the standing "is the game still playable?" check. | A whole authored skirmish re-runs headlessly to its expected win/lose outcome and is locked as a standing regression check. | Not started | P0, P2, P3, P4, P5, P6 |

## Notes

- **Track P gates the roadmap.** [Track A's A1 (headless host)](track-a-foundations.md) — and by
  extension A2/A4 and all of Tracks B/C — depends on **P7** (a validated, terminating match loop).
  The headless host's *unit of work* is exactly the skirmish that Track P makes real.
- **ReplaySketch is the primary validation tool.** Per the project's tooling convention,
  end-to-end and scenario testing flows through **ReplaySketch** (`OpenSage.Tools.ReplaySketch`)
  rather than bespoke harnesses — it is the entry point lower-effort/automated agents use. **P0
  finalizes it into a general harness for authoring and validating *any* arbitrary command/order
  set** (not just the current fixed `ActionType` vocabulary), so P1–P7's checks — and the
  [A5 result visualizer](deep-dives/visualization.md) later — all run through one tool.
- **Reference game = Generals / Zero Hour.** This track is scoped to the reference domain only
  (the RTS [domain model](track-a-foundations.md)). It is *not* about feature parity with the
  original game — it is about a **complete, correct, terminating** skirmish loop. BFME/other games
  are out of scope here.
- **Relationship to determinism (A2).** Track P only needs the match to *run and terminate
  correctly*, not to be reproducible; the seeded/comparable-run guarantees come later in
  [A2](track-a-foundations.md). But P3's queryable win/lose outcome and P7's invariant checks are
  what A2's verification harness and B1's benchmarks will assert against.
- **Small skirmish/victory config seam.** Two Track-P decisions are deliberately exposed as
  configuration rather than hard-coded: the **defeat rule** (the real Generals condition is the default
  but swappable — [P3](deep-dives/victory-defeat.md)) and **AI difficulty** (structured for tiers, but
  only the minimum legal-opponent tier is populated — [P2](deep-dives/skirmish-ai.md)). Track P only
  populates the defaults/minimum; the seam exists so later experiments (and [A4](track-a-foundations.md)
  pluggable policies) can vary them without re-plumbing.
