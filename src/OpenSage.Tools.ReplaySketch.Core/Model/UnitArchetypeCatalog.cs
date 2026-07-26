using System;
using System.Collections.Generic;
using OpenSage.Logic.Object;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// The reference unit for one archetype in one faction: the definition name, the
/// <see cref="ObjectKinds"/> tags that identify the archetype, and the command-set special
/// powers the contract must exercise. See deep dive section 2.1.
/// </summary>
public sealed record ArchetypeReferenceUnit(
    UnitArchetype Archetype,
    string FactionName,
    string DefinitionName,
    IReadOnlyList<ObjectKinds> KindOfTags,
    IReadOnlyList<string> SpecialPowerNames);

/// <summary>
/// The P6b archetype reference-unit resolver. Maps <c>archetype x faction</c> to the reference
/// unit drawn from the deep dive and the in-repo <c>UnitCatalog</c>. Cells without a valid
/// reference unit resolve to <see langword="null"/> and are pruned by the matrix ConstraintFilter.
/// </summary>
public static class UnitArchetypeCatalog
{
    public const string FactionUSA = "USA";
    public const string FactionGLA = "GLA";
    public const string FactionChina = "China";

    public static readonly IReadOnlyList<string> Factions =
    [
        FactionUSA,
        FactionGLA,
        FactionChina,
    ];

    public static readonly IReadOnlyList<UnitArchetype> Archetypes =
    [
        UnitArchetype.Infantry,
        UnitArchetype.Vehicle,
        UnitArchetype.Aircraft,
        UnitArchetype.Builder,
        UnitArchetype.Support,
        UnitArchetype.Superweapon,
    ];

    private static readonly IReadOnlyList<ObjectKinds> InfantryTags = [ObjectKinds.Infantry];
    private static readonly IReadOnlyList<ObjectKinds> VehicleTags = [ObjectKinds.Vehicle];
    private static readonly IReadOnlyList<ObjectKinds> AircraftTags = [ObjectKinds.Aircraft];
    private static readonly IReadOnlyList<ObjectKinds> DozerTags = [ObjectKinds.Dozer];
    private static readonly IReadOnlyList<ObjectKinds> SuperweaponTags =
        [ObjectKinds.Structure, ObjectKinds.FSSuperweapon];

    // USA reference units (deep dive section 2.1; names confirmed against UnitCatalog).
    private static readonly ArchetypeReferenceUnit UsaInfantry =
        new(UnitArchetype.Infantry, FactionUSA, "AmericaInfantryRanger", InfantryTags, []);
    private static readonly ArchetypeReferenceUnit UsaVehicle =
        new(UnitArchetype.Vehicle, FactionUSA, "AmericaTankCrusader", VehicleTags, []);
    private static readonly ArchetypeReferenceUnit UsaAircraft =
        new(UnitArchetype.Aircraft, FactionUSA, "AmericaVehicleComanche", AircraftTags, []);
    private static readonly ArchetypeReferenceUnit UsaBuilder =
        new(UnitArchetype.Builder, FactionUSA, "AmericaVehicleDozer", DozerTags, []);
    private static readonly ArchetypeReferenceUnit UsaSupport =
        new(UnitArchetype.Support, FactionUSA, "AmericaVehicleMedic", VehicleTags, ["CleanupArea"]);
    private static readonly ArchetypeReferenceUnit UsaSuperweapon =
        new(UnitArchetype.Superweapon, FactionUSA, "AmericaParticleCannon", SuperweaponTags,
            ["ParticleUplinkCannon"]);

    // China reference units.
    private static readonly ArchetypeReferenceUnit ChinaInfantry =
        new(UnitArchetype.Infantry, FactionChina, "ChinaInfantryRedGuard", InfantryTags, []);
    private static readonly ArchetypeReferenceUnit ChinaVehicle =
        new(UnitArchetype.Vehicle, FactionChina, "ChinaTankBattleMaster", VehicleTags, []);
    private static readonly ArchetypeReferenceUnit ChinaAircraft =
        new(UnitArchetype.Aircraft, FactionChina, "ChinaJetMIG", AircraftTags, []);
    private static readonly ArchetypeReferenceUnit ChinaBuilder =
        new(UnitArchetype.Builder, FactionChina, "ChinaVehicleDozer", DozerTags, []);
    private static readonly ArchetypeReferenceUnit ChinaSupport =
        new(UnitArchetype.Support, FactionChina, "ChinaInfantryHacker", InfantryTags, []);
    private static readonly ArchetypeReferenceUnit ChinaSuperweapon =
        new(UnitArchetype.Superweapon, FactionChina, "ChinaNuclearMissile", SuperweaponTags,
            ["NuclearMissile"]);

    // GLA reference units. GLA has no aircraft and no unambiguous support unit in the base game;
    // those cells are intentionally absent and pruned by the matrix ConstraintFilter.
    private static readonly ArchetypeReferenceUnit GlaInfantry =
        new(UnitArchetype.Infantry, FactionGLA, "GLAInfantryRebel", InfantryTags, []);
    private static readonly ArchetypeReferenceUnit GlaVehicle =
        new(UnitArchetype.Vehicle, FactionGLA, "GLATankScorpion", VehicleTags, []);
    private static readonly ArchetypeReferenceUnit GlaBuilder =
        new(UnitArchetype.Builder, FactionGLA, "GLAVehicleWorker", DozerTags, []);
    private static readonly ArchetypeReferenceUnit GlaSuperweapon =
        new(UnitArchetype.Superweapon, FactionGLA, "GLAScudStorm", SuperweaponTags, ["ScudStorm"]);

    private static readonly Dictionary<(UnitArchetype, string), ArchetypeReferenceUnit> References = new()
    {
        // USA: all six archetypes.
        [(UnitArchetype.Infantry, FactionUSA)] = UsaInfantry,
        [(UnitArchetype.Vehicle, FactionUSA)] = UsaVehicle,
        [(UnitArchetype.Aircraft, FactionUSA)] = UsaAircraft,
        [(UnitArchetype.Builder, FactionUSA)] = UsaBuilder,
        [(UnitArchetype.Support, FactionUSA)] = UsaSupport,
        [(UnitArchetype.Superweapon, FactionUSA)] = UsaSuperweapon,
        // China: all six archetypes.
        [(UnitArchetype.Infantry, FactionChina)] = ChinaInfantry,
        [(UnitArchetype.Vehicle, FactionChina)] = ChinaVehicle,
        [(UnitArchetype.Aircraft, FactionChina)] = ChinaAircraft,
        [(UnitArchetype.Builder, FactionChina)] = ChinaBuilder,
        [(UnitArchetype.Support, FactionChina)] = ChinaSupport,
        [(UnitArchetype.Superweapon, FactionChina)] = ChinaSuperweapon,
        // GLA: four archetypes; aircraft and support pruned (no valid reference unit).
        [(UnitArchetype.Infantry, FactionGLA)] = GlaInfantry,
        [(UnitArchetype.Vehicle, FactionGLA)] = GlaVehicle,
        [(UnitArchetype.Builder, FactionGLA)] = GlaBuilder,
        [(UnitArchetype.Superweapon, FactionGLA)] = GlaSuperweapon,
    };

    /// <summary>
    /// Returns the reference unit for <paramref name="archetype"/> in <paramref name="faction"/>,
    /// or <see langword="null"/> when no valid reference unit exists (the cell is pruned).
    /// </summary>
    public static ArchetypeReferenceUnit? Resolve(UnitArchetype archetype, string faction) =>
        References.TryGetValue((archetype, faction), out var unit) ? unit : null;

    /// <summary>True when a reference unit exists for the archetype-faction cell.</summary>
    public static bool HasReferenceUnit(UnitArchetype archetype, string faction) =>
        Resolve(archetype, faction) is not null;

    /// <summary>Total number of valid (non-pruned) archetype-faction cells.</summary>
    public static int ValidCellCount
    {
        get
        {
            var count = 0;
            foreach (var archetype in Archetypes)
            {
                foreach (var faction in Factions)
                {
                    if (HasReferenceUnit(archetype, faction))
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }

    /// <summary>Builds a <see cref="UnitContractScenario"/> from the catalog reference unit.</summary>
    public static UnitContractScenario BuildScenario(UnitArchetype archetype, string faction)
    {
        var unit = Resolve(archetype, faction)
            ?? throw new ArgumentException(
                $"No reference unit for archetype {archetype} in faction {faction}.",
                nameof(archetype));
        return new UnitContractScenario
        {
            Archetype = unit.Archetype,
            FactionName = unit.FactionName,
            UnitDefinitionName = unit.DefinitionName,
            SpecialPowerNames = unit.SpecialPowerNames,
        };
    }
}
