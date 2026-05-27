using System.Collections.Generic;
using System.Linq;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// A trainable unit definition that can be targeted by a <see cref="ActionType.RecruitBasicUnit"/> action.
/// </summary>
public sealed record UnitOption(string DefinitionName, string DisplayName);

/// <summary>
/// Known trainable units per faction for the Generals / Zero Hour scope.
/// Keyed by the <see cref="PlayerSlotConfig.FactionIndex"/> values used in replay metadata.
/// </summary>
public static class UnitCatalog
{
    // ── USA (factionIndex 2) ──────────────────────────────────────────────────
    private static readonly IReadOnlyList<UnitOption> UsaUnits =
    [
        // Infantry (Barracks)
        new("AmericaInfantryRanger",          "Ranger"),
        new("AmericaInfantryPathfinder",       "Pathfinder"),
        new("AmericaInfantryMissileDefender",  "Missile Defender"),
        // Vehicles (War Factory)
        new("AmericaVehicleHumvee",            "Humvee"),
        new("AmericaVehicleAmbulance",         "Ambulance"),
        new("AmericaVehicleCrusaderTank",      "Crusader Tank"),
        new("AmericaVehiclePaladinTank",       "Paladin Tank"),
        new("AmericaVehicleTomahawk",          "Tomahawk"),
        // Air (Airfield)
        new("AmericaVehicleComanche",          "Comanche"),
        new("AmericaVehicleAuroraAlpha",       "Aurora Bomber"),
    ];

    // ── GLA (factionIndex 4) ──────────────────────────────────────────────────
    private static readonly IReadOnlyList<UnitOption> GlaUnits =
    [
        // Infantry (Barracks)
        new("GLAInfantryRebel",       "Rebel"),
        new("GLAInfantryRPGTrooper",  "RPG Trooper"),
        new("GLAInfantryHijacker",    "Hijacker"),
        new("GLAInfantryTerrorist",   "Terrorist"),
        new("GLAInfantryAngryMob",    "Angry Mob"),
        // Vehicles (Arms Dealer)
        new("GLAVehicleTechnical",    "Technical"),
        new("GLAVehicleQuadCannon",   "Quad Cannon"),
        new("GLAVehicleRocketBuggy",  "Rocket Buggy"),
        new("GLAVehicleScorpionTank", "Scorpion Tank"),
        new("GLAVehicleSCUDLauncher", "SCUD Launcher"),
    ];

    // ── China (factionIndex 3) ────────────────────────────────────────────────
    private static readonly IReadOnlyList<UnitOption> ChinaUnits =
    [
        // Infantry (Barracks)
        new("ChinaInfantryRedGuard",        "Red Guard"),
        new("ChinaInfantryTankHunter",      "Tank Hunter"),
        new("ChinaInfantryBlackLotus",      "Black Lotus"),
        new("ChinaInfantryHacker",          "Hacker"),
        // Vehicles (War Factory)
        new("ChinaVehicleBattlemasterTank", "Battlemaster Tank"),
        new("ChinaVehicleDragonTank",       "Dragon Tank"),
        new("ChinaVehicleGattlingTank",     "Gattling Tank"),
        new("ChinaVehicleTroopCrawler",     "Troop Crawler"),
        new("ChinaVehicleNukeLauncher",     "Nuke Cannon"),
        new("ChinaVehicleOverlord",         "Overlord Tank"),
        // Air (Airfield)
        new("ChinaVehicleMiG",              "MiG Fighter"),
        new("ChinaVehicleHelix",            "Helix"),
    ];

    /// <summary>
    /// Returns the ordered list of trainable units available to the given faction.
    /// Falls back to an empty list for unrecognised faction indices.
    /// </summary>
    public static IReadOnlyList<UnitOption> ForFaction(int factionIndex) => factionIndex switch
    {
        2 => UsaUnits,
        3 => ChinaUnits,
        4 => GlaUnits,
        // General-specific factions inherit from their base faction
        5 or 9 or 10 or 11 or 12 => UsaUnits,  // USA Generals
        6 or 7 or 8 => GlaUnits,                // GLA Generals
        _ => [],
    };

    /// <summary>
    /// Returns the friendly display name for a definition name within the given faction.
    /// Falls back to the raw definition name if not found.
    /// </summary>
    public static string GetDisplayName(string definitionName, int factionIndex)
    {
        foreach (var u in ForFaction(factionIndex))
        {
            if (u.DefinitionName == definitionName)
                return u.DisplayName;
        }

        return definitionName;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="definitionName"/> appears in any
    /// faction's unit list — used by <see cref="Services.ReplayActionMonitor"/> to recognise
    /// any recruited unit regardless of which faction is playing.
    /// </summary>
    public static bool ContainsDefinition(string definitionName) =>
        UsaUnits.Any(u => u.DefinitionName == definitionName) ||
        GlaUnits.Any(u => u.DefinitionName == definitionName) ||
        ChinaUnits.Any(u => u.DefinitionName == definitionName);
}
