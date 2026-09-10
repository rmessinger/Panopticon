#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>One generation's outcome: its report and the advanced population.</summary>
public sealed record NeuralGenerationResult(
    NeuralGenerationReport Report,
    NeuralPopulation NextPopulation);

/// <summary>
/// Runs one neural-policy generation (WI-A4-15, design §4): evaluates every genome against
/// every configured match seed, aggregates fitness, ranks, computes diversity, advances the
/// population, and emits the machine-readable generation report. Failures are explicit:
/// failed matches carry their error, failed genomes get no fitness and rank last, and the
/// report is still produced.
/// </summary>
public sealed class NeuralPolicyEvolutionRunner
{
    private readonly INeuralPolicyMatchEvaluator _evaluator;

    public NeuralEvolutionConfig Config { get; }

    public NeuralPolicyEvolutionRunner(INeuralPolicyMatchEvaluator evaluator, NeuralEvolutionConfig config)
    {
        config.Validate();
        _evaluator = evaluator;
        Config = config;
    }

    public NeuralGenerationResult RunGeneration(NeuralPopulation population, int generation)
    {
        if (population.Genomes.Count != Config.PopulationSize)
        {
            throw new InvalidOperationException(
                $"RunGeneration expects {Config.PopulationSize} genomes; got {population.Genomes.Count}.");
        }

        var report = new NeuralGenerationReport
        {
            ExperimentSeed = Config.ExperimentSeed,
            Generation = generation,
            PopulationSize = Config.PopulationSize,
        };

        foreach (var genome in population.Genomes)
        {
            var entry = new NeuralGenomeReport { Index = genome.Index };
            var results = new List<NeuralFitnessResult?>();
            foreach (var (slot, matchSeed) in MatchSeeds(generation, genome.Index))
            {
                var (match, fitness) = EvaluateMatch(genome, matchSeed);
                entry.Matches.Add(match);
                results.Add(fitness);
            }

            Aggregate(entry, results);
            report.Genomes.Add(entry);
        }

        Rank(report.Genomes);
        report.Summary = Summarize(population, report.Genomes);
        AttachMatchSeeds(report);

        var advance = NeuralEvolutionOperators.Advance(
            ToRanked(report.Genomes, population),
            Config,
            (uint)generation);
        report.NextGeneration = advance.Ancestry
            .Select(a => new NeuralNextGenomeReport { Index = a.Index, ParentIndex = a.ParentIndex, Elite = a.Elite })
            .ToList();

        return new NeuralGenerationResult(report, advance.Next);
    }

    private IEnumerable<(int Slot, uint Seed)> MatchSeeds(int generation, int genomeIndex)
    {
        for (var slot = 0; slot < Config.MatchSeedCount; slot++)
        {
            yield return (slot, NeuralEvolutionOperators.MatchSeed(Config, (uint)generation, genomeIndex, slot));
        }
    }

    private (NeuralMatchReport Report, NeuralFitnessResult? Fitness) EvaluateMatch(
        NeuralGenome genome, uint matchSeed)
    {
        var match = new NeuralMatchReport { Seed = matchSeed };
        try
        {
            var evaluation = _evaluator.Evaluate(genome.Index, genome.Weights, matchSeed);
            var fitness = AIFitnessScorer.Score(evaluation.Output, evaluation.GenomePlayerName, Config.MatchFrameCap);
            match.Fitness = fitness.Total;
            match.Outcome = fitness.OutcomeLabel;
            match.Frames = evaluation.Output.TotalLogicFrames;
            return (match, fitness);
        }
        catch (Exception ex)
        {
            match.Failed = true;
            match.Error = ex.Message;
            return (match, null);
        }
    }

    private static void Aggregate(NeuralGenomeReport entry, IReadOnlyList<NeuralFitnessResult?> results)
    {
        var successful = results.OfType<NeuralFitnessResult>().ToList();
        entry.Failed = successful.Count != results.Count;
        if (entry.Failed || successful.Count == 0)
        {
            entry.Fitness = null;
            return;
        }

        entry.Fitness = successful.Average(f => f.Total);
        entry.Terms = new NeuralFitnessTermsReport
        {
            Outcome = successful.Average(f => f.OutcomeTerm),
            Economy = successful.Average(f => f.EconomyTerm),
            Army = successful.Average(f => f.ArmyTerm),
            Speed = successful.Average(f => f.SpeedTerm),
        };
    }

    // Ranks successful genomes by mean fitness descending, ties by lower index; failed
    // genomes follow in index order without a rank.
    private static void Rank(IReadOnlyList<NeuralGenomeReport> entries)
    {
        var rank = 1;
        foreach (var entry in entries
                     .Where(e => !e.Failed)
                     .OrderByDescending(e => e.Fitness!.Value)
                     .ThenBy(e => e.Index))
        {
            entry.Rank = rank++;
        }
    }

    private static NeuralGenerationSummary Summarize(NeuralPopulation population, IReadOnlyList<NeuralGenomeReport> entries)
    {
        var fitnesses = entries.Where(e => e.Fitness is not null).Select(e => e.Fitness!.Value).ToList();

        return new NeuralGenerationSummary
        {
            Best = fitnesses.Count > 0 ? fitnesses.Max() : null,
            Mean = fitnesses.Count > 0 ? fitnesses.Average() : null,
            Worst = fitnesses.Count > 0 ? fitnesses.Min() : null,
            Diversity = Diversity(population),
        };
    }

    private static double Diversity(NeuralPopulation population)
    {
        var genomes = population.Genomes;
        var total = 0d;
        var pairs = 0;
        for (var i = 0; i < genomes.Count; i++)
        {
            for (var j = i + 1; j < genomes.Count; j++)
            {
                total += Distance(genomes[i].Weights, genomes[j].Weights);
                pairs++;
            }
        }

        return pairs > 0 ? total / pairs : 0d;
    }

    private static double Distance(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var sum = 0d;
        for (var i = 0; i < a.Count; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static void AttachMatchSeeds(NeuralGenerationReport report)
    {
        var representative = report.Genomes.FirstOrDefault(e => e.Index == 0);
        if (representative is null)
        {
            return;
        }

        report.MatchSeeds.AddRange(representative.Matches.Select(m => m.Seed));
    }

    // Ranked order for advancement: successful genomes by rank, then failed genomes (last).
    private static IReadOnlyList<RankedGenome> ToRanked(
        IReadOnlyList<NeuralGenomeReport> entries, NeuralPopulation population)
    {
        var byIndex = population.Genomes.ToDictionary(g => g.Index);
        return entries
            .OrderBy(e => e.Rank ?? int.MaxValue)
            .ThenBy(e => e.Index)
            .Select(e => new RankedGenome(byIndex[e.Index], e.Fitness, e.Failed))
            .ToList();
    }
}
