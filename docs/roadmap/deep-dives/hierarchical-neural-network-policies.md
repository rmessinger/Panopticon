# Deep Dive: Hierarchical Neural Network Policies (A4-3e)

> Designed by `WI-A4-24`; implemented by `WI-A4-25`..`28`. Contract style follows
> [neural-network-ai-policy.md](neural-network-ai-policy.md) (the flat policy).
>
> This document is the frozen per-rank genome contract. Changing any table changes the
> meaning of every evolved genome and must be a new revision.

All three networks are feed-forward with one hidden layer of `tanh` units and argmax-over-
logits decoding (softmax is monotone, so the decision is the argmax; ties resolve to the
lowest output index). Weight vectors are flat genomes laid out as
`[W1 (per hidden unit) | b1 | W2 (per output unit) | b2]`, exactly like the flat policy.
Construction clones and validates the length; any other length throws `ArgumentException`.
No rank uses RNG: inference is a deterministic function of (context, weights).

Rank size constants (single source of truth, on the policy classes):

| Rank | Inputs | Hidden | Outputs | Genome length |
|---|---|---|---|---|
| Soldier | 8 | 6 | 3 | 8*6+6+6*3+3 = 75 |
| Commander | 14 | 8 | 4 | 14*8+8+8*4+4 = 156 |
| General | 9 | 8 | 4 | 9*8+8+8*4+4 = 116 |

## 1. Soldier network (per unit)

Input features (all normalized to [0, 1]):

| Index | Feature | Definition |
|---|---|---|
| 0 | own health fraction | `Health / MaxHealth`, 0 when the unit has no body module |
| 1 | goal has target | 1 when the goal's target id is valid, else 0 |
| 2 | goal has destination | 1 when the goal's destination is set, else 0 |
| 3 | goal stance | `Hold` 0.0, `Advance` 0.5, `Retreat` 1.0 |
| 4 | distance to goal target | `min(distance(unit, targetPosition) / 1000, 1)`; 1 when there is no target |
| 5 | distance to goal destination | `min(distance / 1000, 1)`; 1 when there is no destination |
| 6 | target relative X | `clamp((target.X - unit.X) / 500, -1, 1)` mapped to [0, 1] as `(v + 1) / 2`; 0.5 when there is no target |
| 7 | target relative Y | same for Y |

Target positions resolve through the unit's game logic by object id; an id that no longer
resolves (dead target) makes features 4-7 take their no-target values.

Actions:

| Index | Action | Decoding |
|---|---|---|
| 0 | hold | no order |
| 1 | attack goal target | force-attack order on the goal target; falls back to hold when the goal has no valid target (invalid input behavior) |
| 2 | move to goal destination | move order to the goal destination; falls back to hold when there is none |

## 2. Commander network (per squad)

One squad-level pass produces a uniform goal template applied to every live member in
stable id order (the rule-based reference's shape: one directive, one goal for all). The
output space therefore stays bounded regardless of squad size.

Input features (all [0, 1]):

| Index | Feature | Definition |
|---|---|---|
| 0 | directive posture | `Attack` 0.0, `Defend` 0.5, `Regroup` 1.0 |
| 1 | directive has target | 1/0 |
| 2 | member count ratio | `members / 8` |
| 3 | average health fraction | mean over members, 0 when empty |
| 4 | squad spread | `min(max distance from centroid / 500, 1)` |
| 5..12 | member health slots | health fraction of the i-th member in id order; 0 padding beyond the live count |

Bounds: squads hold at most 8 members (`SquadHierarchy.MaxSquadSize`); the encoding covers
all of them. A squad presented with more than 8 members throws (the commander contract's
existing oversize guard).

Actions:

| Index | Action | Decoding |
|---|---|---|
| 0 | hold at centroid | goal `Destination = centroid, Stance = Hold` |
| 1 | attack directive target | goal `TargetId = directive.TargetId, Advance`; falls back to the directive's region when the directive has no target; falls back to hold when it has neither |
| 2 | move to directive region | goal `Destination = directive.TargetRegion, Advance`; falls back to hold when there is no region |
| 3 | retreat to centroid | goal `Destination = centroid, Stance = Retreat` |

## 3. General network (per player)

Input features (all [0, 1]):

| Index | Feature | Definition |
|---|---|---|
| 0 | cash level | `min(log(1 + money) / log(1 + 20000), 1)` |
| 1 | total squad units ratio | `sum of squad member counts / 24`, capped at 1 |
| 2 | average squad health | mean of the squads' average health fractions, 0 when there are no squads |
| 3 | enemy attacker seen | 1 when any enemy owns a non-structure `CanAttack` object, else 0 |
| 4 | enemy structure seen | 1 when any enemy owns a structure, else 0 |
| 5..8 | squad summary slots (4) | for the first four squads in id order: `(memberCount/8) * 0.5 + averageHealthFraction * 0.5`; 0 padding beyond the squad count |

Bounds: the slot encoding covers the first 4 squads; a player with more squads encodes the
first 4 by id order (documented overflow) while the decoded directive still applies to every
squad (uniform template), so no squad is left without a directive.

Actions:

| Index | Action | Decoding |
|---|---|---|
| 0 | attack defensive target | directive `Attack` targeting the first enemy attacker in the player's base (the flat policy's defensive-first priority); falls back to action 1's target when there is none |
| 1 | attack offensive structure | directive `Attack` targeting the first enemy structure; falls back to `Defend` when there is none |
| 2 | defend | directive `Defend`, no target |
| 3 | regroup | directive `Regroup`, no target |

## 4. Hand-computable fixture (used by WI-A4-28)

Zero genomes (all weights zero) make every logits vector uniform, so each rank decodes its
lowest index: the general emits defend directives, the commander holds at centroid, the
soldier holds. A b2-only genome on any rank picks that rank's action regardless of inputs.
Each b2 block starts after `W1 + b1 + W2` (soldier 48+6+18 = 72, commander 112+8+32 = 152,
general 72+8+32 = 112), so: soldier weights with `weights[73] = 5` (b2[1], attack) attack
the goal target; commander weights with `weights[153] = 5` (b2[1]) attack the directive
target; general weights with `weights[112] = 5` (b2[0]) emit the defensive-first attack
directive.

## 5. Genome set and selection

A hierarchical neural policy is constructed from a `HierarchicalRankWeights` record
(`Soldier`, `Commander`, `General` vectors) on `AIDecisionPolicyPayload`; the factory builds
`HierarchicalAIDecisionPolicy` with the three neural rank policies, validating every length
(75, 156, 116). Without rank weights the factory builds the rule-based reference hierarchy.
Each genome is used only by its own rank.

## 6. Related

- Seam and rank contracts: [a4-decentralized-hierarchical-ai.md](a4-decentralized-hierarchical-ai.md)
- Flat network contract this follows: [neural-network-ai-policy.md](neural-network-ai-policy.md)
- Co-evolution design: [a4-decentralized-hierarchical-ai.md](a4-decentralized-hierarchical-ai.md) §6 (WI-A4-29)
