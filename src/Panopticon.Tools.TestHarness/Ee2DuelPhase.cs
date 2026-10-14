using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-M3-03: EE2-versus-Generals archetype duels inside the validation ground's game
/// session. Each duel spawns one published EE2 unit and one native Generals reference, orders
/// them to engage, and measures the frames until the first death; the result must land within
/// the duel's authored time-to-kill bounds. Results are machine-readable
/// (<c>ee2-duel-report.json</c>), keyed by duel name.
/// </summary>
public static class Ee2DuelPhase
{
    public static int Run(Game game, Ee2ValidationProfile profile, ITestHarnessReporter reporter)
    {
        var report = RunDuels(game, profile, reporter);
        var path = Path.Combine(AppContext.BaseDirectory, "ee2-duel-report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, Ee2ValidationProfile.Options));
        reporter.Info($"EE2 duel report saved to: {path}");
        reporter.Info($"EE2 duels: {report.Duels.Count(d => d.Passed)}/{report.Duels.Count} passed.");
        foreach (var failed in report.Duels.Where(d => !d.Passed))
        {
            reporter.Error($"Duel '{failed.Name}' failed: {failed.FailureReason}");
        }

        return report.AllPassed ? 0 : 5;
    }

    /// <summary>Runs the duels and returns the report without writing it (test seam).</summary>
    public static Ee2DuelReport RunDuels(Game game, Ee2ValidationProfile profile, ITestHarnessReporter reporter)
    {
        var duels = new List<Ee2DuelResult>();
        var origin = new Vector3(1200f, 900f, 0f);
        foreach (var duel in profile.Duels)
        {
            duels.Add(RunDuel(game, duel, origin, reporter));
            origin += new Vector3(400f, 0f, 0f);
            Ee2ContractPhase.ClearSpawned(game);
        }

        return new Ee2DuelReport
        {
            Profile = profile.Name,
            Duels = duels,
            AllPassed = duels.All(d => d.Passed),
        };
    }

    private static Ee2DuelResult RunDuel(Game game, Ee2DuelEntry duel, Vector3 origin, ITestHarnessReporter reporter)
    {
        var ee2 = Ee2ContractPhase.ResolveDefinition(game, duel.Ee2Definition);
        var generals = game.AssetStore.ObjectDefinitions.GetByName(duel.GeneralsDefinition);
        if (ee2 == null || generals == null)
        {
            return Failed(duel, $"definition not found: '{duel.Ee2Definition}' or '{duel.GeneralsDefinition}'");
        }

        var owner = game.Scene3D.Players[2];
        var hostile = game.Scene3D.Players[3];

        var ee2Position = GroundPosition(game, origin);
        var generalsPosition = GroundPosition(game, origin + new Vector3(duel.Distance, 0f, 0f));
        var ee2Unit = Ee2ContractPhase.SpawnAt(game, ee2, owner, ee2Position);
        var generalsUnit = Ee2ContractPhase.SpawnAt(game, generals, hostile, generalsPosition);
        FaceEachOther(ee2Unit, generalsUnit);

        if (duel.Kind == Ee2DuelKind.Air)
        {
            // Airborne spawn through the fixed-wing contract pattern: aircraft fly at their
            // authored cruising height (or a hover height for rotorcraft) with the jet state
            // already MovingTowardsTarget, then pursue their weapon target directly.
            LiftAirborne(game, ee2Unit, generalsUnit.Translation);
            LiftAirborne(game, generalsUnit, ee2Unit.Translation);
        }

        // Engagement through the contract phase's killer pattern: a per-frame weapon target on
        // both units. AttackObject orders are not used because the AI's own approach loop fights
        // the direct target on mutually hostile combatants (observed: both sides stall).
        var frames = Ee2ContractPhase.Wait(
            game,
            frame =>
            {
                if (frame % 600 == 0)
                {
                    reporter.Info($"[duel] {duel.Name} f{frame} ee2Hp={ee2Unit.BodyModule?.Health:0} generalsHp={generalsUnit.BodyModule?.Health:0} dist={Vector3.Distance(ee2Unit.Translation, generalsUnit.Translation):0}");
                }

                ee2Unit.CurrentWeapon?.SetTarget(new WeaponTarget(generalsUnit.GameEngine.Scene3D.GameObjects, generalsUnit.Id));
                generalsUnit.CurrentWeapon?.SetTarget(new WeaponTarget(ee2Unit.GameEngine.Scene3D.GameObjects, ee2Unit.Id));
                return ee2Unit.IsEffectivelyDead || generalsUnit.IsEffectivelyDead;
            },
            duel.FrameBudget);

        var seconds = frames / 30.0;
        string? failure = null;
        if (!ee2Unit.IsEffectivelyDead && !generalsUnit.IsEffectivelyDead)
        {
            failure = $"neither unit died within {duel.FrameBudget} frames ({seconds:0.0}s)";
        }
        else if (seconds < duel.MinSeconds)
        {
            failure = $"resolved in {seconds:0.00}s, below the authored minimum {duel.MinSeconds:0.0}s";
        }
        else if (seconds > duel.MaxSeconds)
        {
            failure = $"resolved in {seconds:0.00}s, above the authored maximum {duel.MaxSeconds:0.0}s";
        }

        return new Ee2DuelResult(
            duel.Name,
            duel.Ee2Definition,
            duel.GeneralsDefinition,
            failure)
        {
            Seconds = Math.Round(seconds, 3, MidpointRounding.AwayFromZero),
            Frames = frames,
            Winner = ee2Unit.IsEffectivelyDead
                ? generalsUnit.IsEffectivelyDead ? null : duel.GeneralsDefinition
                : generalsUnit.IsEffectivelyDead ? duel.Ee2Definition : null,
            MinSeconds = duel.MinSeconds,
            MaxSeconds = duel.MaxSeconds,
            Resolved = ee2Unit.IsEffectivelyDead || generalsUnit.IsEffectivelyDead,
        };
    }

    private static Vector3 GroundPosition(Game game, Vector3 position)
    {
        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(position.X, position.Y)) ?? 0f;
        return position with { Z = height };
    }

    private static void FaceEachOther(GameObject a, GameObject b)
    {
        var toB = Vector3.Normalize(b.Translation - a.Translation);
        a.UpdateTransform(a.Translation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(toB.Y, toB.X)));
        b.UpdateTransform(b.Translation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(-toB.Y, -toB.X)));
        a.UpdateColliders();
        b.UpdateColliders();
    }

    /// <summary>Places an aircraft at its authored cruising height, already flying toward the foe.</summary>
    private static void LiftAirborne(Game game, GameObject aircraft, Vector3 foe)
    {
        var cruise = aircraft.Definition.LocomotorSets[LocomotorSetType.Normal].Locomotors?[0].Value?.PreferredHeight ?? 0f;
        var height = MathF.Max(60f, cruise);
        var toFoe = Vector3.Normalize(foe - aircraft.Translation);
        var liftOff = aircraft.Translation - toFoe * 40f;
        aircraft.UpdateTransform(
            liftOff with { Z = (game.Scene3D.Terrain.GetGroundHeight(new Vector2(liftOff.X, liftOff.Y)) ?? 0f) + height },
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(toFoe.Y, toFoe.X)));
        aircraft.UpdateColliders();

        if (aircraft.AIUpdate is JetAIUpdate jetAI)
        {
            jetAI.CurrentJetAIState = JetAIUpdate.JetAIState.MovingTowardsTarget;
        }
    }

    private static Ee2DuelResult Failed(Ee2DuelEntry duel, string reason)
    {
        return new Ee2DuelResult(duel.Name, duel.Ee2Definition, duel.GeneralsDefinition, reason)
        {
            MinSeconds = duel.MinSeconds,
            MaxSeconds = duel.MaxSeconds,
            Resolved = false,
        };
    }
}

/// <summary>One authored duel: the pair, the engagement distance, and the TTK bounds.</summary>
public sealed class Ee2DuelEntry
{
    public string Name { get; set; } = "";

    /// <summary>Source-qualified EE2 definition, for example <c>ee2:FighterUpgradeEpoch15</c>.</summary>
    public string Ee2Definition { get; set; } = "";

    public string GeneralsDefinition { get; set; } = "";

    /// <summary>Air duels spawn both aircraft airborne at their cruising heights.</summary>
    [JsonConverter(typeof(KindConverter))]
    public Ee2DuelKind Kind { get; set; } = Ee2DuelKind.Ground;

    public sealed class KindConverter : JsonConverter<Ee2DuelKind>
    {
        public override Ee2DuelKind Read(ref Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "Ground" => Ee2DuelKind.Ground,
                "Air" => Ee2DuelKind.Air,
                var value => throw new JsonException($"Unknown duel kind '{value}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, Ee2DuelKind value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>Initial ground separation in world units.</summary>
    public float Distance { get; set; } = 160f;

    public int FrameBudget { get; set; } = 9000;

    /// <summary>Authored time-to-kill bounds in seconds; a duel outside them fails.</summary>
    public double MinSeconds { get; set; }

    public double MaxSeconds { get; set; } = 120;
}

public enum Ee2DuelKind
{
    Ground,
    Air,
}

public sealed record Ee2DuelReport
{
    public string Profile { get; init; } = "";

    public bool AllPassed { get; init; }

    public List<Ee2DuelResult> Duels { get; init; } = [];
}

/// <summary>One duel's result; null FailureReason means every check passed.</summary>
public sealed class Ee2DuelResult
{
    public Ee2DuelResult(string name, string ee2Definition, string generalsDefinition, string? failureReason = null)
    {
        Name = name;
        Ee2Definition = ee2Definition;
        GeneralsDefinition = generalsDefinition;
        FailureReason = failureReason;
    }

    public string Name { get; }

    public string Ee2Definition { get; }

    public string GeneralsDefinition { get; }

    public string? FailureReason { get; }

    public bool Passed => FailureReason is null;

    public double? Seconds { get; init; }

    public int? Frames { get; init; }

    /// <summary>The surviving side's definition; null until the duel resolves.</summary>
    public string? Winner { get; init; }

    public double? MinSeconds { get; init; }

    public double? MaxSeconds { get; init; }

    public bool Resolved { get; init; }
}
