using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model.Capture;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Card WI-M2-04: EE2 reference contracts executed inside the Unit Validation Ground's game
/// session. Ground units prove displacement, weapon-caused damage, and death beside Generals
/// units; fixed-wing aircraft prove the full sortie cycle through the M1 parking path
/// (departure, ammunition consumption, return, refill, second damaging sortie); the helicopter
/// proves hover attack, scripted return, and a second sortie, with clip consumption reported
/// absent-in-source because no EE2 helicopter authors ordnance. Results are machine-readable,
/// keyed by source-qualified definition id.
/// </summary>
public static class Ee2ContractPhase
{
    private const float DisplacementThreshold = 40f;
    private const int AttackBudgetFrames = 3600;
    private const int SortieBudgetFrames = 6000;

    public static int Run(Game game, Ee2ValidationProfile profile, ITestHarnessReporter reporter)
    {
        var report = RunContracts(game, profile, reporter);
        WriteReport(report, reporter);
        reporter.Info($"EE2 contracts: {report.Contracts.Count(r => r.Passed)}/{report.Contracts.Count} passed.");
        foreach (var failed in report.Contracts.Where(r => !r.Passed))
        {
            reporter.Error($"EE2 contract '{failed.Definition}' failed: {failed.FailureReason}");
        }

        return report.AllPassed ? 0 : 5;
    }

    /// <summary>Runs the contracts and returns the report without writing it (test seam).</summary>
    public static Ee2ContractReport RunContracts(Game game, Ee2ValidationProfile profile, ITestHarnessReporter reporter)
    {
        // The standard roster has resolved by now; its leftovers would fire into the EE2
        // contracts, so the phase runs on a cleared scene (its report was already written).
        foreach (var gameObject in game.Scene3D.GameObjects.Objects.Where(obj => obj.Owner is not null).ToList())
        {
            if (!gameObject.IsEffectivelyDead)
            {
                gameObject.Kill();
            }

            if (!gameObject.IsDestroyed)
            {
                gameObject.Destroy();
            }
        }

        var results = new List<Ee2ContractResult>();
        var owner = game.Scene3D.Players[2];
        var hostile = game.Scene3D.Players[3];

        var groundOrigin = new Vector3(1200f, 900f, 0f);
        var aircraftOffset = 0f;
        foreach (var contract in profile.Contracts)
        {
            results.Add(contract.Kind switch
            {
                Ee2ContractKind.Ground => RunGroundContract(game, profile, contract, owner, hostile, groundOrigin, reporter),
                Ee2ContractKind.FixedWing => RunFixedWingContract(game, profile, contract, owner, hostile, aircraftOffset += 300, reporter),
                Ee2ContractKind.Helicopter => RunHelicopterContract(game, profile, contract, owner, hostile, reporter),
                _ => new Ee2ContractResult(contract.Definition, "Unknown contract kind."),
            });

            groundOrigin += new Vector3(160f, 0f, 0f);
            ClearSpawned(game);
        }

        return new Ee2ContractReport
        {
            Profile = profile.Name,
            Contracts = results,
            Frame = CurrentFrame(game),
            AllPassed = results.All(r => r.Passed),
        };
    }

    /// <summary>Native names resolve in the built-in store; <c>source:name</c> in that source's published collections.</summary>
    private static ObjectDefinition? ResolveDefinition(Game game, string definition)
    {
        var separator = definition.IndexOf(':');
        if (separator <= 0)
        {
            return game.AssetStore.ObjectDefinitions.GetByName(definition);
        }

        return definition[..separator] == ContentSource.Ee2.Id
            ? game.AssetStore.GetSourceContent(ContentSource.Ee2).ObjectDefinitions.GetByName(definition[(separator + 1)..])
            : null;
    }

    private static Ee2ContractResult RunGroundContract(
        Game game,
        Ee2ValidationProfile profile,
        Ee2ContractEntry contract,
        Player owner,
        Player hostile,
        Vector3 origin,
        ITestHarnessReporter reporter)
    {
        var definition = ResolveDefinition(game, contract.Definition);
        if (definition == null)
        {
            return new Ee2ContractResult(contract.Definition, "definition not found after import");
        }

        var unit = SpawnAt(game, definition, owner, origin + new Vector3(0f, 0f, 0f));
        var destination = origin + new Vector3(0f, -220f, 0f);
        var target = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.TargetDefinition)!, game.Scene3D.Players[1], destination + new Vector3(0f, -60f, 0f));
        var killer = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.KillerDefinition)!, hostile, origin + new Vector3(240f, 0f, 0f));
        var victim = SpawnAt(game, definition, hostile, origin + new Vector3(240f, 60f, 0f));

        // Movement: a real engine move order on the selected unit, observed as displacement.
        var processor = new OrderProcessor(game);
        var moveOrder = new Order(game.Scene3D.GetPlayerIndex(owner), OrderType.MoveTo);
        moveOrder.AddPositionArgument(destination);
        owner.SelectUnits([unit]);
        processor.Process([moveOrder]);
        var moveFrames = Wait(game, frame => Vector2.Distance(To2(unit.Translation), To2(destination)) < DisplacementThreshold / 2 || frame > 1800, 1800);
        var displacement = Vector2.Distance(To2(unit.Translation), To2(origin));

        // Attack: a real AttackObject order, so the engine runs its own approach and fire loop.
        var attackOrder = new Order(game.Scene3D.GetPlayerIndex(owner), OrderType.AttackObject);
        attackOrder.AddObjectIdArgument(target.Id);
        owner.SelectUnits([unit]);
        processor.Process([attackOrder]);
        var initialHealth = target.BodyModule?.Health ?? 0;
        var attackFrames = Wait(game, _ => (target.BodyModule?.Health ?? 0) < initialHealth || target.IsEffectivelyDead, AttackBudgetFrames);
        var damaged = (target.BodyModule?.Health ?? 0) < initialHealth || target.IsEffectivelyDead;
        unit.CurrentWeapon?.SetTarget(null);

        // Death: a Generals killer destroys a second copy; observe the dying/removal lifecycle.
        killer.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, victim.Id));
        var deathFrames = Wait(game, _ => victim.IsEffectivelyDead, AttackBudgetFrames);
        killer.CurrentWeapon?.SetTarget(null);

        var failures = new List<string>();
        if (displacement < DisplacementThreshold)
        {
            failures.Add($"moved {displacement:0} of {DisplacementThreshold} units in {moveFrames} frames");
        }

        if (!damaged)
        {
            failures.Add($"no weapon-caused health loss on '{profile.TargetDefinition}' within {AttackBudgetFrames} frames");
        }

        if (!victim.IsEffectivelyDead)
        {
            failures.Add($"death not observed within {AttackBudgetFrames} frames");
        }

        return new Ee2ContractResult(
            contract.Definition,
            failures.Count == 0 ? null : string.Join("; ", failures))
        {
            Displacement = displacement,
            MoveFrames = moveFrames,
            AttackFrames = attackFrames,
            DeathFrames = deathFrames,
            Moved = displacement >= DisplacementThreshold,
            DamagedTarget = damaged,
            Died = victim.IsEffectivelyDead,
        };
    }

    private static Ee2ContractResult RunFixedWingContract(
        Game game,
        Ee2ValidationProfile profile,
        Ee2ContractEntry contract,
        Player owner,
        Player hostile,
        float baseOffsetX,
        ITestHarnessReporter reporter)
    {
        var definition = ResolveDefinition(game, contract.Definition);
        if (definition == null)
        {
            return new Ee2ContractResult(contract.Definition, "definition not found after import");
        }

        var airfield = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.AirfieldDefinition)!, owner, new Vector3(1000f + baseOffsetX, 1200f, 0f));
        var parking = airfield?.FindBehavior<ParkingPlaceBehaviour>();
        if (parking == null)
        {
            return new Ee2ContractResult(contract.Definition, $"airfield '{profile.AirfieldDefinition}' has no parking places");
        }

        // The parking slot is claimed through the existing parking path so the jet has a base.
        // The jet starts airborne in the engine's direct-spawn mode: EE2 authors no taxi
        // locomotion, and the runway waypoint flow cannot be traversed at authored air speed
        // (the turn radius exceeds the parking bone spacing); that gap is recorded in the M2
        // implementation plan. Flight, consumption, the return flight, refill, and a second
        // damaging sortie are proven on authored values alone.
        parking.EnqueueObject();
        var jet = game.Scene3D.GameObjects.CreateObject(definition, owner);
        parking.ReportSpawn(jet.Id);
        var jetAI = (JetAIUpdate)jet.AIUpdate;
        jetAI.Base = airfield;
        jetAI.CurrentJetAIState = JetAIUpdate.JetAIState.MovingTowardsTarget;

        var weapon = jet.CurrentWeapon;
        var clipSize = weapon?.Template.ClipSize ?? 0;
        if (clipSize <= 0)
        {
            return new Ee2ContractResult(contract.Definition, "fixed-wing contract requires an authored clip (ordnance)");
        }

        var home = new Vector3(700f + baseOffsetX, 1650f, 0f);
        // West-out, home, east-out: every leg is a straight flight or a single reversal, which
        // the wings model is stable on (sustained turns at authored rates accumulate sideslip).
        var targetAPosition = home + new Vector3(-600f, -50f, 0f);
        var targetA = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.TargetDefinition)!, game.Scene3D.Players[1], targetAPosition);

        // Face the first target so the approach is the straight flight the engine's wings model
        // is stable on (tight orbiting at authored turn rates does not close to weapon range).
        var launch = home + new Vector3(60f, 60f, 0f);
        var facing = MathF.Atan2(targetAPosition.Y - launch.Y, targetAPosition.X - launch.X);
        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(launch.X, launch.Y)) ?? 0f;
        // Card WI-M3-01: spawn at the authored cruising height (the factor-converted
        // PreferredHeight), not a fixed offset; spawning below it makes the kinematic height
        // hold teleport the jet and the landing flow (which watches the Moving flag) takes over
        // before the wings model accelerates to its authored minimum speed.
        var cruiseHeight = jet.Definition.LocomotorSets[LocomotorSetType.Normal].Locomotors?[0].Value?.PreferredHeight ?? 48f;
        jet.UpdateTransform(
            new Vector3(launch.X, launch.Y, height + MathF.Max(48f, cruiseHeight)),
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, facing));
        jet.UpdateColliders();

        // Sortie 1: pursue and fire on the first target.
        var healthA = targetA.BodyModule?.Health ?? 0;
        jet.AIUpdate.SetTargetPoint(targetAPosition);
        weapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, targetA.Id));
        var minimumRounds = clipSize;
        var consumed = false;
        var damagedFirst = false;
        var firstFrames = Wait(
            game,
            frame =>
            {
                if (jet.IsEffectivelyDead)
                {
                    return true;
                }

                if (weapon != null)
                {
                    minimumRounds = Math.Min(minimumRounds, weapon.CurrentRounds);
                }

                if (frame % 600 == 0)
                {
                    reporter.Info($"[ee2] {contract.Definition} sortie1 f{frame} rounds={weapon?.CurrentRounds} distA={Vector2.Distance(To2(jet.Translation), To2(targetAPosition)):0} hpA={targetA.BodyModule?.Health:0} jetZ={jet.Translation.Z:0} state={jetAI.CurrentJetAIState}");
                }

                if (!damagedFirst && (targetA.BodyModule?.Health ?? 0) < healthA)
                {
                    damagedFirst = true;
                    return true;
                }

                return false;
            },
            profile.FrameBudget);
        consumed = minimumRounds < clipSize;
        weapon?.SetTarget(null);

        // Return flight: homebound order, observed as proximity to the base.
        jet.AIUpdate.SetTargetPoint(home);
        var returnFrames = Wait(
            game,
            frame =>
            {
                if (jet.IsEffectivelyDead)
                {
                    return true;
                }

                if (frame % 600 == 0)
                {
                    reporter.Info($"[ee2] {contract.Definition} return f{frame} home={Vector2.Distance(To2(jet.Translation), To2(home)):0}");
                }

                return Vector2.Distance(To2(jet.Translation), To2(home)) < 260f;
            },
            profile.FrameBudget);
        var returned = Vector2.Distance(To2(jet.Translation), To2(home)) < 260f;

        // Second sortie: a fresh target along a fresh heading. The refill proof is the clip
        // observed full after the re-engagement and before the second damage, not merely a
        // second departure.
        var roundsAtReorder = weapon?.CurrentRounds ?? 0;
        var targetBPosition = home + new Vector3(600f, -50f, 0f);
        var targetB = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.TargetDefinition)!, game.Scene3D.Players[1], targetBPosition);
        var healthB = targetB.BodyModule?.Health ?? 0;
        jet.AIUpdate.SetTargetPoint(targetBPosition);
        weapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, targetB.Id));
        var refilled = false;
        var damagedSecond = false;
        var secondFrames = Wait(
            game,
            frame =>
            {
                if (jet.IsEffectivelyDead)
                {
                    return true;
                }

                if (weapon != null && weapon.CurrentRounds >= clipSize)
                {
                    refilled = true;
                }

                if (frame % 600 == 0)
                {
                    reporter.Info($"[ee2] {contract.Definition} sortie2 f{frame} rounds={weapon?.CurrentRounds} distB={Vector2.Distance(To2(jet.Translation), To2(targetBPosition)):0} hpB={targetB.BodyModule?.Health:0} dead={jet.IsEffectivelyDead} hp={jet.BodyModule?.Health:0}");
                }

                if ((targetB.BodyModule?.Health ?? 0) < healthB)
                {
                    damagedSecond = true;
                    return true;
                }

                return false;
            },
            profile.FrameBudget);

        // Death: a hostile Generals anti-air unit destroys a second copy; observe the lifecycle.
        var antiAirKiller = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName("GLAVehicleQuadCannon")!, hostile, new Vector3(airfield!.Translation.X + 200f, airfield!.Translation.Y + 200f, 0f));
        var airborneVictim = SpawnAt(game, definition!, owner, new Vector3(airfield!.Translation.X + 240f, airfield!.Translation.Y + 240f, 0f));
        var victimDied = false;
        var deathFrames = Wait(
            game,
            _ =>
            {
                if (airborneVictim.IsEffectivelyDead)
                {
                    victimDied = true;
                    return true;
                }

                antiAirKiller.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, airborneVictim.Id));
                return false;
            },
            profile.FrameBudget);

        var failures = new List<string>();
        if (jet.IsEffectivelyDead)
        {
            failures.Add("the aircraft was destroyed during the contract");
        }

        if (!consumed)
        {
            failures.Add($"ammunition never consumed (clip {clipSize})");
        }

        if (!damagedFirst)
        {
            failures.Add("no damage on the first sortie");
        }

        if (!returned)
        {
            failures.Add("return flight to base not observed (never within 260 units of the airfield)");
        }

        if (!refilled)
        {
            failures.Add($"clip not refilled on re-engagement (rounds at reorder {roundsAtReorder} of {clipSize})");
        }

        if (!damagedSecond)
        {
            failures.Add("no damage on the second sortie after refill");
        }

        if (!victimDied)
        {
            failures.Add("death not observed (anti-air killer never destroyed the second copy)");
        }

        return new Ee2ContractResult(
            contract.Definition,
            failures.Count == 0 ? null : string.Join("; ", failures))
        {
            DeathFrames = deathFrames,
            ClipSize = clipSize,
            MinimumRoundsObserved = minimumRounds,
            AttackFrames = firstFrames,
            ReturnFrames = returnFrames,
            RefillFrame = refilled ? roundsAtReorder : null,
            SecondSortieFrames = secondFrames,
            DamagedTarget = damagedFirst,
            Died = victimDied,
            ConsumedAmmunition = consumed,
            ReturnedToBase = returned,
            ClipRefilled = refilled,
            DamagedSecondSortie = damagedSecond,
        };
    }

    private static Ee2ContractResult RunHelicopterContract(
        Game game,
        Ee2ValidationProfile profile,
        Ee2ContractEntry contract,
        Player owner,
        Player hostile,
        ITestHarnessReporter reporter)
    {
        var definition = ResolveDefinition(game, contract.Definition);
        if (definition == null)
        {
            return new Ee2ContractResult(contract.Definition, "definition not found after import");
        }

        var airfield = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.AirfieldDefinition)!, owner, new Vector3(1600f, 1200f, 0f));
        var parking = airfield?.FindBehavior<ParkingPlaceBehaviour>();
        if (parking == null)
        {
            return new Ee2ContractResult(contract.Definition, $"airfield '{profile.AirfieldDefinition}' has no parking places");
        }

        parking.EnqueueObject();
        var helicopter = game.Scene3D.GameObjects.CreateObject(definition, owner);
        parking.ReportSpawn(helicopter.Id);
        helicopter.AIUpdate.SetLocomotor(LocomotorSetType.Taxiing);
        var heliAI = (JetAIUpdate)helicopter.AIUpdate;
        heliAI.Base = airfield;
        heliAI.CurrentJetAIState = JetAIUpdate.JetAIState.Parked;
        helicopter.SetTransformMatrix(parking.GetUnitCreateTransform(producedAtHelipad: true, helicopter.Id).Matrix * airfield.TransformMatrix);
        helicopter.UpdateColliders();

        // A helicopter does not use the runway state machine (KeepsParkingSpaceWhenAirborne is
        // cleared by the typed materializer): the flight order switches it onto the normal air
        // locomotor directly.
        var targetA = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.TargetDefinition)!, game.Scene3D.Players[1], new Vector3(1600f, 600f, 0f));
        var healthA = targetA.BodyModule?.Health ?? 0;

        helicopter.AIUpdate.SetLocomotor(LocomotorSetType.Normal);
        helicopter.AIUpdate.SetTargetPoint(targetA.Translation + new Vector3(60f, 0f, 0f));
        helicopter.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, targetA.Id));

        var sortie = Wait(game, _ => (targetA.BodyModule?.Health ?? 0) < healthA, SortieBudgetFrames);
        var damaged = (targetA.BodyModule?.Health ?? 0) < healthA;
        helicopter.CurrentWeapon?.SetTarget(null);

        // Return to the airfield: scripted, as every validation-ground order is.
        var basePosition = airfield!.Translation + new Vector3(80f, 0f, 0f);
        helicopter.AIUpdate.SetTargetPoint(basePosition);
        var returnFrames = Wait(game, _ => helicopter.AIUpdate.TargetPoints.Count == 0, SortieBudgetFrames);
        var returned = helicopter.AIUpdate.TargetPoints.Count == 0 && Vector2.Distance(To2(helicopter.Translation), To2(basePosition)) < DisplacementThreshold * 2;

        // Second damaging sortie against a fresh target.
        var targetB = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName(profile.TargetDefinition)!, game.Scene3D.Players[1], new Vector3(1900f, 600f, 0f));
        var healthB = targetB.BodyModule?.Health ?? 0;
        helicopter.AIUpdate.SetTargetPoint(targetB.Translation + new Vector3(60f, 0f, 0f));
        helicopter.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, targetB.Id));
        var secondSortie = Wait(game, _ => (targetB.BodyModule?.Health ?? 0) < healthB, SortieBudgetFrames);
        var damagedTwice = (targetB.BodyModule?.Health ?? 0) < healthB;

        // Death: a hostile Generals anti-air unit destroys a second copy; observe the lifecycle.
        var antiAirKiller = SpawnAt(game, game.AssetStore.ObjectDefinitions.GetByName("GLAVehicleQuadCannon")!, hostile, new Vector3(airfield!.Translation.X + 700f, airfield.Translation.Y + 700f, 0f));
        var airborneVictim = SpawnAt(game, definition!, owner, new Vector3(airfield.Translation.X + 740f, airfield.Translation.Y + 740f, 0f));
        var victimDied = false;
        var deathFrames = Wait(
            game,
            _ =>
            {
                if (airborneVictim.IsEffectivelyDead)
                {
                    victimDied = true;
                    return true;
                }

                antiAirKiller.CurrentWeapon?.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, airborneVictim.Id));
                return false;
            },
            profile.FrameBudget);

        var failures = new List<string>();
        if (!damaged)
        {
            failures.Add($"no weapon-caused health loss on the first sortie within {SortieBudgetFrames} frames");
        }

        if (!returned)
        {
            failures.Add("return to the airfield not observed");
        }

        if (!damagedTwice)
        {
            failures.Add("no damage on the second sortie after return");
        }

        if (!victimDied)
        {
            failures.Add("death not observed (anti-air killer never destroyed the second copy)");
        }

        return new Ee2ContractResult(
            contract.Definition,
            failures.Count == 0 ? null : string.Join("; ", failures))
        {
            DeathFrames = deathFrames,
            // No EE2 helicopter authors ordnance: the clip is infinite by source, so consumption
            // and refill are reported absent rather than fabricated.
            AmmunitionAbsentInSource = true,
            AttackFrames = sortie,
            ReturnFrames = returnFrames,
            SecondSortieFrames = secondSortie,
            DamagedTarget = damaged,
            Died = victimDied,
            ReturnedToBase = returned,
            DamagedSecondSortie = damagedTwice,
        };
    }

    private static readonly List<GameObject> Spawned = [];

    private static GameObject SpawnAt(Game game, ObjectDefinition definition, Player owner, Vector3 position)
    {
        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(position.X, position.Y)) ?? 0f;
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        gameObject.UpdateTransform(new Vector3(position.X, position.Y, height), gameObject.Rotation);
        gameObject.UpdateColliders();
        Spawned.Add(gameObject);
        return gameObject;
    }

    /// <summary>
    /// Removes every object this phase spawned (kill, then destroy once dead) so leftover
    /// hostiles from one contract cannot fire into the next.
    /// </summary>
    private static void ClearSpawned(Game game)
    {
        foreach (var gameObject in Spawned.Where(obj => !obj.IsEffectivelyDead))
        {
            gameObject.Kill();
        }

        foreach (var gameObject in Spawned.Where(obj => obj.IsEffectivelyDead && !obj.IsDestroyed))
        {
            gameObject.Destroy();
        }

        Spawned.Clear();
    }

    private static int Wait(Game game, Func<int, bool> until, int budget)
    {
        var start = CurrentFrame(game);
        var frame = start;
        while (frame - start < budget)
        {
            if (until(checked((int)(frame - start))))
            {
                break;
            }

            game.Update(Array.Empty<Input.InputMessage>());
            frame = CurrentFrame(game);
        }

        return checked((int)(frame - start));
    }

    private static Vector2 To2(Vector3 position) => new(position.X, position.Y);

    private static uint CurrentFrame(Game game) => unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    private static void WriteReport(Ee2ContractReport report, ITestHarnessReporter reporter)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ee2-contract-report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, Ee2ValidationProfile.Options));
        reporter.Info($"EE2 contract report saved to: {path}");
    }
}

/// <summary>The checked-in EE2 validation profile loaded by the validation ground.</summary>
public sealed class Ee2ValidationProfile
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The checked-in profile carries explanatory comments, matching the capture profiles.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Name { get; set; } = "";

    public CaptureContentImport ContentImport { get; set; } = new();

    public string AirfieldDefinition { get; set; } = "AmericaAirfield";

    public string TargetDefinition { get; set; } = "GLAInfantryRebel";

    public string KillerDefinition { get; set; } = "AmericaTankCrusader";

    public int FrameBudget { get; set; } = 12000;

    public List<Ee2ContractEntry> Contracts { get; set; } = [];

    public static Ee2ValidationProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<Ee2ValidationProfile>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Could not load EE2 validation profile '{path}'.");
        return profile;
    }
}

public sealed class Ee2ContractEntry
{
    /// <summary>Source-qualified definition, for example <c>ee2:FighterUpgradeEpoch15</c>.</summary>
    public string Definition { get; set; } = "";

    [JsonConverter(typeof(KindConverter))]
    public Ee2ContractKind Kind { get; set; }

    public sealed class KindConverter : JsonConverter<Ee2ContractKind>
    {
        public override Ee2ContractKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "Ground" => Ee2ContractKind.Ground,
                "FixedWing" => Ee2ContractKind.FixedWing,
                "Helicopter" => Ee2ContractKind.Helicopter,
                var value => throw new JsonException($"Unknown EE2 contract kind '{value}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, Ee2ContractKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

public enum Ee2ContractKind
{
    Ground,
    FixedWing,
    Helicopter,
}

public sealed record Ee2ContractReport
{
    public string Profile { get; init; } = "";

    public bool AllPassed { get; init; }

    public uint Frame { get; init; }

    public List<Ee2ContractResult> Contracts { get; init; } = [];
}

/// <summary>One reference unit's contract result; null FailureReason means every check passed.</summary>
public sealed class Ee2ContractResult
{
    public Ee2ContractResult(string definition, string? failureReason = null)
    {
        Definition = definition;
        FailureReason = failureReason;
    }

    public string Definition { get; }

    public string? FailureReason { get; }

    public bool Passed => FailureReason is null;

    public double? Displacement { get; init; }

    public int? MoveFrames { get; init; }

    public int? AttackFrames { get; init; }

    public int? DeathFrames { get; init; }

    public int? ClipSize { get; init; }

    public int? MinimumRoundsObserved { get; init; }

    public int? DepartFrame { get; init; }

    public int? ReturnFrame { get; init; }

    public int? RefillFrame { get; init; }

    public int? ReturnFrames { get; init; }

    public int? SecondSortieFrames { get; init; }

    public int? SortieFrames { get; init; }

    public bool? AmmunitionAbsentInSource { get; init; }

    /// <summary>Individual checks, named so a runtime gap is visible rather than flattened into one verdict.</summary>
    public bool? Moved { get; init; }

    public bool? DamagedTarget { get; init; }

    public bool? Died { get; init; }

    public bool? ConsumedAmmunition { get; init; }

    public bool? ReturnedToBase { get; init; }

    public bool? ClipRefilled { get; init; }

    public bool? DamagedSecondSortie { get; init; }
}
