using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Loads and saves <see cref="ValidationGroundScenario"/> JSON: from disk, from a string, or from
/// the checked-in standard embedded profile. The scenario model itself stays file-agnostic.
/// </summary>
public static class ValidationGroundScenarioStore
{
    private const string UnitSchemaProperty = "UnitSchema";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(), new Vector3JsonConverter() },
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Serializes the scenario (layout + overrides) to a JSON string.</summary>
    public static string ToJson(ValidationGroundScenario scenario) =>
        JsonSerializer.Serialize(scenario, JsonOptions);

    /// <summary>
    /// Deserializes a scenario from a JSON string; returns a default on a null result. A missing
    /// <c>UnitSchema</c> marker is a legacy game-unit file and its absolute lengths are converted to
    /// meters here, once. An unrecognized marker throws <see cref="UnsupportedUnitSchemaException"/>.
    /// </summary>
    public static ValidationGroundScenario FromJson(string json)
    {
        var scenario = JsonSerializer.Deserialize<ValidationGroundScenario>(json, JsonOptions) ?? new ValidationGroundScenario();
        using var document = JsonDocument.Parse(json, JsonDocumentOptions);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return scenario;
        }

        if (!root.TryGetProperty(UnitSchemaProperty, out var marker))
        {
            ConvertLegacyGameUnitsToMeters(scenario);
            return scenario;
        }

        var schema = marker.ValueKind == JsonValueKind.String ? marker.GetString() : marker.GetRawText();
        if (schema != ValidationGroundScenario.MetricUnitSchema)
        {
            throw new UnsupportedUnitSchemaException(schema);
        }

        return scenario;
    }

    /// <summary>Loads a scenario from JSON, or the standard embedded scenario if the file is absent.</summary>
    public static ValidationGroundScenario Load(string path) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path)) : Standard();

    /// <summary>Saves the scenario (layout + overrides) to JSON for the TestHarness to load.</summary>
    public static void Save(ValidationGroundScenario scenario, string path) =>
        File.WriteAllText(path, ToJson(scenario));

    /// <summary>
    /// The standard validation-ground scenario: the formation, roster, target definitions, and run
    /// values, loaded from the checked-in
    /// <c>Profiles/unit-validation-ground-scenario.json</c> embedded resource. This is the single
    /// source of truth for the standard configuration (no formation values live in source).
    /// </summary>
    public static ValidationGroundScenario Standard()
    {
        var assembly = typeof(ValidationGroundScenarioStore).Assembly;
        var name = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith(".Profiles.unit-validation-ground-scenario.json", StringComparison.Ordinal));
        if (name == null)
        {
            throw new InvalidOperationException(
                "Embedded standard validation-ground scenario not found. "
                + "Ensure Profiles/unit-validation-ground-scenario.json is an embedded resource.");
        }

        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream ?? Stream.Null);
        return FromJson(reader.ReadToEnd());
    }

    private static void ConvertLegacyGameUnitsToMeters(ValidationGroundScenario scenario)
    {
        var layout = scenario.Layout;
        layout.TargetClusterCenter = new Vector3(
            LegacyToMeters(layout.TargetClusterCenter.X),
            LegacyToMeters(layout.TargetClusterCenter.Y),
            LegacyToMeters(layout.TargetClusterCenter.Z));
        layout.TargetClusterRadius = LegacyToMeters(layout.TargetClusterRadius);
        layout.UnitSpacing = LegacyToMeters(layout.UnitSpacing);
        layout.FactionRowOffset = LegacyToMeters(layout.FactionRowOffset);
        layout.TargetApproachDistance = LegacyToMeters(layout.TargetApproachDistance);

        foreach (var placement in scenario.PlacementOverrides)
        {
            placement.X = LegacyToMetersOrNull(placement.X);
            placement.Y = LegacyToMetersOrNull(placement.Y);
            placement.TargetDistance = LegacyToMetersOrNull(placement.TargetDistance);
        }
    }

    private static float LegacyToMeters(float legacyUnits) => (float)UnitConversionPolicy.ScaleLength(legacyUnits);

    private static float? LegacyToMetersOrNull(float? legacyUnits) =>
        legacyUnits.HasValue ? LegacyToMeters(legacyUnits.Value) : null;

    /// <summary>Thrown when a validation-ground scenario declares a <c>UnitSchema</c> this build does not read.</summary>
    public sealed class UnsupportedUnitSchemaException : Exception
    {
        public UnsupportedUnitSchemaException(string? schema)
            : base($"Unsupported validation-ground scenario UnitSchema '{schema}'. Expected '{ValidationGroundScenario.MetricUnitSchema}', or no UnitSchema for legacy game-unit files.")
        {
            UnitSchema = schema;
        }

        public string? UnitSchema { get; }
    }
}
