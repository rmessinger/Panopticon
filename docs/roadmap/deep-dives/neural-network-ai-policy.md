# Deep Dive: Neural Network AI Decision Policy (v1)

> Design produced by `WI-A4-03`; implemented (inference only) by `WI-A4-04` as
> `NeuralNetworkAIDecisionPolicy` behind the [pluggable AI seam](a4-pluggable-ai.md).
>
> Audience: the concrete encoding/network/decoding contract. Anything changing the numbers
> here changes the meaning of every genome and must be a new design revision, not an edit.

Purpose: define the first non-rule-based `IAIDecisionPolicy`: a fixed-weight feed-forward
network whose weight vector is the evolvable genome of the future training harness
([pluggable-ai.md §4](a4-pluggable-ai.md#4-what-this-unblocks-evolutionary-training-via-seeded-weight-populations)).
This document is the contract the implementation follows exactly.

---

## 1. State encoding

`AIDecisionContext` maps to a fixed feature vector of 9 floats, each normalized to [0, 1].
Feature indices are stable; append-only if ever extended.

| Index | Feature | Definition | Normalization |
|---|---|---|---|
| 0 | cash level | `Owner.BankAccount.Money` | `min(log(1 + cash) / log(1 + 20000), 1)` |
| 1 | attack force ratio | count of non-structure, `CanAttack`, non-harvester, non-dozer owned objects | `min(count / 8, 1)` |
| 2 | structure ratio | count of owned structures | `min(count / 20, 1)` |
| 3 | harvester ratio | count of non-structure `ObjectKinds.Harvester` owned objects | `min(count / 4, 1)` |
| 4 | dozer ratio | count of non-structure `ObjectKinds.Dozer` owned objects | `min(count / 2, 1)` |
| 5 | enemy attacker seen | 1 if any enemy owns a non-structure `CanAttack` object, else 0 | binary |
| 6 | enemy structure seen | 1 if any enemy owns a structure, else 0 | binary |
| 7 | supply center owned | 1 if owner has a structure with `ObjectKinds.SupplyGatheringCenter`, else 0 | binary |
| 8 | idle harvester ratio | count of non-structure harvesters whose `SupplyAIUpdate` has no current source or target | `min(count / 4, 1)` |

Cash normalization saturates at 20000, the scale of a supply center build cost, so the
network sees order-of-magnitude wealth rather than raw amounts.

## 2. Network shape

Feed-forward, fully connected, one hidden layer:

- Input: 9 (the feature vector above).
- Hidden: 8 units, `tanh` activation.
- Output: 5 logits, softmax. The decision is the argmax; softmax is monotone in the logits,
  so the implementation takes the argmax over logits directly. Ties resolve to the lowest
  index.

Total parameters: `9*8 + 8 + 8*5 + 5 = 125`. A population of thousands of genomes is a few
hundred kilobytes; a forward pass is 157 multiply-adds, negligible against a 60-frame
decision cadence. This sizing is deliberately minimal so evolution over populations stays
cheap (the genome-as-weights constraint in pluggable-ai.md §4).

### 2.1 Weight layout (the genome)

A genome is a flat `float[125]`, laid out in four consecutive blocks:

| Block | Indices | Layout |
|---|---|---|
| W1 | 0..71 | grouped by hidden unit `j`: `weights[j * 9 + i]` is input `i` to hidden `j` |
| b1 | 72..79 | `weights[72 + j]` is the bias of hidden unit `j` |
| W2 | 80..119 | grouped by output `k`: `weights[80 + k * 8 + j]` is hidden `j` to output `k` |
| b2 | 120..124 | `weights[120 + k]` is the bias of output `k` |

Construction rejects any other length with an explicit exception; weights are cloned, never
aliased.

Forward pass: `h_j = tanh(b1_j + Σ_i W1[i,j] * x_i)`, then `z_k = b2_k + Σ_j W2[j,k] * h_j`.

## 3. Action space and decoding

Five discrete actions, one per output index. Each decision tick emits at most one order
group for the argmax action; every action decodes to the same `Order` shapes the rule-based
policy issues, reusing its selection helpers (`RuleBasedAIDecisionPolicy.SelectBuildList`,
`TryCreateBuildOrder`, `TryCreateUnitProductionOrder`, `TrySelectAttackForce`,
`FindDefensiveTarget`/`FindOffensiveTargetStructure`, `CreateAttackOrders`) so targeting
semantics cannot drift between policies.

| Index | Action | Decoding |
|---|---|---|
| 0 | None | no orders |
| 1 | BuildSupplyPriority | first buildable, unowned, affordable supply-flagged build-list structure, as a `BuildObject` order |
| 2 | BuildNextStructure | same selection without the supply filter |
| 3 | ProduceCombatUnit | first queueable combat unit from a producer's command set, as a `CreateUnit` order |
| 4 | AttackNearestEnemy | if the attack force meets the threshold (3), select it and attack the first enemy attacker, else the first enemy structure |

### 3.1 Descopes from the rule-based policy (v1)

The rule-based policy covers three decision categories per tick; v1 addresses each:

- Economy/build-out: covered by actions 1 and 2, one build per tick instead of up to one
  per category.
- Production: covered by action 3 with identical queueability predicates.
- Military: covered by action 4; the defensive/offensive distinction collapses to the same
  target priority order (attacker first, then structure).
- Idle harvester reassignment (gather orders) is descoped: no v1 action emits supply gather
  orders. Harvesters assigned at map start keep working; reassignment returns with a future
  action index if evolution needs it.

"Nearest" in the action name is nominal: target selection is first-enumerated, matching the
rule-based reference; spatial targeting is future work if fitness demands it.

## 4. Randomness

None. Encoding, forward pass, and argmax are deterministic functions of the context and the
weights; tie-breaks are fixed (lowest index). A run's behavior is therefore reproducible
from (seed, weights) with no additional policy RNG.

## 5. Open questions

- Whether v2 should expand the action space (gather assignment, special powers) or add
  continuous outputs (target offsets) is deferred to the training harness's fitness results;
  logged in [open-questions.md](../open-questions.md).
- Whether the feature vector should include enemy economy estimates (currently only binary
  presence) depends on what fitness pressure reveals; also logged there.

## 6. Related

- Seam and factory: [pluggable-ai.md](a4-pluggable-ai.md)
- Reference policy: `RuleBasedAIDecisionPolicy` (`src/OpenSage.Game/Logic/AI/`)
- Implementation: `NeuralNetworkAIDecisionPolicy` (`src/OpenSage.Game/Logic/AI/`)
- Tests: `src/OpenSage.Game.Tests/Logic/AI/NeuralNetworkAIDecisionPolicyTests.cs` (hand-computed
  forward pass, controlled-state encoding, bias-driven action selection through the full
  `AIPlayer` dispatch path)
