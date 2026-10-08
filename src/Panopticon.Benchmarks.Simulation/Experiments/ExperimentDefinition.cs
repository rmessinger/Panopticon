using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Benchmarks.Simulation.Experiments;

/// <summary>
/// A typed experiment configuration: a name, the B1 scenario to run, sample settings, and one
/// axis per swept seam. Construction and JSON loading never touch a game installation or the
/// registry; validation against registrations happens in
/// <see cref="ExperimentImplementationRegistry.ValidateDefinition"/>.
/// </summary>
public sealed class ExperimentDefinition
{
    public const string CurrentSchemaVersion = "a4.experiment-definition/1";

    public ExperimentDefinition(
        string name,
        string scenarioId,
        uint seed,
        int warmupFrames,
        int measuredFrames,
        IReadOnlyList<ExperimentAxis> axes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredFrames);
        Ensure.NotEmpty(axes, "At least one axis is required.");

        Name = name;
        ScenarioId = scenarioId;
        Seed = seed;
        WarmupFrames = warmupFrames;
        MeasuredFrames = measuredFrames;
        Axes = axes;
    }

    public string SchemaVersion => CurrentSchemaVersion;

    public string Name { get; }

    public string ScenarioId { get; }

    public uint Seed { get; }

    public int WarmupFrames { get; }

    public int MeasuredFrames { get; }

    public IReadOnlyList<ExperimentAxis> Axes { get; }

    /// <summary>
    /// The cartesian product of all axes in axis order. Cell keys render each binding as
    /// `seam=implementation` joined by `;`, so the same definition always yields the same
    /// keys in the same order.
    /// </summary>
    public IReadOnlyList<ExperimentCell> Expand()
    {
        var cells = new List<ExperimentCell> { new(string.Empty, new Dictionary<string, string>()) };
        foreach (var axis in Axes)
        {
            var next = new List<ExperimentCell>();
            foreach (var cell in cells)
            {
                foreach (var implementation in axis.Implementations)
                {
                    next.Add(Combine(cell, axis.Seam, implementation));
                }
            }

            cells = next;
        }

        return cells;
    }

    private static ExperimentCell Combine(ExperimentCell cell, string seam, string implementation)
    {
        var bindings = new Dictionary<string, string>(cell.Bindings)
        {
            [seam] = implementation,
        };
        var key = string.Join(";", bindings.Select(b => $"{b.Key}={b.Value}"));
        return new ExperimentCell(key, bindings);
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static ExperimentDefinition FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var dto = JsonSerializer.Deserialize<ExperimentDefinitionDto>(json, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.ScenarioId))
            {
                throw new ArgumentException("The JSON does not contain an experiment definition.");
            }

            if (dto.SchemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentException(
                    $"The experiment definition schema version '{dto.SchemaVersion}' is not supported; expected '{CurrentSchemaVersion}'.");
            }

            var axes = (dto.Axes ?? [])
                .Select(a => new ExperimentAxis(
                    a.Seam ?? string.Empty,
                    a.Implementations ?? []))
                .ToList();
            return new ExperimentDefinition(dto.Name, dto.ScenarioId, dto.Seed, dto.WarmupFrames, dto.MeasuredFrames, axes);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The JSON does not contain an experiment definition.", ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class ExperimentDefinitionDto
    {
        public string? SchemaVersion { get; set; }

        public string? Name { get; set; }

        public string? ScenarioId { get; set; }

        public uint Seed { get; set; }

        public int WarmupFrames { get; set; }

        public int MeasuredFrames { get; set; }

        public List<ExperimentAxisDto>? Axes { get; set; }
    }

    private sealed class ExperimentAxisDto
    {
        public string? Seam { get; set; }

        public List<string>? Implementations { get; set; }
    }
}
