using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content;
using Panopticon.Logic.Orders;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// An open command model that maps directly onto an engine <see cref="Order"/>.
/// Supports arbitrary <see cref="OrderType"/> and typed arguments.
/// </summary>
[JsonConverter(typeof(CommandSpecJsonConverter))]
public sealed class CommandSpec
{
    /// <summary>The engine order type.</summary>
    public OrderType OrderType { get; }

    /// <summary>Typed arguments for this command.</summary>
    public IReadOnlyList<CommandArgument> Arguments { get; }

    public CommandSpec(OrderType orderType, IReadOnlyList<CommandArgument> arguments)
    {
        OrderType = orderType;
        Arguments = arguments;
    }

    /// <summary>Creates an Order from this spec by resolving all arguments.</summary>
    public Order ToOrder(int playerIndex, PositionContext ctx, Random rng)
    {
        var order = new Order(playerIndex, OrderType);
        foreach (var arg in Arguments)
        {
            arg.AppendTo(order, ctx, rng);
        }

        return order;
    }

    public CommandSpec Resolve(IObjectReferenceResolver resolver)
    {
        var resolvedArguments = Arguments.Select(arg => arg.Resolve(resolver)).ToList();
        return new CommandSpec(OrderType, resolvedArguments);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Factory methods for legacy actions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Factory for Construct (legacy).</summary>
    public static CommandSpec Construct() =>
        new(OrderType.BuildObject, Array.Empty<CommandArgument>());

    /// <summary>Factory for GatherResources (legacy).</summary>
    public static CommandSpec GatherResources() =>
        new(OrderType.GatherDumpSupplies, Array.Empty<CommandArgument>());

    /// <summary>Factory for RecruitBasicUnit (legacy).</summary>
    public static CommandSpec RecruitBasicUnit() =>
        new(OrderType.CreateUnit, Array.Empty<CommandArgument>());

    /// <summary>Factory for AttackEnemyBase (legacy).</summary>
    public static CommandSpec AttackEnemyBase() =>
        new(OrderType.ForceAttackGround, Array.Empty<CommandArgument>());

    /// <summary>Factory for EnemyBaseDestroyed (legacy sentinel — produces no order).</summary>
    public static CommandSpec EnemyBaseDestroyed() =>
        new(OrderType.Unknown1, Array.Empty<CommandArgument>());

    /// <summary>
    /// Converts a legacy <see cref="ActionType"/> to its <see cref="CommandSpec"/> factory equivalent.
    /// </summary>
    public static CommandSpec ForLegacyType(ActionType type) =>
        type switch
        {
            ActionType.Construct => Construct(),
            ActionType.GatherResources => GatherResources(),
            ActionType.RecruitBasicUnit => RecruitBasicUnit(),
            ActionType.AttackEnemyBase => AttackEnemyBase(),
            ActionType.EnemyBaseDestroyed => EnemyBaseDestroyed(),
            _ => throw new InvalidOperationException($"Unknown ActionType: {type}"),
        };

    /// <summary>
    /// Creates an open command (not legacy) with the given order type and arguments.
    /// </summary>
    public static CommandSpec CreateOpen(OrderType orderType, params CommandArgument[] arguments) =>
        new(orderType, arguments);

    /// <summary>
    /// Creates an open command (not legacy) with the given order type and arguments.
    /// </summary>
    public static CommandSpec CreateOpen(OrderType orderType, IEnumerable<CommandArgument> arguments) =>
        new(orderType, arguments.ToList());
}

/// <summary>Custom JSON converter for CommandSpec.</summary>
internal sealed class CommandSpecJsonConverter : JsonConverter<CommandSpec>
{
    public override CommandSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var orderTypeStr = root.GetProperty("orderType").GetString();
        if (!Enum.TryParse<OrderType>(orderTypeStr, out var orderType))
        {
            throw new JsonException($"Unknown OrderType: '{orderTypeStr}'");
        }

        var arguments = new List<CommandArgument>();
        if (root.TryGetProperty("arguments", out var argsElem))
        {
            foreach (var argElem in argsElem.EnumerateArray())
            {
                var arg = JsonSerializer.Deserialize<CommandArgument>(argElem, options)
                    ?? throw new JsonException("Failed to deserialize CommandArgument");
                arguments.Add(arg);
            }
        }

        return new CommandSpec(orderType, arguments);
    }

    public override void Write(Utf8JsonWriter writer, CommandSpec value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("orderType", value.OrderType.ToString());

        writer.WritePropertyName("arguments");
        writer.WriteStartArray();
        foreach (var arg in value.Arguments)
        {
            JsonSerializer.Serialize(writer, arg, typeof(CommandArgument), options);
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}
