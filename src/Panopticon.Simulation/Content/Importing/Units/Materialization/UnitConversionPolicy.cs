#nullable enable

using System;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Declared source-to-runtime unit conversions. Only unit changes are applied; values are never
/// rebalanced. A distance in meters needs an explicit <see cref="GameUnitsPerMeter"/>.
/// </summary>
public sealed class UnitConversionPolicy
{
    public SageGame Game { get; }

    public double? GameUnitsPerMeter { get; }

    public UnitConversionPolicy(SageGame game, double? gameUnitsPerMeter = null)
    {
        if (gameUnitsPerMeter is { } scale)
        {
            if (!double.IsFinite(scale) || scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gameUnitsPerMeter), scale, "Game units per meter must be finite and positive.");
            }
        }

        Game = game;
        GameUnitsPerMeter = gameUnitsPerMeter;
    }

    public double LogicFramesPerSecond => Game.LogicFramesPerSecond();

    public double ToGameDistance(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.GameDistance => quantity.Value,
            QuantityUnit.Meters => quantity.Value * (GameUnitsPerMeter ?? throw new InvalidOperationException("Meters need a declared game-units-per-meter scale.")),
            _ => throw Unexpected(quantity),
        };
    }

    public double ToSeconds(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.Seconds => quantity.Value,
            QuantityUnit.Milliseconds => quantity.Value / 1000.0,
            QuantityUnit.LogicFrames => quantity.Value / LogicFramesPerSecond,
            _ => throw Unexpected(quantity),
        };
    }

    /// <summary>Whole logic frames, rounded up like the INI parser's millisecond conversion.</summary>
    public uint ToLogicFrames(Quantity quantity) => (uint)Math.Ceiling(ToSeconds(quantity) * LogicFramesPerSecond - 1e-9);

    public double ToDistancePerFrame(Quantity quantity)
    {
        return quantity.Unit == QuantityUnit.DistancePerSecond
            ? ToGameDistance(quantity with { Unit = QuantityUnit.GameDistance }) / LogicFramesPerSecond
            : throw Unexpected(quantity);
    }

    public double ToDistancePerFrameSquared(Quantity quantity)
    {
        return quantity.Unit == QuantityUnit.DistancePerSecondSquared
            ? quantity.Value / (LogicFramesPerSecond * LogicFramesPerSecond)
            : throw Unexpected(quantity);
    }

    public double ToRadiansPerFrame(Quantity quantity)
    {
        return quantity.Unit == QuantityUnit.DegreesPerSecond
            ? quantity.Value * Math.PI / 180.0 / LogicFramesPerSecond
            : throw Unexpected(quantity);
    }

    private static InvalidOperationException Unexpected(Quantity quantity) => new($"Unexpected unit {quantity.Unit} for this conversion.");
}
