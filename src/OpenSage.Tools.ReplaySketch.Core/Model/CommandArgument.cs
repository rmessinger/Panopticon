using System;
using System.Numerics;
using System.Text.Json.Serialization;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// A single typed argument to a <see cref="CommandSpec"/>, mirroring the
/// <see cref="OrderArgumentType"/> variants understood by the engine.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IntegerCommandArgument), "integer")]
[JsonDerivedType(typeof(FloatCommandArgument), "float")]
[JsonDerivedType(typeof(BooleanCommandArgument), "boolean")]
[JsonDerivedType(typeof(ObjectIdCommandArgument), "objectId")]
[JsonDerivedType(typeof(PositionCommandArgument), "position")]
public abstract class CommandArgument
{
    /// <summary>Appends this argument to <paramref name="order"/>.</summary>
    public abstract void AppendTo(Order order, PositionContext ctx, Random rng);
}

/// <summary>Appends an integer argument.</summary>
public sealed class IntegerCommandArgument : CommandArgument
{
    public int Value { get; }

    [JsonConstructor]
    public IntegerCommandArgument(int value) => Value = value;

    public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
        order.AddIntegerArgument(Value);
}

/// <summary>Appends a float argument.</summary>
public sealed class FloatCommandArgument : CommandArgument
{
    public float Value { get; }

    [JsonConstructor]
    public FloatCommandArgument(float value) => Value = value;

    public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
        order.AddFloatArgument(Value);
}

/// <summary>Appends a boolean argument.</summary>
public sealed class BooleanCommandArgument : CommandArgument
{
    public bool Value { get; }

    [JsonConstructor]
    public BooleanCommandArgument(bool value) => Value = value;

    public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
        order.AddBooleanArgument(Value);
}

/// <summary>Appends an object-id argument.</summary>
public sealed class ObjectIdCommandArgument : CommandArgument
{
    public uint Value { get; }

    [JsonConstructor]
    public ObjectIdCommandArgument(uint value) => Value = value;

    public override void AppendTo(Order order, PositionContext ctx, Random rng) =>
        order.AddObjectIdArgument(new ObjectId(Value));
}

/// <summary>
/// Appends a world-space position argument, resolved from a <see cref="PositionSpec"/>
/// so that positions can be landmark-relative or normalized.
/// </summary>
public sealed class PositionCommandArgument : CommandArgument
{
    public PositionSpec Spec { get; }

    [JsonConstructor]
    public PositionCommandArgument(PositionSpec spec) => Spec = spec;

    public override void AppendTo(Order order, PositionContext ctx, Random rng)
    {
        var worldPos = Spec.Resolve(ctx, rng);
        order.AddPositionArgument(worldPos);
    }
}
