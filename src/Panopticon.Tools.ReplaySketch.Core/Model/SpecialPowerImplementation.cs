using System;
using System.Collections.Generic;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Whether a <see cref="Panopticon.Logic.Object.SpecialPowerType"/> has a live handler in the
/// order-processing applicators, or is an unimplemented gap.
/// </summary>
public enum SpecialPowerImplementationStatus
{
    Implemented,
    Unimplemented,
}

/// <summary>
/// Static probe that reports which special powers have a live handler. Grounded in the switch
/// arms of <c>SpecialPowerApplicator</c>, <c>SpecialPowerAtLocationApplicator</c>, and
/// <c>SpecialPowerAtObjectApplicator</c>: arms that call a real executor are implemented; arms
/// that throw <see cref="NotImplementedException"/> or fall to the default
/// <see cref="ArgumentOutOfRangeException"/> are gaps. Unknown names default to unimplemented so
/// P6b surfaces them as explicit gaps rather than silent passes.
/// </summary>
public static class SpecialPowerImplementation
{
    private static readonly HashSet<string> ImplementedPowers = new(StringComparer.Ordinal)
    {
        // SpecialPowerAtLocationApplicator (real executors).
        "SpySatellite",
        "SpyDrone",
        "RadarVanScan",
        "Ambush",
        "CleanupArea",
        // SpecialPowerAtObjectApplicator (real executors).
        "CashHack",
        "BlackLotusCaptureBuilding",
        "InfantryCaptureBuilding",
        "HackerDisableBuilding",
        "BlackLotusDisableVehicleHack",
        "BlackLotusStealCashHack",
        // SpecialPowerApplicator (real executor).
        "ChangeBattlePlans",
    };

    public static SpecialPowerImplementationStatus Of(string specialPowerName) =>
        ImplementedPowers.Contains(specialPowerName)
            ? SpecialPowerImplementationStatus.Implemented
            : SpecialPowerImplementationStatus.Unimplemented;

    public static bool IsImplemented(string specialPowerName) =>
        Of(specialPowerName) == SpecialPowerImplementationStatus.Implemented;
}
