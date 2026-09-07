# Task Card `WI-A4-11`: Design flat neural-policy evolution

| Field | Value |
|---|---|
| Card ID | `WI-A4-11` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-04` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Specify the policy payload, population, evaluation matrix, fitness, variation, and report contracts
needed to evolve the existing 125-float neural policy.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-pluggable-ai.md` §§4 and 6.
- `docs/roadmap/deep-dives/neural-network-ai-policy.md`.
- `src/OpenSage.Game/Logic/AI/` and `Logic/GameOutcome.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`.

## Steps

1. Fix population size/configuration, initialization, elitism/selection, mutation, optional
   crossover, seed pairing, and stopping rules.
2. Define match and shaped fitness only from existing `GameOutcome` and T0 metrics.
3. Define how a genome reaches `AIDecisionPolicyFactory`.
4. Define the per-generation machine-readable report.
5. Update the pluggable-AI deep dive and its diagrams.

## Files likely to change

- `docs/roadmap/deep-dives/a4-pluggable-ai.md`
- `docs/roadmap/implementation/track-a-a4-ai-policy-evolution.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every algorithm choice and serialized field is explicit.
- [ ] Evaluation is repeatable from configuration, generation, genome, and match seeds.
- [ ] Fitness does not require new gameplay instrumentation.
- [ ] The diagrams show payload construction, match evaluation, and generation advancement.

## Validation commands

```sh
cd docs/roadmap
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/a4-pluggable-ai.md -o /tmp/a4-ai.svg
rm -f /tmp/a4-ai*.svg
```

## Out of scope / guardrails

No training code, new network shape, or hierarchical co-evolution.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-12` through `WI-A4-15`.
