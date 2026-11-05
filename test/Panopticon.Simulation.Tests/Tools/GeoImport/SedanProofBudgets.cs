using System;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Pure budget mathematics for the Sedan operator proof. Budget A is the operator's
/// 1.0 m engineering acceptance limit against the frozen LiDAR truth; Budget B is the
/// independent one-quantum engine-encoding guard. Neither borrows from the other.
/// </summary>
public static class SedanProofBudgets
{
    /// <summary>Operator decision of 2026-11-03, engineering acceptance only; not a certified source accuracy.</summary>
    public const double EngineeringLimitMeters = 1.0d;

    /// <summary>One legacy height quantum in game units (GeneratedMapPreviewBuilder.HeightQuantumGameUnits).</summary>
    public const double BudgetBLimitGameUnits = 0.0390625d;

    /// <summary>Payload-local game X of a site given in UTM easting meters.</summary>
    public static double SiteGameX(double siteUtmEasting, double payloadOriginEastingMeters, double metersPerGameUnit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(metersPerGameUnit, 0d);

        return (siteUtmEasting - payloadOriginEastingMeters) / metersPerGameUnit;
    }

    /// <summary>Payload-local game Y of a site given in UTM northing meters.</summary>
    public static double SiteGameY(double siteUtmNorthing, double payloadOriginNorthingMeters, double metersPerGameUnit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(metersPerGameUnit, 0d);

        return (siteUtmNorthing - payloadOriginNorthingMeters) / metersPerGameUnit;
    }

    /// <summary>
    /// Absolute vertical residual in meters of a payload game Z against the frozen truth,
    /// inverting the game-unit scale and the vertical exaggeration around the origin.
    /// </summary>
    public static double BudgetAResidualMeters(double verticalOriginMeters, double payloadGameZ, double frozenTruthMeters, double metersPerGameUnit, double verticalExaggeration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(metersPerGameUnit, 0d);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(verticalExaggeration, 0d);

        return Math.Abs(verticalOriginMeters + (payloadGameZ * metersPerGameUnit / verticalExaggeration) - frozenTruthMeters);
    }

    /// <summary>Budget A holds when the residual is at or under <see cref="EngineeringLimitMeters"/>.</summary>
    public static bool BudgetAPasses(double verticalOriginMeters, double payloadGameZ, double frozenTruthMeters, double metersPerGameUnit, double verticalExaggeration)
    {
        return BudgetAResidualMeters(verticalOriginMeters, payloadGameZ, frozenTruthMeters, metersPerGameUnit, verticalExaggeration) <= EngineeringLimitMeters;
    }

    /// <summary>Absolute residual in game units between an engine stored Z and the payload Z minus the crop minimum.</summary>
    public static double BudgetBResidualGameUnits(double engineGameZ, double payloadGameZ, double cropMinimumGameZ)
    {
        return Math.Abs(engineGameZ - (payloadGameZ - cropMinimumGameZ));
    }

    /// <summary>Budget B holds when the residual is at or under <see cref="BudgetBLimitGameUnits"/>.</summary>
    public static bool BudgetBPasses(double engineGameZ, double payloadGameZ, double cropMinimumGameZ)
    {
        return BudgetBResidualGameUnits(engineGameZ, payloadGameZ, cropMinimumGameZ) <= BudgetBLimitGameUnits;
    }

    /// <summary>Meters at the site restored from an engine stored Z by re-adding the crop minimum, then inverting scale and exaggeration.</summary>
    public static double RestoredEngineMeters(double verticalOriginMeters, double engineGameZ, double cropMinimumGameZ, double metersPerGameUnit, double verticalExaggeration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(metersPerGameUnit, 0d);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(verticalExaggeration, 0d);

        return verticalOriginMeters + ((engineGameZ + cropMinimumGameZ) * metersPerGameUnit / verticalExaggeration);
    }

    /// <summary>Engine XY from payload XY subtracts the crop origin; a translation, never a scale factor.</summary>
    public static double EngineXyFromPayload(double payloadGameXY, double cropOriginGameXY)
    {
        return payloadGameXY - cropOriginGameXY;
    }

    /// <summary>Inverse of <see cref="EngineXyFromPayload"/>: payload XY adds the crop origin back.</summary>
    public static double PayloadXyFromEngine(double engineGameXY, double cropOriginGameXY)
    {
        return engineGameXY + cropOriginGameXY;
    }
}
