#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model;

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
