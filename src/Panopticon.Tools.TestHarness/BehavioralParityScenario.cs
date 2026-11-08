using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// WI-H5-05: one behavioral-parity scenario. Declarative input for
/// <see cref="BehavioralParityRunner"/>: a representative legacy map (resolved against the game
/// installation), a roster spec (definition plus per-side count), one fixed seed, the run
/// schedule, and an assertion-set reference naming the end-state paths compared across runs.
/// </summary>
public sealed class BehavioralParityScenario
{
    /// <summary>Scenario name; carried into the report for provenance.</summary>
    public string Name { get; set; } = "h5-parity";

    /// <summary>Legacy .map path inside the installation file system (the original-content side).</summary>
    public string SourceMapPath { get; set; } = "maps/alpine assault/alpine assault.map";

    /// <summary>Fixed seed for every run, both sides, both repeats.</summary>
    public uint Seed { get; set; } = 20261108;

    /// <summary>Logic frames run after spawning before the first orders issue.</summary>
    public int SettleFrames { get; set; } = 60;

    /// <summary>Measured engagement budget in logic frames (orders re-issue every refresh).</summary>
    public int EngagementFrames { get; set; } = 900;

    /// <summary>Frames between general-attack re-issues.</summary>
    public int OrderRefreshInterval { get; set; } = 120;

    /// <summary>Which named end-state assertion set the comparison evaluates ("default" today).</summary>
    public string AssertionSet { get; set; } = "default";

    /// <summary>Roster spec: each entry spawns Count units per side, identically placed.</summary>
    public List<BehavioralParityRosterEntry> Roster { get; set; } = [];

    /// <summary>Optional directory for PNG captures of the converted-content run.</summary>
    public string? CaptureDirectory { get; set; }

    /// <summary>Capture one PNG every Nth logic frame of the converted run (1 = every frame).</summary>
    public int CaptureStride { get; set; } = 15;

    public static BehavioralParityScenario Default() => new()
    {
        Roster =
        [
            // The P6b reference roster (WI-H4-05 / WI-P6b archetypes, USA reference units),
            // superweapon first so its large footprint claims the first row.
            new BehavioralParityRosterEntry { Definition = "AmericaParticleCannonUplink", Count = 1 },
            new BehavioralParityRosterEntry { Definition = "AmericaInfantryRanger", Count = 2 },
            new BehavioralParityRosterEntry { Definition = "AmericaTankCrusader", Count = 2 },
            new BehavioralParityRosterEntry { Definition = "AmericaVehicleComanche", Count = 1 },
            new BehavioralParityRosterEntry { Definition = "AmericaVehicleDozer", Count = 1 },
            new BehavioralParityRosterEntry { Definition = "AmericaVehicleMedic", Count = 1 },
        ],
    };

    /// <summary>Loads a scenario JSON file; missing roster entries fall back to the default roster.</summary>
    public static BehavioralParityScenario Load(string path)
    {
        var json = File.ReadAllText(path);
        var scenario = JsonSerializer.Deserialize<BehavioralParityScenario>(
            json,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true });
        if (scenario is null)
        {
            throw new InvalidDataException($"Parity scenario '{path}' parsed to null.");
        }

        if (scenario.Roster.Count == 0)
        {
            scenario.Roster = Default().Roster;
        }

        return scenario;
    }
}

public sealed class BehavioralParityRosterEntry
{
    [JsonPropertyName("definition")]
    public string Definition { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    public override string ToString() => $"{Definition} x{Count}";
}
