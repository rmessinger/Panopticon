using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSage.Tools.ReplaySketch.Model;

[JsonConverter(typeof(ActionEntryJsonConverter))]
public sealed class ActionEntry
{
    /// <summary>
    /// Unique identifier for this action, used by <see cref="AfterActionTiming.TriggerActionId"/>.
    /// Auto-generated on construction; preserved in JSON for stable cross-action references.
    /// </summary>
    public string Id { get; }

    public string Label { get; set; }

    /// <summary>The open command model. Always non-null.</summary>
    public CommandSpec Command { get; set; }

    /// <summary>
    /// Non-null for legacy actions (Construct, GatherResources, etc.); null for open CommandSpec commands.
    /// </summary>
    [JsonIgnore]
    public ActionType? Type { get; set; }

    /// <summary>
    /// Object definition name for Construct actions. Null or unused for other action types.
    /// </summary>
    public string? BuildingDefinitionName { get; set; }

    /// <summary>
    /// Object definition name for RecruitBasicUnit actions. Null or unused for other action types.
    /// </summary>
    public string? UnitDefinitionName { get; set; }

    /// <summary>
    /// Number of units to queue for RecruitBasicUnit actions. Null or unused for other action
    /// types. Defaults to 5 (see <see cref="Services.ReplayExporter"/>) when unset, to preserve
    /// the original "queue enough attackers" behaviour of the default scenario.
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Position for legacy actions. Null or unused for open commands, which embed position
    /// arguments directly in their CommandSpec.Arguments.
    /// </summary>
    public PositionSpec? Position { get; set; }

    public TimingConfig Timing { get; set; }

    public ActionEntry(string label, CommandSpec command, PositionSpec? position, TimingConfig timing,
        string? id = null, string? buildingDefinitionName = null, string? unitDefinitionName = null,
        ActionType? type = null, int? quantity = null)
    {
        Id = id ?? GenerateId();
        Label = label;
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Position = position;
        Timing = timing;
        BuildingDefinitionName = buildingDefinitionName;
        UnitDefinitionName = unitDefinitionName;
        Quantity = quantity;
        Type = type;
    }

    /// <summary>Legacy constructor. Wraps ActionType in a CommandSpec.</summary>
    public ActionEntry(string label, ActionType type, PositionSpec? position, TimingConfig timing,
        string? id = null, string? buildingDefinitionName = null, string? unitDefinitionName = null,
        int? quantity = null)
        : this(label, CommandSpec.ForLegacyType(type), position, timing, id, buildingDefinitionName, unitDefinitionName, type, quantity)
    {
    }

    private static string GenerateId() => Guid.NewGuid().ToString("N")[..6];
}

/// <summary>
/// Custom JSON converter that handles both legacy ("type": "Construct") and new ("command": {...})
/// formats. Writes "type" for legacy actions and "command" for open commands.
/// </summary>
internal sealed class ActionEntryJsonConverter : JsonConverter<ActionEntry>
{
    public override ActionEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetString();
        var label = root.GetProperty("label").GetString() ?? string.Empty;
        var timing = JsonSerializer.Deserialize<TimingConfig>(root.GetProperty("timing"), options)
            ?? throw new JsonException("Missing or invalid 'timing' field");

        CommandSpec? command = null;
        ActionType? legacyType = null;

        // Try new format first: "command": {...}
        if (root.TryGetProperty("command", out var commandElem))
        {
            command = JsonSerializer.Deserialize<CommandSpec>(commandElem, options)
                ?? throw new JsonException("Invalid 'command' format");
        }

        // Fall back to legacy format: "type": "Construct"
        else if (root.TryGetProperty("type", out var typeElem) && typeElem.ValueKind == JsonValueKind.String)
        {
            var typeStr = typeElem.GetString();
            if (Enum.TryParse<ActionType>(typeStr, out var t))
            {
                legacyType = t;
            }
            else
            {
                throw new JsonException($"Unknown legacy action type: '{typeStr}'");
            }
        }
        else
        {
            throw new JsonException("Neither 'command' nor 'type' field found");
        }

        if (command == null && legacyType.HasValue)
        {
            command = CommandSpec.ForLegacyType(legacyType.Value);
        }
        else if (command == null)
        {
            throw new JsonException("Failed to create CommandSpec");
        }

        var position = root.TryGetProperty("position", out var posElem) && posElem.ValueKind != JsonValueKind.Null
            ? JsonSerializer.Deserialize<PositionSpec>(posElem, options)
            : null;

        var buildingName = root.TryGetProperty("buildingDefinitionName", out var bElem) && bElem.ValueKind == JsonValueKind.String
            ? bElem.GetString()
            : null;

        var unitName = root.TryGetProperty("unitDefinitionName", out var uElem) && uElem.ValueKind == JsonValueKind.String
            ? uElem.GetString()
            : null;

        var quantity = root.TryGetProperty("quantity", out var qElem) && qElem.ValueKind == JsonValueKind.Number
            ? qElem.GetInt32()
            : (int?)null;

        return new ActionEntry(label, command, position, timing, id, buildingName, unitName, legacyType, quantity);
    }

    public override void Write(Utf8JsonWriter writer, ActionEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("label", value.Label);
        if (value.Type.HasValue)
        {
            writer.WriteString("type", value.Type.Value.ToString());
        }
        else
        {
            writer.WritePropertyName("command");
            JsonSerializer.Serialize(writer, value.Command, options);
        }

        writer.WritePropertyName("timing");
        JsonSerializer.Serialize(writer, value.Timing, options);
        if (value.Position != null)
        {
            writer.WritePropertyName("position");
            JsonSerializer.Serialize(writer, value.Position, options);
        }

        if (value.BuildingDefinitionName != null)
        {
            writer.WriteString("buildingDefinitionName", value.BuildingDefinitionName);
        }

        if (value.UnitDefinitionName != null)
        {
            writer.WriteString("unitDefinitionName", value.UnitDefinitionName);
        }

        if (value.Quantity.HasValue)
        {
            writer.WriteNumber("quantity", value.Quantity.Value);
        }

        writer.WriteEndObject();
    }
}

