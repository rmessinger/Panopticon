#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>One evaluated match within a generation report.</summary>
public sealed class NeuralMatchReport
{
    public uint Seed { get; set; }

    public double? Fitness { get; set; }

    public string? Outcome { get; set; }

    public uint? Frames { get; set; }

    public bool Failed { get; set; }

    public string? Error { get; set; }
}

/// <summary>Per-term mean fitness across a genome's successful matches.</summary>
public sealed class NeuralFitnessTermsReport
{
    public double Outcome { get; set; }

    public double Economy { get; set; }

    public double Army { get; set; }

    public double Speed { get; set; }
}

/// <summary>One genome's aggregated result in a generation report.</summary>
public sealed class NeuralGenomeReport
{
    public int Index { get; set; }

    /// <summary>1-based rank among successful genomes (fitness descending, ties by lower
    /// index); null for failed genomes, which follow all ranked ones.</summary>
    public int? Rank { get; set; }

    public double? Fitness { get; set; }

    public bool Failed { get; set; }

    public NeuralFitnessTermsReport Terms { get; set; } = new();

    public List<NeuralMatchReport> Matches { get; set; } = new();
}

/// <summary>Ancestry of one next-generation genome.</summary>
public sealed class NeuralNextGenomeReport
{
    public int Index { get; set; }

    public int ParentIndex { get; set; }

    public bool Elite { get; set; }
}

public sealed class NeuralGenerationSummary
{
    public double? Best { get; set; }

    public double? Mean { get; set; }

    public double? Worst { get; set; }

    /// <summary>Mean pairwise L2 distance over the generation's weight vectors.</summary>
    public double Diversity { get; set; }
}

/// <summary>
/// Machine-readable per-generation evolution report (WI-A4-15, design §4): evaluation
/// matrix, per-genome results with per-term fitness, ranking, diversity, and next
/// generation ancestry. Consumed by A5 visualization.
/// </summary>
public sealed class NeuralGenerationReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public uint ExperimentSeed { get; set; }

    public int Generation { get; set; }

    public int PopulationSize { get; set; }

    public List<uint> MatchSeeds { get; set; } = [];

    public List<NeuralGenomeReport> Genomes { get; set; } = [];

    public NeuralGenerationSummary Summary { get; set; } = new();

    public List<NeuralNextGenomeReport> NextGeneration { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static NeuralGenerationReport? FromJson(string json) =>
        JsonSerializer.Deserialize<NeuralGenerationReport>(json, JsonOptions);
}
