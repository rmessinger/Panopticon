#nullable enable

using System;
using System.Collections.Concurrent;

namespace Panopticon.Content.Importing.Units.Materialization;

public enum SourceUnitStatus
{
    EstablishedPhysical,
    ProvisionalCompatibility,
}

/// <summary>
/// Centralized per-game source unit declaration at the source-to-engine boundary.
/// Declares meters-per-source-unit scale, never an assumed global physical truth.
/// </summary>
public sealed record SourceUnitPolicy(
    SageGame Game,
    double MetersPerSourceUnit,
    SourceUnitStatus Status,
    string Description)
{
    public bool IsEstablishedPhysical => Status == SourceUnitStatus.EstablishedPhysical;
    public bool IsProvisional => Status == SourceUnitStatus.ProvisionalCompatibility;

    private static readonly ConcurrentDictionary<SageGame, SourceUnitPolicy> Overrides = new();

    public static SourceUnitPolicy ForGame(SageGame game)
    {
        if (Overrides.TryGetValue(game, out var overridden))
        {
            return overridden;
        }

        return game switch
        {
            SageGame.CncGenerals => Generals,
            SageGame.CncGeneralsZeroHour => GeneralsZeroHour,
            SageGame.Bfme => Bfme,
            SageGame.Bfme2 => Bfme2,
            SageGame.Bfme2Rotwk => Bfme2Rotwk,
            SageGame.Cnc3 or SageGame.Cnc3KanesWrath or SageGame.Ra3 or SageGame.Ra3Uprising or SageGame.Cnc4 =>
                throw new NotSupportedException($"SourceUnitPolicy is not declared for unsupported game '{game}'."),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, $"Unknown SageGame '{game}'.")
        };
    }

    public static void SetOverride(SageGame game, SourceUnitPolicy? policy)
    {
        if (policy is null)
        {
            Overrides.TryRemove(game, out _);
        }
        else
        {
            Overrides[game] = policy;
        }
    }

    public static void ClearOverrides() => Overrides.Clear();

    public static readonly SourceUnitPolicy Generals = new(
        SageGame.CncGenerals,
        UnitConversionPolicy.GeneralsFeetToMeters,
        SourceUnitStatus.EstablishedPhysical,
        "Established physical calibration: 0.3048 meters (1 foot) per legacy unit.");

    public static readonly SourceUnitPolicy GeneralsZeroHour = new(
        SageGame.CncGeneralsZeroHour,
        UnitConversionPolicy.GeneralsFeetToMeters,
        SourceUnitStatus.EstablishedPhysical,
        "Established physical calibration: 0.3048 meters (1 foot) per legacy unit.");

    public static readonly SourceUnitPolicy Bfme = new(
        SageGame.Bfme,
        UnitConversionPolicy.GeneralsFeetToMeters,
        SourceUnitStatus.ProvisionalCompatibility,
        "Provisional compatibility scale: 0.3048 meters per legacy unit, unverified physical calibration; never claimed as a measured unit.");

    public static readonly SourceUnitPolicy Bfme2 = new(
        SageGame.Bfme2,
        UnitConversionPolicy.GeneralsFeetToMeters,
        SourceUnitStatus.ProvisionalCompatibility,
        "Provisional compatibility scale: 0.3048 meters per legacy unit, unverified physical calibration; never claimed as a measured unit.");

    public static readonly SourceUnitPolicy Bfme2Rotwk = new(
        SageGame.Bfme2Rotwk,
        UnitConversionPolicy.GeneralsFeetToMeters,
        SourceUnitStatus.ProvisionalCompatibility,
        "Provisional compatibility scale: 0.3048 meters per legacy unit, unverified physical calibration; never claimed as a measured unit.");
}

/// <summary>
/// Declared source-to-runtime unit conversions and explicit similarity primitives for s = 0.3048.
/// Only unit changes are applied; values are never rebalanced.
/// </summary>
public sealed class UnitConversionPolicy
{
    /// <summary>The established Generals distance convention: one foot (0.3048 meters per legacy unit).</summary>
    public const double GeneralsFeetToMeters = 0.3048;

    /// <summary>Inverse legacy scale: legacy units per meter.</summary>
    public const double MetersToGeneralsFeet = 1.0 / GeneralsFeetToMeters;

    /// <summary>Scales a legacy length (L^1) to meters by factor s.</summary>
    public static double ScaleLength(double legacyUnits) => legacyUnits * GeneralsFeetToMeters;

    /// <summary>Scales a legacy area or squared threshold (L^2) to square meters by factor s^2.</summary>
    public static double ScaleArea(double legacyArea) => legacyArea * (GeneralsFeetToMeters * GeneralsFeetToMeters);

    /// <summary>Scales a legacy volume (L^3) to cubic meters by factor s^3.</summary>
    public static double ScaleVolume(double legacyVolume) => legacyVolume * (GeneralsFeetToMeters * GeneralsFeetToMeters * GeneralsFeetToMeters);

    /// <summary>Scales an inverse-length quantity (L^-1) to inverse-meters by factor 1/s.</summary>
    public static double ScaleInverseLength(double legacyInverseLength) => legacyInverseLength / GeneralsFeetToMeters;

    /// <summary>Unscales meters (L^1) to legacy units by factor 1/s.</summary>
    public static double UnscaleLength(double meters) => meters / GeneralsFeetToMeters;

    /// <summary>Unscales square meters (L^2) to legacy area by factor 1/s^2.</summary>
    public static double UnscaleArea(double squareMeters) => squareMeters / (GeneralsFeetToMeters * GeneralsFeetToMeters);

    /// <summary>Unscales cubic meters (L^3) to legacy volume by factor 1/s^3.</summary>
    public static double UnscaleVolume(double cubicMeters) => cubicMeters / (GeneralsFeetToMeters * GeneralsFeetToMeters * GeneralsFeetToMeters);

    /// <summary>Unscales inverse-meters (L^-1) to legacy inverse-length by factor s.</summary>
    public static double UnscaleInverseLength(double inverseMeters) => inverseMeters * GeneralsFeetToMeters;

    // Non-length quantities: angles, time, health, damage, ratios remain unchanged
    public static double ScaleAngle(double degrees) => degrees;
    public static double ScaleTime(double seconds) => seconds;
    public static double ScaleHealth(double health) => health;
    public static double ScaleDamage(double damage) => damage;
    public static double ScaleRatio(double ratio) => ratio;

    /// <summary>
    /// Scales an EE2 source-distance scale factor (e.g. tile scale) to meters by similarity: scale * s.
    /// Predetermined expectation: 12 * 0.3048 = 3.6576 m; 18.2505 * 0.3048 = 5.5627524 m.
    /// </summary>
    public static double ScaleEe2SourceDistance(double legacyScale) => legacyScale * GeneralsFeetToMeters;

    /// <summary>
    /// Scales an EE2 source model unit scale factor (e.g. pose scale) to meters by similarity: scale * s.
    /// Predetermined expectation: 0.36501 * 0.3048 = 0.111255048 m.
    /// </summary>
    public static double ScaleEe2SourceModelUnit(double legacyScale) => legacyScale * GeneralsFeetToMeters;

    public SageGame Game { get; }

    public SourceUnitPolicy SourcePolicy { get; }

    public double? GameUnitsPerMeter { get; }

    /// <summary>Meters per source distance unit (for example per EE2 tile); declared by the caller, never inferred.</summary>
    public double? MetersPerSourceDistance { get; }

    /// <summary>Legacy alias for MetersPerSourceDistance.</summary>
    public double? GameUnitsPerSourceDistance => MetersPerSourceDistance;

    public UnitConversionPolicy(
        SageGame game,
        double? gameUnitsPerMeter = null,
        double? gameUnitsPerSourceDistance = null,
        double? metersPerSourceDistance = null)
    {
        var effectiveSourceDistance = metersPerSourceDistance ?? gameUnitsPerSourceDistance;
        if (effectiveSourceDistance is { } sourceScale && (!double.IsFinite(sourceScale) || sourceScale <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(metersPerSourceDistance), sourceScale, "Meters per source distance must be finite and positive.");
        }

        MetersPerSourceDistance = effectiveSourceDistance;

        if (gameUnitsPerMeter is { } scale)
        {
            if (!double.IsFinite(scale) || scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gameUnitsPerMeter), scale, "Game units per meter must be finite and positive.");
            }
        }

        Game = game;
        GameUnitsPerMeter = gameUnitsPerMeter;
        SourcePolicy = SourceUnitPolicy.ForGame(game);
    }

    public double LogicFramesPerSecond => Game.LogicFramesPerSecond();

    public double ToDistance(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.Meters => quantity.Value,
            QuantityUnit.GameDistance => quantity.Value,
            QuantityUnit.SourceDistance => quantity.Value * (MetersPerSourceDistance ?? throw new InvalidOperationException("Source distances need a declared meters-per-source-distance scale.")),
            _ => throw Unexpected(quantity),
        };
    }

    public double ToGameDistance(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.GameDistance => quantity.Value,
            QuantityUnit.Meters => quantity.Value * (GameUnitsPerMeter ?? throw new InvalidOperationException("Meters need a declared game-units-per-meter scale.")),
            QuantityUnit.SourceDistance => quantity.Value * (MetersPerSourceDistance ?? throw new InvalidOperationException("Source distances need a declared game-units-per-source-distance scale.")),
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
            QuantityUnit.MetersPerSecond => quantity.Value / LogicFramesPerSecond,
            QuantityUnit.DistancePerSecond => quantity.Value / LogicFramesPerSecond,
            QuantityUnit.SourceDistancePerSecond => ToDistance(quantity with { Unit = QuantityUnit.SourceDistance }) / LogicFramesPerSecond,
            _ => throw Unexpected(quantity),
        };
    }

    public double ToDistancePerFrameSquared(Quantity quantity)
    {
        return quantity.Unit switch
        {
            QuantityUnit.MetersPerSecondSquared or QuantityUnit.DistancePerSecondSquared =>
                quantity.Value / (LogicFramesPerSecond * LogicFramesPerSecond),
            _ => throw Unexpected(quantity),
        };
    }

    public double ToRadiansPerFrame(Quantity quantity)
    {
        return quantity.Unit == QuantityUnit.DegreesPerSecond
            ? quantity.Value * Math.PI / 180.0 / LogicFramesPerSecond
            : throw Unexpected(quantity);
    }

    private static InvalidOperationException Unexpected(Quantity quantity) => new($"Unexpected unit {quantity.Unit} for this conversion.");
}
