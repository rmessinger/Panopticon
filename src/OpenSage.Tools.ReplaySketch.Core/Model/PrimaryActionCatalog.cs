using System.Collections.Generic;
using OpenSage.Logic.Orders;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Resolves every <see cref="UnitCatalog"/> entry to exactly one <see cref="PrimaryAction"/>,
/// with <see cref="PrimaryActionKind.Move"/> as the universal fallback. Dozers/workers resolve to
/// Build, supply units to Gather, support units to SpecialPower, remaining combat units to Attack.
/// See deep dive P9 §2.4.
/// </summary>
public static class PrimaryActionCatalog
{
    public const int MoveFrameBudget = 200;
    public const int AttackFrameBudget = 250;
    public const int SpecialPowerFrameBudget = 300;
    public const int BuildFrameBudget = 400;
    public const int GatherFrameBudget = 400;

    // Support units whose primary contribution is an ability rather than a weapon.
    private static readonly HashSet<string> SupportDefinitionNames = new()
    {
        "AmericaVehicleMedic",       // Ambulance: cleanup/heal field
        "ChinaInfantryBlackLotus",   // hack/steal ability
        "ChinaInfantryHacker",       // internet hack
        "GLAInfantryHijacker",       // vehicle hijack
    };

    /// <summary>
    /// Roster units whose Attack action does not produce target damage through a directly-commanded
    /// weapon in the validation-ground sandbox. Re-verified against the CarAsian1 target
    /// (TruckArmor) per the gap-closure plan WI-VG-01, and each reason is grounded in the install's
    /// own INI data. They would always time out, so the host records them as named gaps instead of
    /// running them through the timer, and they have no baseline entry. Escalate per deep dive
    /// P9 §2.6. Authoritative for both the host and the baseline-coverage test. Units whose only
    /// obstacle is the shared target's armor (the Pathfinder's FLESHY_SNIPER versus TruckArmor)
    /// carry a per-unit UnitOption.TargetOverride instead and are measured normally.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> UnmeasurableAttackReasons = new Dictionary<string, string>
    {
        ["AmericaJetAurora"] = "bomber: JetAIUpdate has no attack state in the engine, so the bombing run cannot be commanded (AuroraBombWeapon is only delivered by the run)",
        ["ChinaVehicleTroopCrawler"] = "transport: its only weapon (TroopCrawlerAssault, 1e-5 damage of type Deploy) is the passenger assault signal, not a damage source",
        ["GLAInfantryAngryMobNexus"] = "horde composite: the nexus weapon (GLAAngryMobNexusHarmlessWeapon, 0.001 damage) is harmless by design; damage comes from SpawnBehavior members that receive no attack order",
    };

    /// <summary>
    /// Resolves the primary action for a definition name. Falls back to Attack for unrecognised
    /// combat units and to Move only when a unit matches no known classification.
    /// </summary>
    public static PrimaryAction Resolve(string definitionName)
    {
        if (IsBuilder(definitionName))
        {
            return new PrimaryAction(
                PrimaryActionKind.Build,
                OrderType.BuildObject,
                BuildTargetFor(definitionName),
                BuildFrameBudget);
        }

        if (IsSupply(definitionName))
        {
            return new PrimaryAction(
                PrimaryActionKind.Gather,
                OrderType.GatherDumpSupplies,
                null,
                GatherFrameBudget);
        }

        if (SupportDefinitionNames.Contains(definitionName))
        {
            return new PrimaryAction(
                PrimaryActionKind.SpecialPower,
                OrderType.SpecialPowerAtObject,
                null,
                SpecialPowerFrameBudget);
        }

        // Default for the roster: a combat unit that attacks its assigned target.
        return new PrimaryAction(
            PrimaryActionKind.Attack,
            OrderType.AttackObject,
            null,
            AttackFrameBudget);
    }

    /// <summary>Resolves the primary action for a catalog unit option.</summary>
    public static PrimaryAction Resolve(UnitOption unit) => Resolve(unit.DefinitionName);

    /// <summary>
    /// Every catalog entry paired with its resolved action, across all factions. Used by the
    /// completeness test so adding a unit to <see cref="UnitCatalog"/> without a mapping fails.
    /// </summary>
    public static IEnumerable<(UnitOption Unit, PrimaryAction Action)> AllEntries()
    {
        foreach (var factionIndex in new[] { 2, 3, 4 })
        {
            foreach (var unit in UnitCatalog.ForFaction(factionIndex))
            {
                yield return (unit, Resolve(unit));
            }
        }
    }

    private static bool IsBuilder(string definitionName) =>
        definitionName.Contains("Dozer") || definitionName.Contains("Worker");

    private static bool IsSupply(string definitionName) =>
        definitionName.Contains("SupplyTruck") || definitionName.Contains("Chinook");

    // The fixed cheap structure a builder constructs. Drawn from BuildingCatalog's per-faction
    // barracks-equivalent so the target resolves against the same roster source.
    private static string BuildTargetFor(string definitionName)
    {
        if (definitionName.StartsWith("America"))
        {
            return "AmericaBarracks";
        }

        if (definitionName.StartsWith("China"))
        {
            return "ChinaBarracks";
        }

        if (definitionName.StartsWith("GLA"))
        {
            return "GLABarracks";
        }

        return "AmericaBarracks";
    }
}
