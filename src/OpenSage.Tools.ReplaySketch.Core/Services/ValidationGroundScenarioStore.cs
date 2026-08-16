using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Loads and saves <see cref="ValidationGroundScenario"/> JSON: from disk, from a string, or from
/// the checked-in standard embedded profile. The scenario model itself stays file-agnostic.
/// </summary>
public static class ValidationGroundScenarioStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(), new Vector3JsonConverter() },
    };

    /// <summary>Serializes the scenario (layout + overrides) to a JSON string.</summary>
    public static string ToJson(ValidationGroundScenario scenario) =>
        JsonSerializer.Serialize(scenario, JsonOptions);

    /// <summary>Deserializes a scenario from a JSON string; returns a default on a null result.</summary>
    public static ValidationGroundScenario FromJson(string json) =>
        JsonSerializer.Deserialize<ValidationGroundScenario>(json, JsonOptions) ?? new ValidationGroundScenario();

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
}
