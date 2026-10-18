using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-15: serializable form of a <see cref="ScenarioMatrix"/>. The schema version gates
/// incompatible documents; constraints load as serializable terms, never executable delegates.
/// </summary>
public sealed class ScenarioMatrixDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ReplayScenario Scenario { get; set; } = new();

    public IReadOnlyList<SweepAxis> Axes { get; set; } = [];

    public IReadOnlyList<ConstraintFilter> Filters { get; set; } = [];

    public IReadOnlyList<MatrixBinding> Bindings { get; set; } = [];

    public IReadOnlyList<AssertionTemplate> AssertionTemplates { get; set; } = [];

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ScenarioMatrixDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<ScenarioMatrixDocument>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidOperationException($"Scenario matrix '{path}' deserialized to null.");
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Scenario matrix '{path}' uses schema version {document.SchemaVersion}; this build understands {CurrentSchemaVersion}.");
        }

        var matrix = document.ToMatrix();
        var diagnostics = matrix.Validate();
        if (diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"Scenario matrix '{path}' is invalid: {string.Join("; ", diagnostics)}");
        }

        ReplayScenarioLoader.Validate(document.Scenario, path);
        return document;
    }

    public void Save(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));
    }

    public ScenarioMatrix ToMatrix()
    {
        return new ScenarioMatrix
        {
            BaseScenario = Scenario,
            Axes = Axes,
            Filters = Filters,
            Bindings = Bindings,
            AssertionTemplates = AssertionTemplates,
        };
    }

    public static ScenarioMatrixDocument FromMatrix(ScenarioMatrix matrix)
    {
        return new ScenarioMatrixDocument
        {
            Scenario = matrix.BaseScenario,
            Axes = matrix.Axes,
            Filters = matrix.Filters,
            Bindings = matrix.Bindings,
            AssertionTemplates = matrix.AssertionTemplates,
        };
    }
}
