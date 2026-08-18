using System.Collections.Generic;
using System.Linq;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// A trainable unit definition that can be targeted by a <see cref="ActionType.RecruitBasicUnit"/> action.
/// <paramref name="zeroHourOnly"/> marks entries whose definition exists only in Zero Hour, so a
/// run against a vanilla Generals install skips them instead of reporting a resolution gap.
/// <paramref name="targetOverride"/> names a per-unit attack target definition that replaces the
/// scenario's shared target for this unit, for weapons that cannot damage the shared target
/// (e.g. a sniper rifle versus TruckArmor); the override must be damageable by the unit's
/// primary weapon, enforced by the TargetOverrides data test.
/// </summary>
public sealed record UnitOption(string DefinitionName, string DisplayName, bool ZeroHourOnly = false, string? TargetOverride = null);

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
        new("AmericaInfantryPathfinder",       "Pathfinder", TargetOverride: "GLAInfantryRebel"),
        new("AmericaInfantryMissileDefender",  "Missile Defender"),
        // Vehicles (War Factory)
        new("AmericaVehicleHumvee",            "Humvee"),
        new("AmericaVehicleMedic",             "Ambulance"),
        new("AmericaTankCrusader",             "Crusader Tank"),
        new("AmericaTankPaladin",              "Paladin Tank"),
        new("AmericaVehicleTomahawk",          "Tomahawk"),
        // Air (Airfield)
        new("AmericaVehicleComanche",          "Comanche"),
        new("AmericaJetAurora",                "Aurora Bomber"),
    ];

    // ── GLA (factionIndex 4) ──────────────────────────────────────────────────
    private static readonly IReadOnlyList<UnitOption> GlaUnits =
    [
        // Infantry (Barracks)
        new("GLAInfantryRebel",       "Rebel"),
        new("GLAInfantryTunnelDefender", "RPG Trooper"),
        new("GLAInfantryHijacker",    "Hijacker"),
        new("GLAInfantryTerrorist",   "Terrorist"),
        new("GLAInfantryAngryMobNexus", "Angry Mob"),
        // Vehicles (Arms Dealer)
        new("GLAVehicleTechnical",    "Technical"),
        new("GLAVehicleQuadCannon",   "Quad Cannon"),
        new("GLAVehicleRocketBuggy",  "Rocket Buggy"),
        new("GLATankScorpion",        "Scorpion Tank"),
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
        new("ChinaTankBattleMaster",        "Battlemaster Tank"),
        new("ChinaTankDragon",              "Dragon Tank"),
        new("ChinaTankGattling",            "Gattling Tank"),
        new("ChinaVehicleTroopCrawler",     "Troop Crawler"),
        new("ChinaVehicleNukeLauncher",     "Nuke Cannon"),
        new("ChinaTankOverlord",            "Overlord Tank"),
        // Air (Airfield)
        new("ChinaJetMIG",                  "MiG Fighter"),
        new("ChinaVehicleHelix",            "Helix", ZeroHourOnly: true),
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
