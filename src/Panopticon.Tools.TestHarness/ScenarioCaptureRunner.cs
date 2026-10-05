using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Translation;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Physics;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Executes a <see cref="CaptureProfile"/> headlessly: spawns the actors, fixes the camera
/// (explicit or auto-fit), runs frame-scheduled commands, captures a PNG sequence at the
/// configured stride, evaluates expectations, and writes an additive manifest the engagement
/// annotator can still consume. The camera is never moved after capture starts.
///
/// Camera fitting pauses logic and refreshes through render-time camera updates only, so a fit
/// consumes zero logic ticks; any required warm-up belongs to the profile's settle period.
/// </summary>
public static class ScenarioCaptureRunner
{
    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;

    // Projectiles are tiny; annotate them from their own projection rather than relying on pixels.
    private const float MinimumMarkerRadiusPixels = 6f;
    private const float ProjectileDirectionMarkerLength = 12f;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static CaptureRunResult Run(
        GameInstallation installation,
        CaptureProfile profile,
        in CaptureRunSettings settings,
        ITestHarnessReporter reporter)
    {
        PhysicsBackendPolicy.Current = ParsePhysicsBackend(profile.PhysicsBackend);
        try
        {
            return RunInternal(installation, profile, settings, reporter);
        }
        finally
        {
            PhysicsBackendPolicy.Current = PhysicsBackendKind.Legacy;
        }
    }

    private static PhysicsBackendKind ParsePhysicsBackend(string? name)
    {
        return string.Equals(name, "Bepu", StringComparison.OrdinalIgnoreCase)
            ? PhysicsBackendKind.Bepu
            : PhysicsBackendKind.Legacy;
    }

    private static CaptureRunResult RunInternal(
        GameInstallation installation,
        CaptureProfile profile,
        in CaptureRunSettings settings,
        ITestHarnessReporter reporter)
    {
        var validationErrors = CaptureProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                reporter.Error($"Profile '{profile.Name}': {error}");
            }

            return new CaptureRunResult(1, null, null, 0, 0, [], [], null);
        }

        var outputDirectory = string.IsNullOrWhiteSpace(profile.OutputDirectory)
            ? settings.FallbackOutputDirectory ?? DefaultOutputDirectory(profile.Name)
            : profile.OutputDirectory!;
        Directory.CreateDirectory(outputDirectory);
        // Only artifacts this runner owns are cleaned; anything else in the directory stays.
        foreach (var owned in Directory.EnumerateFiles(outputDirectory, "*.png"))
        {
            File.Delete(owned);
        }

        var ownedManifest = Path.Combine(outputDirectory, "manifest.json");
        if (File.Exists(ownedManifest))
        {
            File.Delete(ownedManifest);
        }

        QuietenPerFrameLogging();

        using var game = new Game(
            installation,
            settings.Renderer,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = settings.FullHud },
            null);

        var playerSettings = profile.Players.Select((player, index) => new PlayerSetting(
            index,
            player.Side,
            ParseColor(player.Color, index),
            0,
            PlayerOwner.EasyAi,
            string.IsNullOrWhiteSpace(player.Name) ? player.Id : player.Name)).ToArray();

        game.StartSkirmishOrMultiPlayerGame(profile.MapPath, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();

        if (PhysicsBackendPolicy.Current == PhysicsBackendKind.Bepu)
        {
            TerrainPhysicsChunks.Feed(game.Scene3D.Terrain.HeightMap, game.Scene3D.GameEngine.PhysicsWorld);
        }

        if (profile.HideHud)
        {
            // The minimap corner intrudes on a close-up crop; a capture wants the 3D scene only.
            while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
            {
                game.Scene2D.WndWindowManager.PopWindow();
            }
        }

        for (var i = 0; i < profile.Players.Count; i++)
        {
            if (profile.Players[i].StartingMoney > 0)
            {
                ResolvePlayerSlot(game, i).BankAccount.Deposit(profile.Players[i].StartingMoney);
            }
        }

        foreach (var import in profile.ContentImports)
        {
            if (!UnitContentImportRunner.Import(game, import, reporter))
            {
                reporter.Error($"Content import '{import.Source}' failed; no partial content was published.");
                return new CaptureRunResult(1, outputDirectory, null, 0, 0, [], [], null);
            }
        }

        var scene = game.Scene3D;

        // Actor registry: profile IDs and construct-result IDs resolve to live objects from here on.
        var actors = new Dictionary<string, ActorState>();
        foreach (var actor in profile.Actors)
        {
            var definition = ResolveDefinition(game, actor.Definition);
            if (definition == null)
            {
                reporter.Error($"Actor '{actor.Id}' definition '{actor.Definition}' not found.");
                return new CaptureRunResult(1, outputDirectory, null, 0, 0, [], [], null);
            }

            var owner = ResolveOwner(game, actor.Owner, profile);
            var spawned = Spawn(game, definition, owner, actor);
            actors[actor.Id] = new ActorState(actor, spawned)
            {
                Label = string.IsNullOrWhiteSpace(actor.Label) ? DisplayNameOf(spawned) : actor.Label!,
            };
        }

        foreach (var _ in Enumerable.Range(0, profile.SettleFrames))
        {
            Step(game);
        }

        // Seed per-actor observation baselines after settle (health, displacement origin).
        foreach (var state in actors.Values)
        {
            state.InitialHealth = state.Object.BodyModule?.Health ?? 0f;
            state.DisplacementOrigin = state.Object.Translation;
        }

        var cameraSubjects = new List<Vector3>();
        var subjectIds = profile.Camera.SubjectActorIds.Count > 0
            ? profile.Camera.SubjectActorIds
            : profile.Actors.Select(a => a.Id).ToList();
        foreach (var id in subjectIds)
        {
            cameraSubjects.Add(actors[id].Object.Translation);
        }

        ActorState? followState = null;
        ActorState? projectileSourceState = null;
        var followFrozen = false;
        if (profile.Camera.Mode == CaptureCameraMode.Follow)
        {
            if (!string.IsNullOrWhiteSpace(profile.Camera.FollowProjectileSourceActorId))
            {
                projectileSourceState = actors[profile.Camera.FollowProjectileSourceActorId];
                if (!subjectIds.Contains(profile.Camera.FollowProjectileSourceActorId))
                {
                    // The shooter anchors the framing until the first projectile launches.
                    cameraSubjects.Add(projectileSourceState.Object.Translation);
                }
            }
            else
            {
                var followId = string.IsNullOrWhiteSpace(profile.Camera.FollowActorId)
                    ? subjectIds[0]
                    : profile.Camera.FollowActorId;
                followState = actors[followId];

                // The follow subject anchors the framing even when it is not an explicit subject.
                if (!subjectIds.Contains(followId))
                {
                    cameraSubjects.Add(followState.Object.Translation);
                }
            }
        }

        foreach (var point in profile.Camera.FramingPoints)
        {
            var z = point.Z != 0f ? point.Z : scene.Terrain.GetGroundHeight(new Vector2(point.X, point.Y)) ?? 0f;
            cameraSubjects.Add(new Vector3(point.X, point.Y, z));
        }

        var cameraOutcome = SetupCamera(game, profile.Camera, cameraSubjects, actors.Values.Select(a => a.Object));
        reporter.Info(cameraOutcome.Message);
        if (!cameraOutcome.Success)
        {
            return new CaptureRunResult(4, outputDirectory, null, 0, 0, [], [], null);
        }

        var schedule = new CaptureSchedule(profile);
        var commandResults = new List<CommandResultRecord>();
        var frames = new List<FrameRecord>(profile.DurationFrames);
        var framesWithProjectiles = 0;
        var projectileObserved = false;
        var previousProjectilePositions = new Dictionary<ObjectId, Vector3>();

        GameObject? stopActor = null;
        ObjectId followedProjectileId = ObjectId.Invalid;
        var followProjectileExpired = false;
        if (profile.StopWhenActorDestroyed != null
            && actors.TryGetValue(profile.StopWhenActorDestroyed, out var stopState))
        {
            stopActor = stopState.Object;
        }

        var captureIndex = 0;
        for (var frame = 0; frame < profile.DurationFrames; frame++)
        {
            foreach (var command in schedule.DueAt(frame))
            {
                var outcome = ExecuteCommand(game, actors, command);
                commandResults.Add(outcome.Record);
                if (outcome.FailureExitCode is { } commandExitCode)
                {
                    reporter.Error($"Command failed: {command.Describe()}: {outcome.Record.Detail}");
                    return new CaptureRunResult(commandExitCode, outputDirectory, null, frames.Count, 0, commandResults, [], null);
                }
            }

            Step(game);

            // Follow mode: pan every logic frame so the subject stays centered; the fitted
            // height never changes. A destroyed subject freezes the camera at its last spot.
            // Projectile follow rides the newest live projectile from the source: shell flight,
            // freeze at the impact site between shots, hand-off to the next shot.
            // FollowFirstProjectileOnly locks the first projectile for its whole flight and
            // freezes at its death site instead of handing off (rapid-fire weapons).
            var followFirstOnly = profile.Camera.FollowFirstProjectileOnly;
            if (projectileSourceState != null && !(followFirstOnly && followProjectileExpired))
            {
                var projectile = FindFollowProjectile(game, projectileSourceState.Object, followedProjectileId, followFirstOnly);
                if (projectile != null)
                {
                    followedProjectileId = projectile.Id;
                    var position = projectile.Translation;
                    scene.TacticalView.LookAt(new Vector3(position.X, position.Y, 0f));
                }
                else if (followFirstOnly && followedProjectileId != ObjectId.Invalid)
                {
                    followProjectileExpired = true;
                    reporter.Info(
                        "First followed projectile died; camera frozen at its impact site.");
                }
            }
            else if (followState != null && !followFrozen)
            {
                var followObject = followState.Object;
                if (followObject.IsEffectivelyDead || followObject.BodyModule is { Health: <= 0f })
                {
                    followFrozen = true;
                    reporter.Info(
                        $"Follow subject '{followState.Spec.Id}' destroyed; camera frozen at last position.");
                }
                else
                {
                    var position = followObject.Translation;
                    scene.TacticalView.LookAt(new Vector3(position.X, position.Y, 0f));
                }
            }

            var captured = frame % profile.CaptureStride == 0;
            if (!captured && profile.RenderEveryFrame)
            {
                RenderFrame(game);
            }

            if (captured)
            {
                RenderFrame(game);
                var path = Path.Combine(outputDirectory, $"frame_{captureIndex:D4}.png");
                FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, path);

                var actorRecords = new List<ActorRecord>();
                foreach (var state in actors.Values.Where(a => a.Spec.Tracked))
                {
                    var record = Describe(game, state);
                    if (record != null)
                    {
                        actorRecords.Add(record);
                    }
                }

                var projectiles = DescribeProjectiles(
                    game,
                    actors.Values.Select(a => a.Object),
                    previousProjectilePositions);
                if (projectiles.Count > 0)
                {
                    framesWithProjectiles++;
                    projectileObserved = true;
                }

                frames.Add(new FrameRecord(
                    Index: captureIndex,
                    AbsoluteLogicFrame: CurrentLogicFrame(game),
                    File: Path.GetFileName(path),
                    Actors: actorRecords,
                    Projectiles: projectiles));
                captureIndex++;
            }

            ObserveActors(actors);

            if (stopActor != null && (stopActor.IsEffectivelyDead || stopActor.BodyModule is { Health: <= 0f }))
            {
                break;
            }
        }

        var expectationResults = EvaluateExpectations(profile, actors, projectileObserved, frames);

        var manifest = BuildManifest(profile, frames, commandResults, expectationResults);
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOptions));

        reporter.Info($"Captured {frames.Count} frames to {outputDirectory}");
        reporter.Info($"Manifest: {manifestPath}");
        reporter.Info($"Frames containing a projectile: {framesWithProjectiles}");

        var failed = expectationResults.FirstOrDefault(r => !r.Passed);
        if (failed != null)
        {
            reporter.Error($"Expectation not met: {failed.Expectation}: {failed.Detail}");
        }

        var exitCode = failed != null ? 5 : 0;
        return new CaptureRunResult(
            exitCode,
            outputDirectory,
            manifestPath,
            frames.Count,
            framesWithProjectiles,
            commandResults,
            expectationResults,
            manifest);
    }

    private static string DefaultOutputDirectory(string name)
    {
        var sanitized = string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'));
        if (sanitized.Length == 0)
        {
            sanitized = "capture";
        }

        return Path.Combine(Path.GetTempPath(), $"panopticon_capture_{sanitized}");
    }

    private static ColorRgb ParseColor(string? hex, int playerIndex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var trimmed = hex.TrimStart('#');
            if (uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)
                && trimmed.Length == 6)
            {
                return new ColorRgb(
                    (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF));
            }
        }

        var palette = new[]
        {
            new ColorRgb(255, 40, 40),
            new ColorRgb(40, 200, 40),
            new ColorRgb(40, 40, 255),
            new ColorRgb(220, 220, 60),
        };
        return palette[playerIndex % palette.Length];
    }

    /// <summary>Native names resolve in the built-in store; <c>source:name</c> resolves in that source's published collections.</summary>
    private static ObjectDefinition? ResolveDefinition(Game game, string definition)
    {
        var separator = definition.IndexOf(':');
        if (separator <= 0)
        {
            return game.AssetStore.ObjectDefinitions.GetByName(definition);
        }

        var source = definition[..separator] == ContentSource.Ee2.Id ? ContentSource.Ee2 : null;
        return source is null ? null : game.AssetStore.GetSourceContent(source).ObjectDefinitions.GetByName(definition[(separator + 1)..]);
    }

    private static Player ResolvePlayerSlot(Game game, int slotIndex)
    {
        // Player manager layout: index 0 neutral, 1 civilian, then the declared slots in order.
        return game.Scene3D.Players[2 + slotIndex];
    }

    private static Player ResolveOwner(Game game, string ownerId, CaptureProfile profile)
    {
        if (ownerId == CaptureProfile.CivilianOwnerId)
        {
            return game.PlayerManager.GetCivilianPlayer();
        }

        var slot = profile.Players.FindIndex(p => p.Id == ownerId);
        return ResolvePlayerSlot(game, slot);
    }

    private static GameObject Spawn(Game game, ObjectDefinition definition, Player owner, CaptureActor actor)
    {
        var planar = new Vector2(actor.X, actor.Y);
        var height = actor.Z ?? (game.Scene3D.Terrain.GetGroundHeight(planar) ?? 0f);
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        var rotation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathUtility.ToRadians(actor.FacingDegrees))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtility.ToRadians(actor.PitchDegrees))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathUtility.ToRadians(actor.RollDegrees));
        gameObject.UpdateTransform(
            new Vector3(planar.X, planar.Y, height),
            rotation);
        // Colliders are built at the origin; without an immediate refresh the model is frustum-culled
        // until the next logic tick, which would corrupt the camera fit below.
        gameObject.UpdateColliders();
        return gameObject;
    }

    private static (bool Success, string Message) SetupCamera(
        Game game,
        CaptureCamera camera,
        IReadOnlyList<Vector3> subjectPoints,
        IEnumerable<GameObject> trackedActors)
    {
        var view = game.Scene3D.TacticalView;

        view.InitHeightForMap();
        view.SetAngleAndPitchToDefault();

        // Fitting refreshes the camera through render-time updates only; pausing logic keeps the
        // tick count deterministic no matter how many fit iterations a profile needs.
        var wasRunning = game.IsLogicRunning;
        game.IsLogicRunning = false;
        try
        {
            if (camera.Mode == CaptureCameraMode.Explicit)
            {
                var lookAt = camera.LookAt!;
                var explicitHeight = camera.Height;
                // Height first, then aim: the look-at is clamped against the constraint computed
                // for the CURRENT height, so aiming before zooming lands the camera off the subject.
                view.SnapHeightAboveGround(explicitHeight);
                view.LookAt(new Vector3(lookAt.X, lookAt.Y, 0f));
                RefreshCamera(game);

                var offScreen = trackedActors
                    .Where(actor => !IsInsideSafeArea(game.Scene3D.Camera.WorldToScreenPoint(actor.Translation)))
                    .ToList();
                if (offScreen.Count > 0)
                {
                    return (false,
                        $"Explicit camera at height {explicitHeight:0} leaves {offScreen.Count} tracked actor(s) outside the safe viewport; adjust Height, LookAt, or actor positions.");
                }

                return (true, FormattableString.Invariant($"Camera fixed at height {explicitHeight:0}."));
            }

            if (subjectPoints.Count == 0)
            {
                return (false, "No subjects to frame; declare actors or framing points.");
            }

            // A lone follow subject has no span to fit: honor the declared height and center it.
            if (camera.Mode == CaptureCameraMode.Follow && subjectPoints.Count == 1)
            {
                var followHeight = Math.Clamp(camera.InitialCameraHeight, camera.MinCameraHeight, camera.MaxCameraHeight);
                view.SnapHeightAboveGround(followHeight);
                view.LookAt(subjectPoints[0]);
                RefreshCamera(game);

                if (!IsInsideSafeArea(game.Scene3D.Camera.WorldToScreenPoint(subjectPoints[0])))
                {
                    return (false,
                        $"Follow camera at height {followHeight:0} leaves the subject outside the safe viewport; adjust the height bounds.");
                }

                return (true, FormattableString.Invariant(
                    $"Follow camera at height {followHeight:0}, centered on the subject."));
            }

            // AutoFit and Follow share the fit; Follow re-centers on its subject every
            // logic frame during the capture loop instead of staying put.

            var center = new Vector3(
                subjectPoints.Average(p => p.X),
                subjectPoints.Average(p => p.Y),
                0f);
            var desiredSpan = ViewportWidth * Math.Clamp(camera.DesiredSpanFraction, 0.1f, 0.9f);
            var height = Math.Clamp(camera.InitialCameraHeight, camera.MinCameraHeight, camera.MaxCameraHeight);

            for (var attempt = 0; attempt < 12; attempt++)
            {
                view.SnapHeightAboveGround(height);
                view.LookAt(center);
                RefreshCamera(game);

                var screenPoints = subjectPoints
                    .Select(p => game.Scene3D.Camera.WorldToScreenPoint(p))
                    .ToList();
                var minX = screenPoints.Min(p => p.X);
                var maxX = screenPoints.Max(p => p.X);
                var minY = screenPoints.Min(p => p.Y);
                var maxY = screenPoints.Max(p => p.Y);
                var span = MathF.Max(maxX - minX, maxY - minY);
                if (span < 1f)
                {
                    return (false, "All camera subjects project to the same screen point; check the framing setup.");
                }

                var ratio = span / desiredSpan;
                var onScreen = screenPoints.All(IsInsideSafeArea);
                if (onScreen && MathF.Abs(ratio - 1f) < 0.08f)
                {
                    return (true, FormattableString.Invariant(
                        $"Camera fitted at height {height:0} (subjects span {span:0}px of {ViewportWidth}px)."));
                }

                var next = Math.Clamp(height * ratio, camera.MinCameraHeight, camera.MaxCameraHeight);
                if (MathF.Abs(next - height) < 0.5f && onScreen)
                {
                    return (true, FormattableString.Invariant(
                        $"Camera fitted at clamped height {height:0} (subjects span {span:0}px)."));
                }

                height = next;
            }

            return (false, "Could not fit the subjects on screen; adjust separation, framing points, or the height bounds.");
        }
        finally
        {
            game.IsLogicRunning = wasRunning;
        }
    }

    private static void RefreshCamera(Game game)
    {
        // With logic paused, Update runs LocalLogicTick only: the tactical view advances on
        // render time, no logic frame is consumed.
        game.Update(Array.Empty<Input.InputMessage>());
        RenderFrame(game);
    }

    private static bool IsInsideSafeArea(Vector3 screenPoint) =>
        screenPoint.X > ViewportWidth * 0.08f && screenPoint.X < ViewportWidth * 0.92f
        && screenPoint.Y > ViewportHeight * 0.08f && screenPoint.Y < ViewportHeight * 0.92f;

    private static (CommandResultRecord Record, int? FailureExitCode) ExecuteCommand(
        Game game,
        Dictionary<string, ActorState> actors,
        CaptureCommand command)
    {
        switch (command)
        {
            case CaptureAttackCommand attack:
                {
                    var source = actors[attack.SourceActorId].Object;
                    var target = actors[attack.TargetActorId].Object;
                    Weapon weapon = null;
                    if (!string.IsNullOrWhiteSpace(attack.WeaponName))
                    {
                        weapon = source.ActiveWeaponSet.Weapons
                            .FirstOrDefault(w => w != null && string.Equals(w.Template.Name, attack.WeaponName, StringComparison.OrdinalIgnoreCase));
                        if (weapon == null)
                        {
                            return (Failed(command,
                                $"'{source.Definition.Name}' has no weapon '{attack.WeaponName}' in its current weapon set."), 4);
                        }
                    }

                    weapon ??= source.CurrentWeapon;
                    if (weapon == null)
                    {
                        return (Failed(command, $"'{source.Definition.Name}' has no current weapon; nothing would fire."), 4);
                    }

                    // Command the weapon directly rather than through an AttackObject order: the
                    // order's move step suppresses fire for several units, and any approach would
                    // drift the actors out of the framing the capture just fitted.
                    weapon.SetTarget(new WeaponTarget(game.Scene3D.GameObjects, target.Id));
                    return (Dispatched(command), null);
                }

            case CaptureGrantUpgradeCommand grant:
                {
                    var actor = actors[grant.ActorId].Object;
                    var upgrade = game.AssetStore.Upgrades.GetByName(grant.UpgradeName);
                    if (upgrade == null)
                    {
                        return (Failed(command, $"Upgrade '{grant.UpgradeName}' not found."), 1);
                    }

                    // The object's weapon set rebuilds on its next logic tick, so a GrantUpgrade
                    // scheduled before an Attack makes the upgraded weapons addressable by name.
                    // Player-type upgrades land on the owner; the object's upgrade modules
                    // (e.g. WeaponSetUpgrade) must be re-evaluated against the merged set.
                    upgrade.GrantUpgrade(actor);
                    actor.UpdateUpgradeableModules();
                    return (Dispatched(command), null);
                }

            case CaptureMoveCommand move:
                {
                    var actor = actors[move.ActorId].Object;
                    if (actor.AIUpdate == null)
                    {
                        return (Failed(command,
                            $"'{actor.Definition.Name}' has no AI update; it cannot be move-ordered."), 1);
                    }

                    // A real engine move: pathfinded target points through SetTargetPoint, which
                    // raises the MOVING condition flag on dispatch and clears it on arrival.
                    var ground = new Vector3(
                        move.X,
                        move.Y,
                        game.Scene3D.Terrain.GetGroundHeight(new Vector2(move.X, move.Y)) ?? 0f);
                    actor.AIUpdate.SetTargetPoint(ground);
                    return (Dispatched(command), null);
                }

            case CaptureConstructCommand construct:
                {
                    var builder = actors[construct.BuilderActorId].Object;
                    if (builder.AIUpdate is not IBuilderAIUpdate)
                    {
                        return (Failed(command,
                            $"Builder '{construct.BuilderActorId}' ({builder.Definition.Name}) has no builder AI update."), 1);
                    }

                    var buildingDefinition = game.AssetStore.ObjectDefinitions.GetByName(construct.BuildingDefinition);
                    if (buildingDefinition == null)
                    {
                        return (Failed(command, $"Building definition '{construct.BuildingDefinition}' not found."), 1);
                    }

                    var owner = builder.Owner!;
                    var cost = (uint)buildingDefinition.BuildCost;
                    if (owner.BankAccount.Money < cost)
                    {
                        return (Failed(command,
                            $"Owner of '{construct.BuilderActorId}' cannot afford {construct.BuildingDefinition} (has {owner.BankAccount.Money}, needs {cost})."), 1);
                    }

                    // The build order applies to the player's current selection; select exactly this
                    // builder so a stray selection cannot redirect the construction.
                    owner.SelectUnits(new[] { builder });

                    var position = new Vector3(
                        construct.X,
                        construct.Y,
                        game.Scene3D.Terrain.GetGroundHeight(new Vector2(construct.X, construct.Y)) ?? 0f);
                    var order = Order.CreateBuildObject(
                        game.Scene3D.GetPlayerIndex(owner),
                        (int)buildingDefinition.InstanceId,
                        position,
                        MathUtility.ToRadians(construct.FacingDegrees));
                    new OrderProcessor(game).Process([order]);

                    var created = game.Scene3D.GameObjects.Objects
                        .Where(o => o.Owner == owner
                            && o.Definition == buildingDefinition
                            && !actors.Values.Any(a => a.Object == o)
                            && Vector3.Distance(o.Translation, position) < 5f)
                        .FirstOrDefault();
                    if (created == null)
                    {
                        return (Failed(command,
                            $"Build order dispatched but no {construct.BuildingDefinition} appeared at ({construct.X:0}, {construct.Y:0}); construction could not start."), 1);
                    }

                    actors[construct.ResultActorId] = new ActorState(
                        new CaptureActor
                        {
                            Id = construct.ResultActorId,
                            Definition = construct.BuildingDefinition,
                            Owner = profileOwnerOf(actors, construct.BuilderActorId),
                            X = construct.X,
                            Y = construct.Y,
                            FacingDegrees = construct.FacingDegrees,
                            Label = construct.BuildingDefinition,
                            Role = "construction",
                        },
                        created)
                    {
                        Label = construct.BuildingDefinition,
                        InitialHealth = created.BodyModule?.Health ?? 0f,
                        DisplacementOrigin = created.Translation,
                    };
                    return (Dispatched(command), null);
                }

            case CaptureApplyImpulseCommand impulse:
                {
                    var actor = actors[impulse.ActorId].Object;
                    var physics = actor.Physics;
                    if (physics == null)
                    {
                        return (Failed(command,
                            $"Actor '{impulse.ActorId}' ({actor.Definition.Name}) has no PhysicsBehavior to receive an impulse."), 1);
                    }

                    actors[impulse.ActorId].DisplacementOrigin ??= actor.Translation;
                    physics.ApplyForce(new Vector3(impulse.DeltaVelocityX, impulse.DeltaVelocityY, impulse.DeltaVelocityZ) * physics.Mass);
                    return (Dispatched(command), null);
                }

            default:
                return (Failed(command, $"Unsupported command type {command.GetType().Name}."), 1);
        }

        static string profileOwnerOf(Dictionary<string, ActorState> actors, string builderActorId) =>
            actors[builderActorId].Spec.Owner;
    }

    private static CommandResultRecord Dispatched(CaptureCommand command) =>
        new(command.Describe(), command.Frame, "Dispatched", null);

    private static CommandResultRecord Failed(CaptureCommand command, string detail) =>
        new(command.Describe(), command.Frame, "Failed", detail);

    private static void ObserveActors(Dictionary<string, ActorState> actors)
    {
        foreach (var state in actors.Values)
        {
            var obj = state.Object;
            if (obj.IsEffectivelyDead || obj.BodyModule is { Health: <= 0f })
            {
                state.ObservedDestroyed = true;
                continue;
            }

            if (obj.BodyModule != null && obj.BodyModule.Health < state.InitialHealth)
            {
                state.ObservedDamaged = true;
            }

            if (obj.TestStatus(ObjectStatus.UnderConstruction))
            {
                if (obj.BuildProgress > 0f)
                {
                    state.ObservedConstructionProgressed = true;
                }
            }
            else if (obj.BuildProgress >= 0.999f)
            {
                state.ObservedConstructionCompleted = true;
                state.ObservedConstructionProgressed = true;
            }
        }
    }

    private static List<ExpectationResultRecord> EvaluateExpectations(
        CaptureProfile profile,
        Dictionary<string, ActorState> actors,
        bool projectileObserved,
        IReadOnlyList<FrameRecord> frames)
    {
        var results = new List<ExpectationResultRecord>();
        foreach (var expectation in profile.Expectations)
        {
            switch (expectation)
            {
                case CaptureProjectileObservedExpectation:
                    results.Add(new ExpectationResultRecord(
                        Name(expectation),
                        projectileObserved,
                        projectileObserved ? "projectile observed" : "no projectile was ever observed in flight"));
                    break;

                case CaptureProjectileOrientationExpectation orientation:
                    {
                        var samples = frames
                            .SelectMany(frame => frame.Projectiles)
                            .Where(projectile => string.IsNullOrWhiteSpace(orientation.Definition)
                                || projectile.Definition == orientation.Definition)
                            .Select(projectile => projectile.Orientation)
                            .OfType<ProjectileOrientationRecord>()
                            .Where(sample => sample.AlignmentDot.HasValue)
                            .ToList();
                        var minimumDot = samples.Count > 0
                            ? samples.Min(sample => sample.AlignmentDot!.Value)
                            : float.NaN;
                        var sawAscent = samples.Any(sample => sample.TravelZ > 0.01f);
                        var sawDescent = samples.Any(sample => sample.TravelZ < -0.01f);
                        var passed = samples.Count >= orientation.MinimumSamples
                            && minimumDot >= orientation.MinimumAlignmentDot
                            && (!orientation.RequireAscending || sawAscent)
                            && (!orientation.RequireDescending || sawDescent);
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            passed,
                            FormattableString.Invariant(
                                $"{samples.Count} samples, minimum dot {minimumDot:0.####}, ascent {sawAscent}, descent {sawDescent}")));
                        break;
                    }

                case CaptureActorDamagedExpectation damaged:
                    {
                        var state = actors[damaged.ActorId];
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            state.ObservedDamaged,
                            state.ObservedDamaged
                                ? $"health dropped below {state.InitialHealth:0}"
                                : $"health never dropped below the post-setup {state.InitialHealth:0}"));
                        break;
                    }

                case CaptureActorDestroyedExpectation destroyed:
                    {
                        var state = actors[destroyed.ActorId];
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            state.ObservedDestroyed,
                            state.ObservedDestroyed ? "actor destroyed" : "actor survived the capture"));
                        break;
                    }

                case CaptureConstructionProgressedExpectation progressed:
                    {
                        var state = actors[progressed.ActorId];
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            state.ObservedConstructionProgressed,
                            state.ObservedConstructionProgressed
                                ? $"build progress reached {state.Object.BuildProgress:0.##}"
                                : "build progress never exceeded zero"));
                        break;
                    }

                case CaptureConstructionCompletedExpectation completed:
                    {
                        var state = actors[completed.ActorId];
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            state.ObservedConstructionCompleted,
                            state.ObservedConstructionCompleted
                                ? "construction finished"
                                : $"construction did not finish (progress {state.Object.BuildProgress:0.##})"));
                        break;
                    }

                case CaptureMinDisplacementExpectation displacement:
                    {
                        var state = actors[displacement.ActorId];
                        var origin = state.DisplacementOrigin ?? state.Object.Translation;
                        var moved = Vector3.Distance(state.Object.Translation, origin);
                        results.Add(new ExpectationResultRecord(
                            Name(expectation),
                            moved >= displacement.MinDisplacement,
                            FormattableString.Invariant($"moved {moved:0.#} of the required {displacement.MinDisplacement:0.#} world units")));
                        break;
                    }

                default:
                    results.Add(new ExpectationResultRecord(
                        Name(expectation),
                        false,
                        $"unsupported expectation type {expectation.GetType().Name}"));
                    break;
            }
        }

        return results;
    }

    private static string Name(CaptureExpectation expectation) =>
        string.IsNullOrWhiteSpace(expectation.Id) ? expectation.Describe() : expectation.Id!;

    private static CaptureManifest BuildManifest(
        CaptureProfile profile,
        List<FrameRecord> frames,
        List<CommandResultRecord> commandResults,
        List<ExpectationResultRecord> expectationResults)
    {
        string? RoleField(string role, Func<CaptureActor, string?> pick) => profile.Actors
            .Where(a => a.Role == role)
            .Select(pick)
            .FirstOrDefault(value => value != null);

        return new CaptureManifest(
            SchemaVersion: 2,
            Scenario: profile.Name,
            ViewportWidth,
            ViewportHeight,
            RoleField("attacker", a => a.Definition),
            RoleField("target", a => a.Definition),
            RoleField("attacker", a => a.Label),
            RoleField("target", a => a.Label),
            frames,
            commandResults,
            expectationResults);
    }

    private static ActorRecord? Describe(Game game, ActorState state)
    {
        var gameObject = state.Object;
        if (gameObject.BodyModule is { Health: <= 0f })
        {
            return null;
        }

        var camera = game.Scene3D.Camera;
        var center = gameObject.Translation + new Vector3(0f, 0f, gameObject.Definition.Geometry.MaxZ / 2f);
        var screen = camera.WorldToScreenPoint(center);
        var radius = MathF.Max(
            MinimumMarkerRadiusPixels,
            camera.GetScreenSize(new BoundingSphere(center, MathF.Max(gameObject.Definition.Geometry.BoundingSphereRadius, 1f))));

        var physics = gameObject.Physics;
        var velocity = physics?.Velocity ?? Vector3.Zero;
        string constructionState;
        if (gameObject.TestStatus(ObjectStatus.UnderConstruction))
        {
            constructionState = "UnderConstruction";
        }
        else if (gameObject.BuildProgress >= 0.999f)
        {
            constructionState = "Complete";
        }
        else
        {
            constructionState = "None";
        }

        return new ActorRecord(
            state.Spec.Role ?? state.Spec.Id,
            state.Label,
            gameObject.Definition.Name,
            gameObject.Owner?.Side ?? "Civilian",
            screen.X,
            screen.Y,
            radius,
            gameObject.BodyModule?.Health ?? 0f,
            gameObject.BodyModule?.MaxHealth ?? 0f,
            state.Spec.Id,
            gameObject.Translation.X,
            gameObject.Translation.Y,
            gameObject.Translation.Z,
            velocity.X,
            velocity.Y,
            velocity.Z,
            constructionState,
            gameObject.BuildProgress,
            null);
    }

    /// <summary>
    /// The live projectile the follow camera should ride: the previously followed one while it
    /// still exists (stability through its flight), else the newest projectile from the source.
    /// First-only mode acquires the EARLIEST live projectile once and never hands off: the lock
    /// dies with its projectile so the camera freezes instead of jumping mid-salvo. Returns null
    /// between shots (or after the locked projectile's death); the camera then holds its last
    /// position.
    /// </summary>
    private static GameObject? FindFollowProjectile(Game game, GameObject source, ObjectId previouslyFollowed, bool firstOnly)
    {
        GameObject? best = null;
        foreach (var candidate in game.Scene3D.GameObjects.Objects)
        {
            if (!candidate.Definition.KindOf.Get(ObjectKinds.Projectile))
            {
                continue;
            }

            if (candidate.CreatedByObjectID != source.Id)
            {
                continue;
            }

            if (candidate.Id == previouslyFollowed)
            {
                return candidate;
            }

            var takesIt = firstOnly
                ? previouslyFollowed == ObjectId.Invalid
                    && (best == null || candidate.Id.Index < best.Id.Index)
                : best == null || candidate.Id.Index > best.Id.Index;
            if (takesIt)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static List<ActorRecord> DescribeProjectiles(
        Game game,
        IEnumerable<GameObject> tracked,
        Dictionary<ObjectId, Vector3> previousPositions)
    {
        var camera = game.Scene3D.Camera;
        var excluded = new HashSet<GameObject>(tracked);
        var results = new List<ActorRecord>();

        foreach (var candidate in game.Scene3D.GameObjects.Objects)
        {
            if (excluded.Contains(candidate))
            {
                continue;
            }

            if (!candidate.Definition.KindOf.Get(ObjectKinds.Projectile))
            {
                continue;
            }

            var position = candidate.Translation;
            var screen = camera.WorldToScreenPoint(position);
            ProjectileOrientationRecord? orientation = null;
            if (TryNormalize(candidate.TransformMatrix.GetXVector(), out var forward))
            {
                var forwardEnd = camera.WorldToScreenPoint(
                    position + forward * ProjectileDirectionMarkerLength);

                float? travelX = null;
                float? travelY = null;
                float? travelZ = null;
                float? travelScreenX = null;
                float? travelScreenY = null;
                float? alignmentDot = null;
                if (previousPositions.TryGetValue(candidate.Id, out var previousPosition)
                    && TryNormalize(position - previousPosition, out var travel))
                {
                    var travelEnd = camera.WorldToScreenPoint(
                        position + travel * ProjectileDirectionMarkerLength);
                    travelX = travel.X;
                    travelY = travel.Y;
                    travelZ = travel.Z;
                    travelScreenX = travelEnd.X;
                    travelScreenY = travelEnd.Y;
                    alignmentDot = Math.Clamp(Vector3.Dot(forward, travel), -1.0f, 1.0f);
                }

                orientation = new ProjectileOrientationRecord(
                    forward.X,
                    forward.Y,
                    forward.Z,
                    forwardEnd.X,
                    forwardEnd.Y,
                    travelX,
                    travelY,
                    travelZ,
                    travelScreenX,
                    travelScreenY,
                    alignmentDot);
            }

            previousPositions[candidate.Id] = position;

            results.Add(new ActorRecord(
                "projectile",
                "MISSILE",
                candidate.Definition.Name,
                candidate.Owner?.Side ?? "Civilian",
                screen.X,
                screen.Y,
                MinimumMarkerRadiusPixels * 2f,
                0f,
                0f,
                null,
                candidate.Translation.X,
                candidate.Translation.Y,
                candidate.Translation.Z,
                null,
                null,
                null,
                null,
                null,
                orientation));
        }

        return results;
    }

    private static bool TryNormalize(in Vector3 value, out Vector3 normalized)
    {
        var lengthSquared = value.LengthSquared();
        if (!(lengthSquared > 1e-6f) || !float.IsFinite(lengthSquared))
        {
            normalized = default;
            return false;
        }

        normalized = value / MathF.Sqrt(lengthSquared);
        return true;
    }

    private static string DisplayNameOf(GameObject gameObject)
    {
        var name = gameObject.Definition.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return gameObject.Definition.Name;
        }

        // DisplayName is a localized string key ("OBJECT:MissileTeam"); the string table turns it
        // into the name players see. Fall back to the key's trailing segment if it has no entry.
        var translated = name.Translate();
        if (!string.IsNullOrWhiteSpace(translated) && translated != name)
        {
            return translated;
        }

        var separator = name.LastIndexOf(':');
        return separator >= 0 && separator < name.Length - 1 ? name[(separator + 1)..] : name;
    }

    private static void Step(Game game) => game.Update(Array.Empty<Input.InputMessage>());

    private static uint CurrentLogicFrame(Game game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    private static void RenderFrame(Game game)
    {
        game.Panel.EnsureFrame(new Mathematics.Rectangle(0, 0, ViewportWidth, ViewportHeight));
        game.Render();
    }

    private static void QuietenPerFrameLogging()
    {
        var loggingConfig = NLog.LogManager.Configuration;
        if (loggingConfig == null)
        {
            return;
        }

        foreach (var rule in loggingConfig.LoggingRules)
        {
            rule.DisableLoggingForLevel(NLog.LogLevel.Info);
        }

        NLog.LogManager.ReconfigExistingLoggers();
    }

    private sealed class ActorState(CaptureActor spec, GameObject obj)
    {
        public CaptureActor Spec { get; } = spec;
        public GameObject Object { get; } = obj;
        public string Label { get; init; } = "";
        public float InitialHealth { get; set; }
        public Vector3? DisplacementOrigin { get; set; }
        public bool ObservedDamaged { get; set; }
        public bool ObservedDestroyed { get; set; }
        public bool ObservedConstructionProgressed { get; set; }
        public bool ObservedConstructionCompleted { get; set; }
    }
}

public sealed record CaptureRunSettings(
    GraphicsBackend? Renderer,
    bool FullHud,
    string? FallbackOutputDirectory);

public sealed record CaptureRunResult(
    int ExitCode,
    string? OutputDirectory,
    string? ManifestPath,
    int CapturedFrames,
    int FramesWithProjectiles,
    IReadOnlyList<CommandResultRecord> CommandResults,
    IReadOnlyList<ExpectationResultRecord> ExpectationResults,
    CaptureManifest? Manifest);

public sealed record CommandResultRecord(
    string Command,
    int Frame,
    string Status,
    string? Detail);

public sealed record ExpectationResultRecord(
    string Expectation,
    bool Passed,
    string Detail);

public sealed record CaptureManifest(
    int SchemaVersion,
    string Scenario,
    int ViewportWidth,
    int ViewportHeight,
    string? AttackerDefinition,
    string? TargetDefinition,
    string? AttackerLabel,
    string? TargetLabel,
    IReadOnlyList<FrameRecord> Frames,
    IReadOnlyList<CommandResultRecord> CommandResults,
    IReadOnlyList<ExpectationResultRecord> ExpectationResults);

public sealed record FrameRecord(
    int Index,
    long AbsoluteLogicFrame,
    string File,
    IReadOnlyList<ActorRecord> Actors,
    IReadOnlyList<ActorRecord> Projectiles);

public sealed record ActorRecord(
    string Role,
    string Label,
    string Definition,
    string Side,
    float X,
    float Y,
    float RadiusPixels,
    float Health,
    float MaxHealth,
    string? Id,
    float? WorldX,
    float? WorldY,
    float? WorldZ,
    float? VelX,
    float? VelY,
    float? VelZ,
    string? ConstructionState,
    float? BuildProgress,
    ProjectileOrientationRecord? Orientation);

public sealed record ProjectileOrientationRecord(
    float ForwardX,
    float ForwardY,
    float ForwardZ,
    float ForwardScreenX,
    float ForwardScreenY,
    float? TravelX,
    float? TravelY,
    float? TravelZ,
    float? TravelScreenX,
    float? TravelScreenY,
    float? AlignmentDot);
