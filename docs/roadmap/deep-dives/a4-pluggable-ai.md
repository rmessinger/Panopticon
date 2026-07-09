# Deep Dive: Pluggable AI Decision Policy

> Expands phase A4 of [Track A — Foundations](../track-a-foundations.md). Part of the
> [Large-Scale Simulation Roadmap](../README.md).
>
> Audience: human overview of the pluggable-AI design. Code references are in the
> agent-context appendix at the bottom.

Purpose: make a player's strategic decision-making its own A4 experiment seam. The design keeps
rule-based skirmish AI from [P2](../track-p-playable-baseline.md) as one swappable implementation
rather than the only one.

This document designs the seam only. It does not implement a neural-network policy or a training
harness; §6 covers the follow-on work it unblocks.

---

## 1. Rationale

[skirmish-ai.md §5](skirmish-ai.md#5-resolved-design-questions-and-open-work) already records the
key decision: a pluggable AI policy is an A4 experiment seam, not a P2 requirement. P2 lands one
concrete policy, `SkirmishAIPlayer`, described in [skirmish-ai.md §1.1](skirmish-ai.md#11-add-a-strategic-decision-loop-to-the-skirmish-ai). That is the shape an
[A4](../track-a-foundations.md) seam needs: a stable interface with one reference
implementation, sized so a very different implementation such as a neural net can sit behind the
same interface and be selected or compared without caller changes.

The variation axis is narrow: how a player's per-tick strategic decisions are made. It is not
per-object AI (`AIUpdate`/`DozerAIUpdate`/`SupplyAIUpdate`, etc.), and it is not the actuation
path. Every implementation still emits `Order`s through `OrderProcessor`, per the P2 actuation
decision in [skirmish-ai.md §1.3](skirmish-ai.md#13-how-the-ai-issues-actions-decision-orders-via-orderprocessor), so reproducibility (A2) and the recorded/replayed order stream stay intact.

---

## 2. Interface shape (design, not implementation)

The seam sits where `AIPlayer.OnDecisionTick()` sits today: a coarse-cadence callback, driven
from `AIPlayer.Update()`, that reads game state and enqueues `Order`s. Today it is a
`protected virtual` hook overridden by `SkirmishAIPlayer`; this seam turns it into an injectable
strategy so a policy is a pluggable collaborator instead of a base-class override:

```csharp
// Illustrative — not implemented by this doc.
public interface IAIDecisionPolicy
{
    // Called at the AI's decision cadence (AIPlayer.DefaultDecisionCadenceFrames).
    // Reads whatever state it needs from the context; returns orders to enqueue.
    // Must not mutate game state directly — same actuation-via-orders constraint as P2 (§2.3
    // of skirmish-ai.md) so every policy stays replay/record-compatible and A2-reproducible.
    IReadOnlyList<Order> DecideNextOrders(AIDecisionContext context);
}

// Read-only view of the state a policy is allowed to consume. Wraps Player/GameLogic/AssetStore
// access the reference implementation already uses (Owner.BankAccount, Owner.OwnedObjects,
// Owner.Enemies, AssetStore.AIData, ...) behind one seam so implementations aren't coupled to
// engine internals more broadly than necessary, and so a future non-engine (e.g. batched
// training) context can supply a lighter-weight fake.
public readonly struct AIDecisionContext
{
    public Player Owner { get; }
    public int OwnerPlayerIndex { get; }
    public int DecisionTickCount { get; }
    // ... read accessors mirroring what SkirmishAIPlayer already reads (§1.1 of skirmish-ai.md).
}
```

`AIPlayer` becomes a thin host: it owns the tick cadence and dispatches to an
`IAIDecisionPolicy` instead of relying on subclass overrides. `SkirmishAIPlayer`'s existing state
machine from [skirmish-ai.md §1.1](skirmish-ai.md#11-add-a-strategic-decision-loop-to-the-skirmish-ai) becomes the reference implementation, either as a
`RuleBasedAIDecisionPolicy` or via a refactored `SkirmishAIPlayer` that holds one — proving the
seam is real per the A4 definition ([seams.md](../seams.md#what-is-a-seam)).

### 2.1 Selection

Policy selection is config-driven, matching other A4 seams. A `PlayerAIType`-adjacent setting in
the skirmish slot config, which already drives `AIPlayer` vs `SkirmishAIPlayer` selection in
`Player.CreateNew`, grows a policy identifier. A small factory maps that identifier to an
`IAIDecisionPolicy`, and no caller of `AIPlayer` needs to know which policy is running.

### 2.2 Rationale for this shape

- It matches the existing actuation boundary exactly: orders via `OrderProcessor`. The seam is
  additive over P2's actuation decision, not a re-litigation of it.
- It keeps the interface narrow: one method and one read-only context. A neural-network
  implementation then needs only a private state encoding and order-decoding step, not engine
  knowledge about build lists, prerequisites, or unit kind flags.
- It keeps cadence, persistence (`AIPlayer.Persist`), and dispatch (`EnqueueOrder(s)`) in the
  host (`AIPlayer`) instead of duplicating them per policy.

---

## 3. What this unblocks: a neural-network policy

A neural-network implementation of `IAIDecisionPolicy` is the motivating case:

- State encoding: `AIDecisionContext` → a fixed-size feature vector (economy state, owned unit
  counts by kind, enemy visibility, build-list progress, ...). This encoding is itself an
  implementation detail of the NN policy, not part of the seam.
- Network: a small feed-forward network (weights = the evolvable genome) mapping the feature
  vector to a fixed action space (build/produce/attack decisions), decoded back into the same
  `Order`s a human or the rule-based policy would issue.
- Determinism: because actuation is still orders through `OrderProcessor`, a match run with a
  given weight vector and a given map/seed is exactly as reproducible as today's rule-based
  games (A2) — the network's forward pass is a pure function of its weights and the encoded
  state, with no additional nondeterminism to audit beyond what A2 already covers.

## 4. What this unblocks: evolutionary training via seeded weight populations

The user's training goal — generate multiple seed weight vectors and evolve them through repeated
simulation runs — maps directly onto the [A4 experiment harness](../track-a-foundations.md), which
already selects a seam implementation, runs it, and reports results. [A5](visualization.md)
visualizes those results. Applied here:

1. Population — a set of weight vectors (genomes), one `IAIDecisionPolicy` instance per genome.
2. Fitness — a full headless match per genome (via the [P0 harness](playable-baseline-harness.md)
   and [A1 headless host](headless-host.md)), scored from the already-first-class
   [`GameOutcome`](victory-defeat.md) (win/loss/score) plus auxiliary metrics (economy growth,
   territory held, survival time) as a shaped fitness signal.
3. Selection/variation — standard evolutionary-strategy moves (truncation/tournament selection,
   Gaussian mutation, optional crossover) produce the next generation's weight vectors.
4. Repeat — generations run as an A4 experiment sweep (matching the parameter-matrix mechanism
   [A5](visualization.md) already drives for scenario authoring), with per-generation results
   (best/mean/worst fitness, diversity) reported to the visualizer so evolution progress across
   seeds is inspectable, not just the final result.

This doc does not design the training harness in detail; that follows the seam (§6). The seam is
shaped so the harness drops in cleanly: running N genomes across M seeded matches and collecting
fitness requires no change to `AIPlayer`, `OrderProcessor`, or `GameOutcome`. It only requires a
way to construct an `IAIDecisionPolicy` from a weight vector and assign it to a player slot, which
§2.1's factory provides.

---

## 5. Integration surface

- Host: `AIPlayer.OnDecisionTick()` becomes a dispatch to an injected `IAIDecisionPolicy`
  instead of a `protected virtual` override point.
- Reference implementation: `SkirmishAIPlayer`'s existing economy/build-out/production/military
  state machine (skirmish-ai.md §1.1), refactored behind the interface, no behavior change.
- Selection: a policy-identifier config value (alongside the existing skirmish/`PlayerAIType`
  slot configuration in `Player.CreateNew`) resolved by a small factory.
- Actuation: unchanged — every policy still emits `Order`s via `OrderProcessor` (skirmish-ai.md
  §2.3); this seam does not touch that decision.
- Persistence: unchanged in shape — `AIPlayer.Persist`/`SkirmishAIPlayer.Persist` keep owning
  save/load; a stateful policy (e.g. a NN's activations, if ever persisted) would extend that
  same path, not a new one.

---

## 6. Follow-on work (not in this doc)

This doc formalizes the seam only. Follow-up work, each large enough for its own task-card set:

- Extract `IAIDecisionPolicy`/`AIDecisionContext` and refactor `SkirmishAIPlayer` to implement
  it as the reference policy, with no behavior change (verified by the existing P2 tests:
  `SkirmishAIEconomyBuildoutTests`, `SkirmishAIMilitaryBehaviorTests`,
  `SkirmishAIUnitProductionTests`, `SkirmishAIPlayerTickTests`, and the P0-5b scenario matrix in
  skirmish-ai.md §6).
- Add the policy-selection config plumbing (§2.1) and a factory, defaulting to the rule-based
  policy so existing skirmish setups are unaffected.
- Design and implement a first neural-network `IAIDecisionPolicy` (state encoding, network
  shape, order decoding).
- Design and implement the evolutionary training harness (§4) as an A4 experiment: population
  management, fitness scoring off `GameOutcome`, selection/mutation, per-generation reporting
  to [A5](visualization.md).

---

## 7. Related
- Seam registry entry: [`../seams.md`](../seams.md) (Experiment seams (A4) table).
- Reference implementation this seam is carved from: [`skirmish-ai.md`](skirmish-ai.md) (P2).
- Fitness signal: [`victory-defeat.md`](victory-defeat.md) (`GameOutcome`, P3).
- Training-run execution surface: [`headless-host.md`](headless-host.md) (A1),
  [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0).
- Sweep/reporting mechanism the training harness would reuse: [`visualization.md`](visualization.md) (A5).
- Reproducibility guarantee every policy inherits: [open-questions.md](../open-questions.md)
  (relaxed-determinism Decision), [Track A A2](../track-a-foundations.md).

---

## 8. Architecture diagrams

Grounded in the current code (`src/OpenSage.Game/Logic/AI/AIPlayer.cs`,
`src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`, `src/OpenSage.Game/Logic/Player.cs`,
`src/OpenSage.Game/Logic/Orders/OrderProcessor.cs`). Class names ending `(planned)` do not exist
yet; everything else is the type/member as it stands today.

```mermaid
classDiagram
  class Player {
    <<engine, existing>>
    +AIPlayer? AIPlayer
    +LogicTick()
    +CreateNew(...)$ Player
  }

  class AIPlayer {
    <<host, existing — becomes thin dispatcher>>
    -Player _owner
    -uint _countdownSomething1
    +Update()
    #OnDecisionTick() virtual
    #EnqueueOrder(Order)
    #EnqueueOrders(IReadOnlyList~Order~)
    #DispatchOrders(IReadOnlyList~Order~) virtual
    +Persist(StatePersister)
  }

  class IAIDecisionPolicy {
    <<interface, planned>>
    +DecideNextOrders(AIDecisionContext) IReadOnlyList~Order~
  }

  class AIDecisionContext {
    <<readonly struct, planned>>
    +Player Owner
    +int OwnerPlayerIndex
    +int DecisionTickCount
  }

  class RuleBasedAIDecisionPolicy {
    <<planned — extracted from SkirmishAIPlayer>>
    -RunEconomyAndBaseBuildout()
    -RunUnitProduction()
    -RunMilitaryBehavior()
  }

  class SkirmishAIPlayer {
    <<existing — today's OnDecisionTick override>>
    +int DecisionTickCount
    #OnDecisionTick() override
  }

  class NeuralNetworkAIDecisionPolicy {
    <<future — depends on RuleBasedAIDecisionPolicy landing first>>
    -float[] Weights
    -EncodeState(AIDecisionContext) float[]
    -DecodeActions(float[]) IReadOnlyList~Order~
  }

  class OrderProcessor {
    <<engine, existing>>
    +Process(IEnumerable~Order~)
  }

  Player --> AIPlayer : owns
  AIPlayer --> IAIDecisionPolicy : dispatches to (planned)
  IAIDecisionPolicy <|.. RuleBasedAIDecisionPolicy
  IAIDecisionPolicy <|.. NeuralNetworkAIDecisionPolicy
  IAIDecisionPolicy ..> AIDecisionContext : reads
  SkirmishAIPlayer --|> AIPlayer : today's subclass (pre-refactor)
  RuleBasedAIDecisionPolicy ..> SkirmishAIPlayer : replaces subclass override with policy
  AIPlayer ..> OrderProcessor : EnqueueOrder(s) -> DispatchOrders -> ... -> Process(...)
```

Sequence — today's dispatch path (`AIPlayer.Update()` → `OnDecisionTick()` override), and the
planned path once `IAIDecisionPolicy` lands:

```mermaid
sequenceDiagram
  participant PM as PlayerManager
  participant P as Player
  participant AP as AIPlayer
  participant Pol as IAIDecisionPolicy (planned)
  participant OP as OrderProcessor

  PM->>P: LogicTick()
  P->>AP: Update()
  Note over AP: countdown reaches 0 (DefaultDecisionCadenceFrames)
  AP->>Pol: DecideNextOrders(AIDecisionContext)
  Pol-->>AP: IReadOnlyList~Order~
  AP->>AP: EnqueueOrders(orders)
  AP->>OP: DispatchOrders -> NetworkMessageBuffer.AddLocalOrder / Game.InjectOrders -> Process(...)
```

Today, `Pol` does not exist: `AIPlayer.OnDecisionTick()` is a `protected virtual` hook and
`SkirmishAIPlayer` overrides it directly, calling `RunEconomyAndBaseBuildout()` /
`RunUnitProduction()` / `RunMilitaryBehavior()` inline instead of through a policy object. The
first task card ([`WI-A4-01`](../agent/cards/WI-A4-01-extract-ai-decision-policy-interface.md))
introduces `Pol` without changing this sequence's observable behavior.

---

## Appendix: Current code (agent context)

> Agent context: file:line references for agents implementing this phase. Human readers: the
> design intent above does not require reading this section.

- `AIPlayer.OnDecisionTick()` is the current override point (`protected virtual`), dispatched
  from `AIPlayer.Update()` on a `DefaultDecisionCadenceFrames`-frame cadence
  (`src/OpenSage.Game/Logic/AI/AIPlayer.cs`).
- `SkirmishAIPlayer.OnDecisionTick()` overrides it with the reference state machine — economy
  (`RunEconomyAndBaseBuildout`), production (`RunUnitProduction`), military
  (`RunMilitaryBehavior`) — reading `Owner.BankAccount`, `Owner.OwnedObjects`, `Owner.Enemies`,
  `Game.AssetStore.AIData` (`src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`).
- Actuation: `AIPlayer.EnqueueOrder(s)` → `DispatchOrders(...)` →
  `NetworkMessageBuffer.AddLocalOrder` (networked) or `Game.InjectOrders` (local), both ultimately
  processed by `OrderProcessor.Process(...)` (`Logic/Orders/OrderProcessor.cs`) — the actuation
  path every future policy must keep using.
- Selection point: `Player.CreateNew(...)` chooses `SkirmishAIPlayer` vs. `AIPlayer` vs. `null`
  based on `isHuman`/`isSkirmish`/`side` (`src/OpenSage.Game/Logic/Player.cs`) — this is where a
  policy-identifier config value would plug in (§2.1).
- Fitness data source for future training: `GameOutcome` (victory-defeat.md, P3) — not yet
  landed at time of writing; the training harness (§4) depends on it existing and being
  queryable headlessly.
