# Deep Dive: Tech Trees & Era Progression (D1/D2)

> Expands phases D1 and D2 of [Track D — Gameplay Content Expansion](../track-d-gameplay-expansion.md).
> Part of the [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it
> lands.
>
> Audience: human — design intent and architecture diagrams. Code references are in the
> agent-context appendix at the bottom.
>
> The agent-context section at the bottom ([Current code appendix](#appendix-current-code--agent-context))
> contains file:line code references. Skip it if you are reading for design intent.

Goal: generalize the engine's existing upgrade/prerequisite mechanism into an explicit,
data-authored prerequisite-graph model, and add a genuinely new mechanic the SAGE games never
had — discrete era/age progression (Age of Empires/Empire Earth style) — as content on top of
the existing `GameObject`/module system, not a new Simulation Domain Model. Both are prerequisite
design work for Track D's reference non-SAGE scenario (D4).

---

## 1. Scope and non-goals

- In scope: a prerequisite-graph data model general enough to express branching tech trees
  (not just the SAGE games' flat per-object upgrade lists); an era/age state machine per player
  with age-up as a gated action; wiring both into `ObjectDefinition`/module availability so
  build/produce/ability options are gated by tech and era state.
- Out of scope (deferred to D3/D4 or beyond): the actual AoE/Empire Earth content itself (units,
  buildings, art) — that is external content ingested via [ModCollector](../plans/mod-collector.md)
  and Track C importers, not something this deep dive authors. Balance/design of any specific
  tech tree. A dedicated tech-tree editor UI (D4/D5 reuse ReplaySketch per the project's
  no-fragmentation tooling convention — see the [C5 tooling Decision](../open-questions.md)).
- This is content within the existing RTS domain. Per the
  [domain-scope Decision](../open-questions.md), new gameplay mechanics extend the
  `GameObject`/module system; they do not warrant a new Simulation Domain Model.

---

## 2. Design

### 2.1 Prerequisite graph (generalizing `UpgradeTemplate`)

Today, prerequisites are expressed ad hoc per `ObjectDefinition` (build prerequisites,
upgrade requirements) with no shared graph structure — each object/upgrade lists its own
requirements inline. D1 introduces an explicit graph:

```
TechNode {
    string Id;                     // stable identifier, referenced by ObjectDefinition/UpgradeTemplate
    TechNodeKind Kind;              // Unit | Building | Upgrade | Era
    IReadOnlyList<string> Requires; // Ids of TechNodes that must be "unlocked" first
    int? RequiredEra;               // null if era-independent
}

enum TechNodeKind { Unit, Building, Upgrade, Era }
```

`UpgradeTemplate` becomes one producer of `TechNode` entries (a thin adapter), so existing
Generals/BFME/BFME2 content keeps working unchanged — this is the same "refactor the existing
path to be the reference implementation" pattern Track C uses for W3D. The graph is authored in
INI alongside existing object/upgrade definitions, parsed through the existing `IniParseTable<T>`
mechanism (no new parser infrastructure).

A `TechTreeState` (per player) tracks which `TechNode` ids are unlocked, and exposes
`IsAvailable(nodeId)` — true iff every `Requires` entry is unlocked and (if `RequiredEra` is set)
the player's current era is at least that value. Build/produce/ability menus (control bar) query
this instead of the current per-object ad hoc checks.

### 2.2 Era/age progression (new mechanic)

`EraState` (per player): `CurrentEra` (int, 0-based), plus an in-progress age-up flag/timer.
Age-up is a gated action, not automatic:

```
EraDefinition {
    int Era;
    string DisplayName;
    IReadOnlyList<string> RequiredTechNodeIds; // must all be unlocked to start aging up
    int AgeUpCost;                              // resource cost, spent on start
    int AgeUpDurationFrames;                     // time gate
}
```

Age-up is issued as an order (same order-pipeline path as any other player command — see the
[P0 harness](playable-baseline-harness.md) for how orders are authored/asserted), validated
against `EraDefinition.RequiredTechNodeIds`/cost, then resolved after `AgeUpDurationFrames` via
the existing scheduled/update-module pattern (comparable to how production queues resolve).
On completion, `EraState.CurrentEra` increments, and any `TechNode`/`ObjectDefinition` gated by
`RequiredEra` becomes available. Era changes are also a natural, cheap fidelity/visual hook
(reskins, stat multipliers) that can be layered on later without new mechanism.

### 2.3 Save/load

Both `TechTreeState` and `EraState` are per-player data persisted through the existing
`StatePersister` pattern (`PersistVersion()` first, then explicit field persistence) — no new
save/load mechanism, consistent with the
[save/load reach Decision](../open-questions.md) (single-node `StatePersister`, not required to
work across sharded runs).

---

## 3. Sequencing notes

- D1 (prerequisite graph) can start once Track P (playable baseline) is stable, since it
  refactors `UpgradeTemplate` — doing this against a half-working match loop would be hard to
  verify. It does not need Track C.
- D2 (era progression) depends on D1 (`RequiredEra` gating needs the graph to exist) but not on
  Track C or ModCollector — it can be validated with existing placeholder Generals/BFME content
  (e.g. a toy 2-era test tree) before any external asset lands.
- D3 (mod content ingestion) is the first phase that needs [ModCollector](../plans/mod-collector.md)
  and Track C (C1 import abstraction at minimum) — it is what turns D1/D2's mechanism into actual
  AoE/Empire Earth-flavored content.
- D4 (reference scenario) is the integration proof: one small tech tree + one era transition,
  authored and asserted through ReplaySketch, same as every other phase's headless proof (see
  [`deep-dives/playable-baseline-harness.md`](playable-baseline-harness.md)).

---

## Architecture Diagrams

Placeholder — phase D1/D2 is `Not started`. Per the
[maintaining protocol](../agent/MAINTAINING.md), a class diagram (TechNode/TechTreeState/
EraDefinition/EraState and their relationship to `ObjectDefinition`/`UpgradeTemplate`) and a
sequence diagram (age-up order → validation → timed resolution → availability update) must
replace this placeholder before D1's first task card is marked `Ready`.

---

## Appendix: current code / agent context

- `src/OpenSage.Game/Logic/Object/UpgradeTemplate.cs` — existing per-object upgrade template;
  D1's adapter wraps this rather than replacing it.
- `src/OpenSage.Game/Logic/Object/ObjectDefinition.cs` — build/produce prerequisite fields D1's
  `TechNode.Requires` generalizes.
- `src/OpenSage.Game/Logic/GameLogic.cs` — top-level per-match state; `TechTreeState`/`EraState`
  are added as per-player state here, alongside existing `BankAccount`/player state.
- `src/OpenSage.Game/Logic/Object/Update/ProductionUpdate.cs` — the existing timed-completion
  pattern (`ProductionUpdate`) that age-up resolution should mirror rather than invent a second
  timing mechanism.
- Save/load: search `PersistVersion()` call sites in `Logic/` for the established
  `StatePersister` pattern D1/D2 state must follow.
