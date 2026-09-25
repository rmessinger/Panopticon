# Task Card `WI-A4-03`: Design the first neural-network `IAIDecisionPolicy`

| Field | Value |
|---|---|
| Card ID | `WI-A4-03` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI decision policy seam, §3/§4 of [pluggable-ai.md](../../deep-dives/a4-pluggable-ai.md) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-02` (Done): the seam and factory must exist so the design has a concrete interface/context to encode against |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Produce a concrete, written design (not code) for the first `NeuralNetworkAIDecisionPolicy`:
the `AIDecisionContext` → feature-vector encoding, the network shape (layer sizes, activation),
and the fixed action space → `Order` decoding. This is the design the roadmap explicitly defers
(pluggable-ai.md: *"This doc designs the seam only... see §6 for the follow-on work"*): write
it up before any implementation card touches code, per the guardrail against inventing design
inline in a code card.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-pluggable-ai.md` §3 (what this unblocks: NN policy) and §4
  (evolutionary training): the shape and constraints this design must satisfy.
- `src/Panopticon.Simulation/Logic/AI/AIDecisionContext.cs` (added by `WI-A4-01`): the actual read
  surface a state encoding has to work from.
- `src/Panopticon.Simulation/Logic/AI/RuleBasedAIDecisionPolicy.cs` (added by `WI-A4-01`): enumerate
  every decision category the reference policy makes (economy/build-out, production, military)
  so the action space covers an equivalent decision surface, not a smaller one.
- `src/Panopticon.Simulation/Logic/AI/AIDecisionPolicyType.cs`, `AIDecisionPolicyFactory.cs` (added by
  `WI-A4-02`): where the new policy type will register.
- `src/Panopticon.Simulation/Logic/Orders/Order.cs` (or wherever `Order` and its factory/builder helpers
  live): the concrete order shapes the decoder must produce.

## Steps

1. Write a design note (new file `docs/roadmap/deep-dives/neural-network-ai-policy.md`, linked
   from pluggable-ai.md §3 and from `track-a-foundations.md`'s A4 phase-resources table) covering:
   - State encoding: exact fixed-size feature vector derived from `AIDecisionContext`:
     list every feature (e.g. bank cash, per-unit-kind counts, enemy visibility counts, build-list
     progress ratios), its normalization, and the total vector length.
   - Network shape: input size (= feature vector length), hidden layer(s) and sizes,
     activation function(s), output size and layout, output activation. Justify sizing against
     the weight-vector-as-genome constraint from §4 (small enough for a population of genomes to
     be evolved cheaply).
   - Action decoding: how the fixed-size output vector maps back onto concrete `Order`s
     (e.g. one-hot/argmax over a discrete action catalog vs. per-category scalar thresholds):
     must cover the same decision categories as `RuleBasedAIDecisionPolicy` (or explicitly scope
     down and say why, e.g. "military targeting simplified to nearest-enemy-mass for v1").
   - Weight representation: how a genome (`float[]`) maps onto the network's actual weight
     matrices: flat layout, ordering, and total parameter count.
   - Randomness: state whether the policy uses RNG and how the selected seed is supplied.
2. Add a short "Open questions" section for anything genuinely undecided (e.g. whether to expose
   a fixed action catalog vs. continuous outputs) and route those to
   [open-questions.md](../../open-questions.md) rather than deciding them unilaterally in this card.
3. Update `docs/roadmap/deep-dives/a4-pluggable-ai.md` §3 to link the new design doc instead of
   describing the encoding inline, and add the design doc to `track-a-foundations.md`'s A4
   phase-resources table.

## Files likely to change

- `docs/roadmap/deep-dives/neural-network-ai-policy.md` (new)
- `docs/roadmap/deep-dives/a4-pluggable-ai.md` (link update in §3)
- `docs/roadmap/track-a-foundations.md` (phase-resources table row)
- `docs/roadmap/open-questions.md` (if open questions are surfaced)

## Acceptance criteria

- [ ] Design doc states an exact feature-vector length, network shape, and output decoding:
      concrete numbers/layouts, not placeholders.
- [ ] Every decision category `RuleBasedAIDecisionPolicy` covers is addressed by the action
      space (covered or explicitly descoped with a stated reason).
- [ ] Any policy RNG and seed input are documented.
- [ ] Any genuinely undecided question is logged in `open-questions.md`, not silently resolved.
- [ ] This is an investigation/design card. No `.cs` changes, so standard build/test validation
      does not apply; `dotnet format` only applies if any `.cs` sample snippets are added inline
      (avoid adding compiled code here: this card produces a design, not an implementation).

## Validation commands

Read-only / documentation card. No build or test commands apply. Verify the new design doc
renders correctly (valid Markdown/Mermaid) and that all internal links resolve.

## Out of scope / guardrails

- Do not implement `NeuralNetworkAIDecisionPolicy` in code here: that is `WI-A4-04`.
- Do not design the evolutionary training harness in detail here: that is a separate,
  later-unblocked card (depends on the A1 headless host and P3 `GameOutcome`, neither of which
  exist yet at time of writing; see `docs/roadmap/deep-dives/a4-pluggable-ai.md` §4/§6).
- If the feature/action space design turns out to require engine changes beyond
  `AIDecisionContext` as `WI-A4-01` shaped it, stop and mark `Blocked`. Flag the needed context
  extension as a prerequisite rather than redesigning the seam inline.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - `WI-A4-04`: implement `NeuralNetworkAIDecisionPolicy` per this design, register it in
    `AIDecisionPolicyFactory` under a new `AIDecisionPolicyType.NeuralNetwork` member.
  - A later, separately-scoped evolutionary-training-harness card set, once A1 (headless host)
    and P3 (`GameOutcome`) have landed.
