#nullable enable

using System.Collections.Generic;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>
/// Bounded Generals reference roster for WI-H4-05 parity validation.
/// Represents all six P6b archetypes using USA reference units:
/// infantry (Ranger), vehicle (Crusader Tank), aircraft (Comanche),
/// builder (Dozer), support (Ambulance / Medic), and superweapon (Particle Cannon Uplink).
/// Parity is proven by this bounded roster run only; no full-roster or EE2 claim is made.
/// Physics metadata parity covers the string stable-id fields and absent defaults only (AQ-H4-1 gate).
/// </summary>
public static class UnitPackageRoster
{
    /// <summary>P6b reference roster: infantry, vehicle, aircraft, builder, support, superweapon.</summary>
    public static readonly IReadOnlyList<string> ArchetypeUnits =
    [
        "AmericaInfantryRanger",
        "AmericaTankCrusader",
        "AmericaVehicleComanche",
        "AmericaVehicleDozer",
        "AmericaVehicleMedic",
        "AmericaParticleCannonUplink",
    ];

    /// <summary>
    /// Roster roots required to resolve and materialize the reference roster, including
    /// the implicit DefaultThingTemplate from which all Generals objects inherit default modules.
    /// </summary>
    public static readonly IReadOnlyList<string> ImportRoots =
    [
        "DefaultThingTemplate",
        "AmericaInfantryRanger",
        "AmericaTankCrusader",
        "AmericaVehicleComanche",
        "AmericaVehicleDozer",
        "AmericaVehicleMedic",
        "AmericaParticleCannonUplink",
    ];
}
