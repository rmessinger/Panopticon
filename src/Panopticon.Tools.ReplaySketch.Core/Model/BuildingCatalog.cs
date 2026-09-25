using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// A building definition that can be targeted by a <see cref="ActionType.Construct"/> action.
/// </summary>
public sealed record BuildingOption(string DefinitionName, string DisplayName);

/// <summary>
/// Known buildable structures per faction for the Generals / Zero Hour scope.
/// Keyed by the <see cref="PlayerSlotConfig.FactionIndex"/> values used in replay metadata.
/// </summary>
public static class BuildingCatalog
{
    // ── USA (factionIndex 2) ──────────────────────────────────────────────────
    private static readonly IReadOnlyList<BuildingOption> UsaBuildings =
    [
        new("AmericaCommandCenter",  "Command Center"),
        new("AmericaBarracks",       "Barracks"),
        new("AmericaWarFactory",     "War Factory"),
        new("AmericaSupplyCenter",   "Supply Center"),
        new("AmericaPowerPlant",     "Power Plant"),
        new("AmericaAirfield",       "Airfield"),
        new("AmericaStrategyCenter", "Strategy Center"),
    ];

    // ── GLA (factionIndex 4) ──────────────────────────────────────────────────
    private static readonly IReadOnlyList<BuildingOption> GlaBuildings =
    [
        new("GLACommandCenter",  "Command Center"),
        new("GLABarracks",       "Barracks"),
        new("GLAArmsDealer",     "Arms Dealer"),
        new("GLASupplyStash",    "Supply Stash"),
        new("GLATunnelNetwork",  "Tunnel Network"),
        new("GLABlackMarket",    "Black Market"),
    ];

    // ── China (factionIndex 3) ────────────────────────────────────────────────
    private static readonly IReadOnlyList<BuildingOption> ChinaBuildings =
    [
        new("ChinaCommandCenter",  "Command Center"),
        new("ChinaBarracks",       "Barracks"),
        new("ChinaWarFactory",     "War Factory"),
        new("ChinaSupplyCenter",   "Supply Center"),
        new("ChinaPowerPlant",     "Nuclear Reactor"),
        new("ChinaInternetCenter", "Internet Center"),
        new("ChinaPropagandaCenter", "Propaganda Center"),
    ];

    /// <summary>
    /// Returns the ordered list of buildings available to the given faction.
    /// Falls back to an empty list for unrecognised faction indices.
    /// </summary>
    public static IReadOnlyList<BuildingOption> ForFaction(int factionIndex) => factionIndex switch
    {
        2 => UsaBuildings,
        3 => ChinaBuildings,
        4 => GlaBuildings,
        // General-specific factions inherit from their base faction
        5 or 9 or 10 or 11 or 12 => UsaBuildings,  // USA Generals
        6 or 7 or 8 => GlaBuildings,                // GLA Generals
        _ => [],
    };

    /// <summary>
    /// Returns the friendly display name for a definition name within the given faction.
    /// Falls back to the raw definition name if not found.
    /// </summary>
    public static string GetDisplayName(string definitionName, int factionIndex)
    {
        foreach (var b in ForFaction(factionIndex))
        {
            if (b.DefinitionName == definitionName)
                return b.DisplayName;
        }

        return definitionName;
    }
}
