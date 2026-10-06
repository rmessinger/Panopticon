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

    /// <summary>Game units per source distance unit (for example per EE2 tile); declared by the caller, never inferred.</summary>
    public double? GameUnitsPerSourceDistance { get; }

    public UnitConversionPolicy(SageGame game, double? gameUnitsPerMeter = null, double? gameUnitsPerSourceDistance = null)
    {
        if (gameUnitsPerSourceDistance is { } sourceScale && (!double.IsFinite(sourceScale) || sourceScale <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(gameUnitsPerSourceDistance), sourceScale, "Game units per source distance must be finite and positive.");
        }

        GameUnitsPerSourceDistance = gameUnitsPerSourceDistance;

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
            QuantityUnit.SourceDistance => quantity.Value * (GameUnitsPerSourceDistance ?? throw new InvalidOperationException("Source distances need a declared game-units-per-source-distance scale.")),
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

    /// <summary>
    /// Whole logic frames using the engine's own conversion, including its single-precision
    /// rounding, so a typed source and Generals INI yield the same frames for the same time.
    /// </summary>
    public uint ToLogicFrames(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.Milliseconds => Logic.Object.LogicFrameSpan.FromMilliseconds((float)quantity.Value, Game).Value,
            QuantityUnit.Seconds => Logic.Object.LogicFrameSpan.FromSeconds((float)quantity.Value, Game).Value,
            QuantityUnit.LogicFrames => (uint)Math.Ceiling(quantity.Value),
            _ => throw Unexpected(quantity),
        };
    }

    public double ToDistancePerFrame(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.DistancePerSecond => quantity.Value / LogicFramesPerSecond,
            QuantityUnit.SourceDistancePerSecond => ToGameDistance(quantity with { Unit = QuantityUnit.SourceDistance }) / LogicFramesPerSecond,
            _ => throw Unexpected(quantity),
        };
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
