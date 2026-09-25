using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>A structure placed on the map by its author (e.g. the civilian town), not by the plan.</summary>
public sealed record MapStructure(string TypeName, Vector3 Position);

/// <summary>
/// Classifies map-placed civilian structure families for the validation-ground map view, so the
/// formation can be hand-placed clear of the town. Name-prefix matching against the Generals
/// civilian structure families; map objects carry no owner or kind, and resolving
/// <c>ObjectDefinitions</c> would need a full game instance, which the map view deliberately
/// avoids. Extend the prefix list when a map uses an unlisted family.
/// </summary>
public static class CivilianStructureCatalog
{
    private static readonly IReadOnlyList<string> Prefixes =
    [
        "GermanTownHouse",
        "GermanPub",
        // The shipped data contains both spellings (GermanCotage01 and GermanCottageBib1).
        "GermanCotage",
        "GermanCottage",
        "CivilianBunker",
        "StanSchoolhouse",
        "StanGasStation",
        "QuonsetHut",
        "CivilianTEROilDepot",
        // Neutral supply structures (docs/worldbuilder-requirements.md §8.1): if left unlisted,
        // cluster placement can pack a faction's roster on top of one (observed on Alpine
        // Assault's southern SupplyDock), since these otherwise carry no owner/kind the map view
        // could use to treat them as obstacles.
        "SupplyDock",
        "SupplyPile",
        "SupplyWarehouse",
    ];

    public static bool IsCivilianStructure(string typeName) =>
        Prefixes.Any(p => typeName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
