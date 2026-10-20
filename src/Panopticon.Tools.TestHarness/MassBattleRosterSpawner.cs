#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Data;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Tools.ReplaySketch.Model.Capture;

// The Navigation class shares its name with its namespace; alias it to keep call sites readable.
using NavigationGrid = Panopticon.Navigation.Navigation;

namespace Panopticon.Tools.TestHarness;

/// <summary>One roster entry's spawn outcome, keyed by the definition name it spawns.</summary>
public sealed record RosterEntrySpawnResult(
    string Definition,
    int Requested,
    int Spawned,
    List<string> FailureReasons);

/// <summary>One side's roster spawn outcome (WI-M5-02).</summary>
public sealed record RosterSideSpawnResult
{
    public string SideId { get; init; } = "";

    public string RosterId { get; init; } = "";

    public int AirfieldsPlaced { get; init; }

    public List<RosterEntrySpawnResult> GroundEntries { get; init; } = [];

    public List<RosterEntrySpawnResult> AircraftEntries { get; init; } = [];

    public List<GameObject> Units { get; init; } = [];

    public List<string> FailureReasons { get; init; } = [];

    public bool AllGroundSpawned => GroundEntries.All(entry => entry.Spawned == entry.Requested);

    public bool AllAircraftParked => AircraftEntries.All(entry => entry.Spawned == entry.Requested);
}

/// <summary>
/// WI-M5-02: spawns each side's roster manifest, Generals and EE2 rows together, on the mass
/// battle map from one deployment profile. EE2 rows import first through the shared H2a layer;
/// ground entries walk the zone layout in manifest order and aircraft entries park at the side's
/// airfields. Capacity blocks stay with the M0 proof and M1 probe.
/// </summary>
public static class MassBattleRosterSpawner
{
    /// <summary>Shipped battle profile (WI-M5-02): both sides spawn their M3 roster manifests.</summary>
    public const string DefaultBattleProfileFileName = "mass-battle-deepdivide-battle.json";

    /// <summary>
    /// Validates roster mode: every side carries a roster id or none does, every id exists in
    /// the manifest, and roster mode is never mixed with capacity blocks.
    /// </summary>
    public static string? Validate(MassBattleDeploymentProfile profile, RosterManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifest);

        var rosterSides = profile.Sides.Count(side => side.Roster != null);
        if (rosterSides == 0)
        {
            return null;
        }

        if (rosterSides != profile.Sides.Count)
        {
            return "Roster mode needs a roster id on every side; mixed roster and capacity-block sides are not supported.";
        }

        if (profile.Capacity.Blocks.Count > 0 || profile.Capacity.Aircraft != null)
        {
            return "Roster mode cannot also author capacity blocks or aircraft; the manifest is the spawn source.";
        }

        foreach (var side in profile.Sides)
        {
            if (manifest.Sides.All(manifestSide => manifestSide.Id != side.Roster))
            {
                return $"Side '{side.Id}' references roster '{side.Roster}', which the manifest does not define.";
            }
        }

        return null;
    }

    /// <summary>The definition name a manifest entry spawns: ee2 rows publish as ee2:&lt;RowKey&gt;.</summary>
    public static string DefinitionName(RosterEntry entry) =>
        entry.Source == "ee2" ? $"ee2:{entry.Definition}" : entry.Definition;

    /// <summary>The side's ground entries as spawn blocks, in manifest order.</summary>
    public static List<MassBattleUnitBlock> GroundBlocks(RosterSide side) =>
        side.Entries
            .Where(entry => !entry.Aircraft)
            .Select(entry => new MassBattleUnitBlock { Definition = DefinitionName(entry), Count = entry.Count })
            .ToList();

    /// <summary>The side's aircraft entries as (definition name, count) pairs, in manifest order.</summary>
    public static List<(string Definition, int Count)> AircraftEntries(RosterSide side) =>
        side.Entries
            .Where(entry => entry.Aircraft)
            .Select(entry => (DefinitionName(entry), entry.Count))
            .ToList();

    /// <summary>
    /// Imports the manifest's ee2 rows through the shared H2a layer before spawning. The
    /// profile's content-import block carries the policy; its row keys are the union of both
    /// rosters' ee2 entries.
    /// </summary>
    public static bool ImportEe2Content(
        Game game,
        MassBattleDeploymentProfile profile,
        RosterManifest manifest,
        ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(reporter);

        var rowKeys = manifest.Sides
            .SelectMany(side => side.Entries)
            .Where(entry => entry.Source == "ee2")
            .Select(entry => entry.Definition)
            .Distinct()
            .ToList();
        if (rowKeys.Count == 0 || profile.ContentImport is null)
        {
            return true;
        }

        var import = profile.ContentImport;
        import.RowKeys = rowKeys;
        if (!UnitContentImportRunner.Import(game, import, reporter))
        {
            reporter.Error($"EE2 content import failed for roster rows: {string.Join(", ", rowKeys)}.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Spawns one side's manifest: ground entries through the zone layout walk, aircraft parked
    /// at the side's airfields. An unresolved definition is a per-entry failure reason, not a
    /// skip; every aircraft parks with JetAIUpdate.Base set or the shortfall is reported.
    /// </summary>
    public static RosterSideSpawnResult SpawnSide(
        Game game,
        MassBattleDeploymentProfile profile,
        int sideIndex,
        Player owner,
        RosterSide manifestSide,
        NavigationGrid navigation,
        ITestHarnessReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifestSide);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(reporter);

        var side = profile.Sides[sideIndex];
        var result = new RosterSideSpawnResult { SideId = side.Id, RosterId = manifestSide.Id };

        var groundBlocks = GroundBlocks(manifestSide);
        var expectedGround = groundBlocks.Sum(block => block.Count);
        var ground = MassBattlePlacement.SpawnGroundArmy(
            game, profile, sideIndex, owner, groundBlocks, navigation, reporter, expectedGround);
        result.FailureReasons.AddRange(ground.FailureReasons.Select(reason => $"{side.Id}: {reason}"));

        // Imported rows publish under their bare row key inside the ee2 source store, so the
        // per-definition accounting matches on the name past any source prefix.
        var spawnedByDefinition = ground.Units.GroupBy(unit => unit.Definition.Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var block in groundBlocks)
        {
            var separator = block.Definition.IndexOf(':');
            var nativeName = separator > 0 ? block.Definition[(separator + 1)..] : block.Definition;
            var spawned = spawnedByDefinition.GetValueOrDefault(nativeName);
            // Per-entry reasons: the zone walk attributes zone exhaustion to the block; slots
            // skipped as impassable inside a completed walk are the remaining gap.
            var entryReasons = ground.FailureReasons
                .Where(reason => reason.Contains($"'{block.Definition}'"))
                .ToList();
            if (spawned != block.Count && entryReasons.Count == 0)
            {
                entryReasons.Add("impassable slot skips within the zone walk");
            }

            result.GroundEntries.Add(new RosterEntrySpawnResult(block.Definition, block.Count, spawned, entryReasons));
        }

        var (airfields, airfieldReasons) = MassBattlePlacement.PlaceAirfields(
            game, profile, sideIndex, owner, navigation, reporter);
        result.FailureReasons.AddRange(airfieldReasons.Select(reason => $"{side.Id}: {reason}"));
        result = result with { AirfieldsPlaced = airfields.Count };
        var parking = MassBattlePlacement.ParkingPlaces(airfields);

        foreach (var (definitionName, count) in AircraftEntries(manifestSide))
        {
            var definition = MassBattlePlacement.ResolveDefinition(game, definitionName);
            var parked = 0;
            var entryReasons = new List<string>();
            if (definition == null)
            {
                entryReasons.Add($"{side.Id}: unresolved aircraft definition '{definitionName}'");
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var jet = MassBattlePlacement.ParkAircraft(game, owner, definition, parking);
                    if (jet == null)
                    {
                        entryReasons.Add(
                            $"{side.Id}: no free parking slot for aircraft {i + 1} of {count} of '{definitionName}'");
                        break;
                    }

                    result.Units.Add(jet);
                    parked++;
                }
            }

            result.AircraftEntries.Add(new RosterEntrySpawnResult(definitionName, count, parked, entryReasons));
            result.FailureReasons.AddRange(entryReasons);
        }

        result.Units.InsertRange(0, ground.Units);
        return result;
    }
}
