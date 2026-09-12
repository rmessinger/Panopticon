# Implementation Plan: Track A · A4 Hierarchical AI (WI-A4-17..30)

> Checklist item: A4-3 (decentralized hierarchical AI). Seam and rank contracts:
> [deep-dives/a4-decentralized-hierarchical-ai.md](../deep-dives/a4-decentralized-hierarchical-ai.md);
> per-rank genome contracts:
> [deep-dives/hierarchical-neural-network-policies.md](../deep-dives/hierarchical-neural-network-policies.md).
> This file records the WI-A4-29 co-evolution design the runner (WI-A4-30) implements exactly.

## Co-evolution contract (WI-A4-29)

### Schedule: alternating round-robin

Each generation advances exactly one rank population; the other two ranks are fixed
partners. The advancing rank is `rank(generation) = ranks[generation % 3]` over the fixed
cycle [General, Commander, Soldier], starting with General at generation 0. Joint evolution
of all three populations (and stochastic partner sampling) is explicitly future work.

### Partner sampling

The partners for the two non-advancing ranks are deterministic: each partner is genome
index 0 of that rank's current population. Because advancement copies elites verbatim into
slots 0..EliteCount-1 in rank order, slot 0 is always that rank's best-known genome: the
initialized genome #0 before a rank's first advancement, the top elite afterwards. No
stochastic partner selection, so partner identity is reproducible from the generation
number alone.

### Evaluation and credit assignment

Exactly `PopulationSize x MatchSeedCount` matches per generation. Each match assembles one
full hierarchy: the advancing rank's genome `i` plus the two partner genomes, playing the
rule-based reference opponent on the configured map. All three ranks in a match receive the
same match fitness (shared outcome credit). Per-rank credit assignment (for example, a
commander's squad survival rate) is deferred: it requires per-rank T0 metrics that do not
exist yet, recorded as a prerequisite in [open-questions.md](../open-questions.md).

### Fitness

Match fitness is the flat policy's scorer, unchanged
([implementation/track-a-a4-ai-policy-evolution.md](track-a-a4-ai-policy-evolution.md) §3):
outcome term (win 100 / sudden death 50 / frame cap 40 / loss 0) plus economy, army, and
speed shaping over existing T0 rows. Genome fitness is the mean across its match seeds.

### Reproducibility and seeds

The match seed for one evaluation is the double-mixed value

```
HierarchicalMatchSeed(config, generation, rank, genomeIndex, slot)
  = Mix(Mix(ExperimentSeed, generation, (uint)rank + 1, 0), 0,
        (uint)(genomeIndex + 1), (uint)(slot + 1))
```

over the flat operators' `Mix`. Evaluation order is genome-major, seed-slot order, and
rank populations advance only through the documented operators, so a run is reproducible
from (ExperimentSeed, generation, populations).

### Failure behavior

Per-match failures follow the flat contract: the match entry carries the error, the
advancing genome gets `fitness: null` with `failed: true` and ranks last, and the report is
still produced. Partner genomes cannot fail (they are not evaluated).

### Report fields

The flat `NeuralGenerationReport` gains two additive fields: `advancingRank` (the rank name
advanced this generation) and `partnerGenomeIndices` (per-rank partner slot; the advancing
rank's entry is its own evaluated population). Ancestry, best/mean/worst, and diversity
describe the advancing rank's population; flat reports leave both fields null and remain
byte-compatible in shape.

### Population state and lengths

Three rank populations with the frozen genome lengths (general 116, commander 156, soldier
75). `NeuralEvolutionConfig` grows `GenomeLength` (default 125, the flat policy's) and the
operators/genome model validate against it, so one operator implementation serves every
rank. The runner holds the three populations and returns the updated map each generation;
the training loop (caller) stops on `MaxGenerations` or the stale-best rule exactly as the
flat loop does.
