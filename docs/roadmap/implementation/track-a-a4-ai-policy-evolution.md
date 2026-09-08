# Implementation Plan: Track A · A4 Neural-Policy Evolution (WI-A4-11..16)

> Checklist item: A4 experiment follow-on (pluggable AI, evolution harness).
> Source of truth: [deep-dives/a4-pluggable-ai.md](../deep-dives/a4-pluggable-ai.md) §4,
> [neural-network-ai-policy.md](../deep-dives/neural-network-ai-policy.md) (125-float genome
> contract). This file is the WI-A4-11 design: every algorithm choice and serialized field the
> implementing cards must follow exactly.

## 1. Policy payload

The typed configuration carried from player settings to the factory:

```csharp
public sealed record AIDecisionPolicyPayload
{
    public AIDecisionPolicyType Type { get; init; } = AIDecisionPolicyType.RuleBased;
    public float[]? NeuralNetworkWeights { get; init; }   // required iff Type == NeuralNetwork
    public static AIDecisionPolicyPayload Default { get; } = new();
}
```

- `PlayerSetting.AIPolicy` becomes `AIDecisionPolicyPayload?`; null is equivalent to
  `Default` (RuleBased), so every existing construction keeps its behavior.
- `Player.FromMapData` and `PlayerManager` thread the payload (replacing the enum);
  `AIDecisionPolicyFactory.Create(AIDecisionPolicyPayload)` is the single mapping point
  (the enum overload remains and delegates, for tests).
- Neural payloads fail explicitly at the factory boundary: null weights or a vector whose
  length is not 125 throws `ArgumentException` (the policy constructor validates and clones;
  payloads are treated as immutable and the factory clones defensively so later mutation of
  the source array cannot affect a constructed policy).

## 2. Population and variation operators (all in ReplaySketch.Core, game-independent)

Configuration (immutable record `NeuralEvolutionConfig`, defaults below):

| Field | Default | Constraint |
|---|---|---|
| `ExperimentSeed` | 1 | any uint |
| `PopulationSize` | 8 | 2..256 |
| `MatchSeedCount` | 2 | ≥ 1 |
| `EliteCount` | 2 | ≥ 1, < PopulationSize |
| `TournamentSize` | 3 | ≥ 2, ≤ PopulationSize |
| `MutationProbability` | 0.1 | (0, 1] |
| `MutationSigma` | 0.25 | > 0 |
| `InitializationSigma` | 0.5 | > 0 |
| `WeightClamp` | 4.0 | > 0 |
| `MaxGenerations` | 10 | ≥ 1 |
| `StopAfterStaleGenerations` | 3 | 0 disables |
| `MatchFrameCap` | 5400 | ≥ 1 (frames; 5400 = 3 min at 30 fps) |
| `MapPath` | alpine assault | real map path for the evaluator |

Invalid settings throw `InvalidOperationException` at operator construction.

Deterministic seed derivation (all arithmetic wrapping uint):

```
mix(a, b, c, d) = (a * 73856093) ^ (b * 19349663) ^ (c * 83492791) ^ (d * 2971215073)
operatorSeed(generation)                  = mix(ExperimentSeed, generation, 0, 0)
matchSeed(generation, genomeIndex, slot)  = mix(ExperimentSeed, generation, genomeIndex + 1, slot + 1)
```

Gaussian sampling: Box-Muller with `u1 = 1.0 - rng.NextDouble()` (open interval (0, 1], so
`log(u1)` is finite) and `u2 = rng.NextDouble()`;
`z = sqrt(-2 * ln(u1)) * cos(2π * u2)`.

Genome model: `NeuralGenome(int Index, float[] Weights)`; weights always exactly 125,
clamped to ±WeightClamp at creation and mutation.

Operators (`NeuralEvolutionOperators`), each consuming `Random` draws in a fixed order:

- Initialization (`Random(ExperimentSeed)`): genomes in index order; within a genome,
  weights in index order; each weight = clamp(z, ±WeightClamp) with
  `InitializationSigma`.
- Advancement (`Random(operatorSeed(generation))`), given genomes ranked by fitness
  (descending; ties broken by lower index):
  1. The top `EliteCount` genomes are copied to the next generation verbatim
     (ancestor = self, elite = true).
  2. Each remaining child, in slot order: tournament selection over ranked genomes
     (winner = best fitness among `TournamentSize` uniform draws; ties by lower index),
     clone the winner, then mutate weight-by-weight in index order: one uniform draw for
     the mutation check; if it is < `MutationProbability`, two more draws produce the
     Box-Muller step scaled by `MutationSigma`; the result is clamped. Unmutated weights
     consume exactly one draw.

Same inputs and seed therefore produce byte-identical next-generation genomes.

## 3. Match evaluation and fitness (card 14)

One match = the genome player (a supplied payload) versus the fixed rule-based reference
AI on the configured map, started with `StartSkirmishOrMultiPlayerGame` and a derived match
seed, stepped by `SimulationHost` until `MatchFrameCap` or a decided outcome; the T0
`RunOutput` (outcome record plus per-player metric samples) is all the scorer reads.

`AIFitnessScorer.Score(RunOutput output, string genomePlayerName)`:

| Term | Value |
|---|---|
| outcome | win (decided, genome in winners) = 100 · sudden death = 50 · frame cap (still in progress) = 40 · loss = 0 |
| economy | `10 * min(finalMoney / 10000, 1)` of the genome player's last sample |
| army | `10 * min(finalOwnedObjects / 30, 1)` of the genome player's last sample |
| speed | win only: `20 * (1 - decidedOnFrame / MatchFrameCap)` |
| total | sum of the four |

Every term is separately visible in the result DTO. Incomplete or incompatible outputs
(no outcome record, no metric samples, or the genome player absent from the samples) throw
`InvalidOperationException`; there is no success-shaped fallback. No new gameplay
instrumentation: only `RunOutcomeRecord` and `RunPlayerMetrics`.

Genome fitness = mean match total across its `MatchSeedCount` seeds.

## 4. Generation runner and report (card 15)

`NeuralPolicyEvolutionRunner(evaluator, config)` with an evaluator interface so operator
and runner tests stay game-independent:

```csharp
public sealed record NeuralMatchResult(uint Seed, RunOutput Output);   // or failure state
public interface INeuralPolicyMatchEvaluator
{
    NeuralMatchEvaluation Evaluate(int genomeIndex, IReadOnlyList<float> genome, uint matchSeed);
}
```

`RunGeneration(population, generation)` performs exactly `PopulationSize × MatchSeedCount`
evaluations (genome-major, seed-slot order), aggregates each genome's mean fitness, ranks
(fitness descending, ties by lower index), computes diversity (mean pairwise L2 distance
over weight vectors), advances the population, and returns a `NeuralGenerationReport`:

```json
{
  "experimentSeed": 1, "generation": 3, "populationSize": 8, "matchSeeds": [80021, 610937],
  "genomes": [
    { "index": 4, "rank": 1, "fitness": 118.3, "elite": false, "parentIndex": 2,
      "terms": { "outcome": 100.0, "economy": 6.2, "army": 8.9, "speed": 3.2 },
      "matches": [ { "seed": 80021, "fitness": 115.1, "outcome": "win", "frames": 2712 },
                    { "seed": 610937, "fitness": 121.5, "outcome": "win", "frames": 2644 } ] }
  ],
  "summary": { "best": 118.3, "mean": 71.4, "worst": 40.2, "diversity": 12.8 }
}
```

A failed evaluation (evaluator throws or returns a failure) is recorded on the match entry
(`"failed": true`, `"error": "..."`), the genome gets `fitness: null` and ranks last with
`"failed": true`; the report is still produced. The report JSON round-trips
(`ToJson`/`FromJson`). The training loop (caller) stops at `MaxGenerations` or after
`StopAfterStaleGenerations` generations without a new best fitness (the runner exposes the
stale count; it does not loop internally).

## 5. Real evaluator and integration (card 16)

`HeadlessNeuralMatchEvaluator : INeuralPolicyMatchEvaluator` (ReplaySketch.Core) wraps the
existing headless path: constructs the `Game` for the installation, starts the skirmish
with `PlayerSetting`s where slot 0 is the genome player
(`AIPolicy = { Type = NeuralNetwork, Weights = genome }`, owner = AI) and slot 1 the
rule-based opponent, runs under `SimulationHost` with a `RunOutputRecorder`, and returns
the completed `RunOutput` plus the genome player's name. Integration tests: a
non-interactive plumbing test (controlled fake evaluator through the full runner + payload
path into `AIDecisionPolicyFactory`) and an install-backed (`Interactive`) smoke test with
a 2-genome population, 1 seed, and a short frame cap, asserting report identity, genome
selection, seeds, and outcomes.

## 6. Guardrails

- No new network shape, no hierarchical ranks, no MAUI UI, no fitness tuning.
- Fitness uses only `RunOutcomeRecord` and `RunPlayerMetrics` (T0).
- Policy implementations remain factory-only constructions (existing source-scan test).
