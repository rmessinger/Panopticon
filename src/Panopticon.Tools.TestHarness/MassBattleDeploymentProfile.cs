using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Deployment scenario data for the mass-battle map (WI-M0-03): zone rectangles, airfield
/// anchors, and probe rosters as committed JSON. Placement data only; the map itself is never
/// edited or committed, and the profile records the map's provenance.
/// </summary>
public sealed record MassBattleDeploymentProfile
{
    public MassBattleMapProfile Map { get; init; } = new();

    public List<MassBattleSideProfile> Sides { get; init; } = new();

    public MassBattleCapacityProfile Capacity { get; init; } = new();

    public MassBattleReachabilityProfile Reachability { get; init; } = new();

    public static MassBattleDeploymentProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<MassBattleDeploymentProfile>(
            File.ReadAllText(path),
            JsonOptions);
        return profile
            ?? throw new InvalidOperationException($"Deployment profile '{path}' deserialized to null.");
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

public sealed record MassBattleMapProfile
{
    public string Name { get; init; } = "";

    public string Path { get; init; } = "";

    public string Provenance { get; init; } = "";
}

/// <summary>
/// One army: a deployment zone rectangle (world units, map coordinate space), the facing toward
/// the opponent in degrees (yaw 0 faces +X), and airfield anchors behind the zone (on the far
/// side from the opposing zone).
/// </summary>
public sealed record MassBattleSideProfile
{
    public string Id { get; init; } = "";

    public string Side { get; init; } = "FactionAmerica";

    public string Color { get; init; } = "409FFF";

    public MassBattleRect Zone { get; init; } = new();

    public float FacingDegrees { get; init; }

    public string AirfieldDefinition { get; init; } = "AmericaAirfield";

    public List<MassBattlePoint> AirfieldAnchors { get; init; } = new();
}

public sealed record MassBattleRect
{
    public float X { get; init; }

    public float Y { get; init; }

    public float Width { get; init; }

    public float Height { get; init; }

    public float CenterX => X + Width / 2f;

    public float CenterY => Y + Height / 2f;

    public bool Contains(float px, float py) =>
        px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}

public sealed record MassBattlePoint
{
    public float X { get; init; }

    public float Y { get; init; }
}

public sealed record MassBattleUnitBlock
{
    public string Definition { get; init; } = "";

    public int Count { get; init; }
}

/// <summary>
/// Capacity probe: spawn <see cref="UnitsPerSide"/> units per side as blocks laid out row-major
/// inside the zone at a pitch of <see cref="SpacingFactor"/> times the unit's footprint width
/// (2 x MajorRadius), floored at <see cref="MinPitch"/>.
/// </summary>
public sealed record MassBattleCapacityProfile
{
    public int UnitsPerSide { get; init; } = 1000;

    public float SpacingFactor { get; init; } = 2.0f;

    public float MinPitch { get; init; } = 25f;

    public List<MassBattleUnitBlock> Blocks { get; init; } = new();

    /// <summary>
    /// Optional aircraft per side, spawned parked at the side's airfields instead of inside the
    /// zone (WI-M1-01). Null means the ground blocks alone must sum to <see cref="UnitsPerSide"/>
    /// (the M0 deployment proof contract).
    /// </summary>
    public MassBattleAircraftProfile? Aircraft { get; init; }

    public int GroundUnitsPerSide => Blocks.Sum(block => block.Count);

    public int TotalUnitsPerSide => GroundUnitsPerSide + (Aircraft?.Count ?? 0);
}

public sealed record MassBattleAircraftProfile
{
    public string Definition { get; init; } = "AmericaJetAurora";

    public int Count { get; init; }
}

public sealed record MassBattleReachabilityProfile
{
    /// <summary>TargetPoints empty plus a final position within this radius of the target is an arrival.</summary>
    public float ArrivalRadius { get; init; } = 60f;

    /// <summary>Frame budget = distance / MaxSpeed times this factor, plus MinTimeoutFrames.</summary>
    public float MaxSpeedSafetyFactor { get; init; } = 2.0f;

    public int MinTimeoutFrames { get; init; } = 900;

    /// <summary>
    /// Ceiling for the computed per-probe budget, so a near-zero-speed representative cannot
    /// stretch the run into millions of frames. Slow classes that cannot cross within the cap
    /// fail their probe, which is the correct verdict for a mass-battle corridor.
    /// </summary>
    public int MaxFrameBudget { get; init; } = 30_000;
}
