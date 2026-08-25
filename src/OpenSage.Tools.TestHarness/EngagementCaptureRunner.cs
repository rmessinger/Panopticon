using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using OpenSage.Content.Translation;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Network;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Headless capture of a single two-unit engagement (an attacker firing at a target) as a PNG
/// sequence plus a manifest describing, per frame, where every actor projected on screen and what
/// its state was. The manifest is what makes an annotated GIF correct: labels are drawn from the
/// engine's own projection of each actor, not guessed after the fact.
///
/// The camera is auto-fitted so both actors occupy a configured fraction of the viewport, which is
/// what makes them recognizable: a fixed height is only correct for one particular separation, and
/// the tactical camera's map constraint silently clamps a look-at issued at the wrong height (see
/// <c>TacticalViewConstraintTests</c>), which is how earlier captures ended up framing empty
/// terrain.
/// </summary>
public static class EngagementCaptureRunner
{
    private const int ViewportWidth = 1024;
    private const int ViewportHeight = 768;

    // Projectiles are tiny; annotate them from their own projection rather than relying on pixels.
    private const float MinimumMarkerRadiusPixels = 6f;

    public static void Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var options = config.EngagementCapture ?? new EngagementCaptureOptions();
        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetTempPath(), "opensage_engagement")
            : options.OutputDirectory!;

        if (Directory.Exists(outputDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.png"))
            {
                File.Delete(file);
            }
        }
        Directory.CreateDirectory(outputDirectory);

        QuietenPerFrameLogging();

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, FullHud = config.FullHud },
            null);

        var playerSettings = new[]
        {
            new PlayerSetting(0, "FactionAmerica", new ColorRgb(255, 40, 40), 0, PlayerOwner.EasyAi, "USA"),
            new PlayerSetting(1, "FactionChina", new ColorRgb(40, 200, 40), 0, PlayerOwner.EasyAi, "China"),
            new PlayerSetting(2, "FactionGLA", new ColorRgb(40, 40, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };

        game.StartSkirmishOrMultiPlayerGame(options.MapPath, new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }
        game.EnableFixedLogicTimestep();

        if (options.HideHud)
        {
            // The minimap corner intrudes on a close-up crop; a capture wants the 3D scene only.
            while (game.Scene2D.WndWindowManager.OpenWindowCount > 0)
            {
                game.Scene2D.WndWindowManager.PopWindow();
            }
        }

        var scene = game.Scene3D;
        var attackerOwner = scene.Players[AttackerPlayerIndex];
        // The target is civilian: a hostile target would return fire and could kill the attacker
        // mid-capture, and the point of the capture is one side's shot cycle.
        var targetOwner = scene.Players[CivilianPlayerIndex];

        var separation = MathF.Max(10f, options.Separation);
        var axis = new Vector2(
            MathF.Cos(MathUtility.ToRadians(options.AxisAngleDegrees)),
            MathF.Sin(MathUtility.ToRadians(options.AxisAngleDegrees)));
        var center = new Vector2(options.CenterX, options.CenterY);
        var attackerPosition = center - axis * (separation / 2f);
        var targetPosition = center + axis * (separation / 2f);

        var attacker = Spawn(game, options.AttackerDefinition, attackerPosition, attackerOwner, FacingTowards(attackerPosition, targetPosition));
        if (attacker == null)
        {
            reporter.Error($"Attacker definition '{options.AttackerDefinition}' not found.");
            Environment.ExitCode = 1;
            return;
        }

        var target = Spawn(game, options.TargetDefinition, targetPosition, targetOwner, FacingTowards(targetPosition, attackerPosition));
        if (target == null)
        {
            reporter.Error($"Target definition '{options.TargetDefinition}' not found.");
            Environment.ExitCode = 1;
            return;
        }

        if (options.TargetHp > 0f)
        {
            target.BodyModule?.SetMaxHealth(options.TargetHp, MaxHealthChangeType.FullyHeal);
        }

        Step(game);

        var fit = FitCamera(game, options, attacker, target, reporter);
        if (!fit.Success)
        {
            reporter.Error(fit.Message);
            Environment.ExitCode = 4;
            return;
        }
        reporter.Info(fit.Message);

        for (var i = 0; i < options.SettleFrames; i++)
        {
            Step(game);
        }

        // Command the weapon directly rather than through an AttackObject order: the order's move
        // step suppresses fire for several units, and any approach would drift the pair out of the
        // framing this capture just fitted.
        if (attacker.CurrentWeapon == null)
        {
            reporter.Error($"'{options.AttackerDefinition}' has no current weapon; nothing would fire.");
            Environment.ExitCode = 4;
            return;
        }

        attacker.CurrentWeapon.SetTarget(new WeaponTarget(scene.GameObjects, target.Id));

        var attackerLabel = string.IsNullOrWhiteSpace(options.AttackerLabel) ? DisplayNameOf(attacker) : options.AttackerLabel!;
        var targetLabel = string.IsNullOrWhiteSpace(options.TargetLabel) ? DisplayNameOf(target) : options.TargetLabel!;

        var frames = new List<FrameRecord>(options.Frames);
        var projectileFrames = 0;
        for (var i = 0; i < options.Frames; i++)
        {
            Step(game);
            RenderFrame(game);

            var path = Path.Combine(outputDirectory, $"frame_{i:D4}.png");
            FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, path);

            var actors = new List<ActorRecord>(2);
            var attackerRecord = Describe(game, attacker, "attacker", attackerLabel);
            if (attackerRecord != null)
            {
                actors.Add(attackerRecord);
            }
            var targetRecord = Describe(game, target, "target", targetLabel);
            if (targetRecord != null)
            {
                actors.Add(targetRecord);
            }

            var projectiles = DescribeProjectiles(game, attacker, target);
            if (projectiles.Count > 0)
            {
                projectileFrames++;
            }

            frames.Add(new FrameRecord(
                Index: i,
                File: Path.GetFileName(path),
                Actors: actors,
                Projectiles: projectiles));

            // Keep firing: the weapon target is cleared when the target dies, which also ends the
            // interesting part of the capture.
            if (target.BodyModule is { Health: <= 0f })
            {
                break;
            }
        }

        var manifest = new EngagementManifest(
            ViewportWidth,
            ViewportHeight,
            options.AttackerDefinition,
            options.TargetDefinition,
            attackerLabel,
            targetLabel,
            frames);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOptions));

        reporter.Info($"Captured {frames.Count} frames to {outputDirectory}");
        reporter.Info($"Manifest: {manifestPath}");
        reporter.Info($"Frames containing a projectile: {projectileFrames}");
        reporter.Info(FormattableString.Invariant(
            $"Target health: {options.TargetHp:0} -> {target.BodyModule?.Health ?? 0f:0}"));

        // A capture with no damage and no projectile shows nothing worth annotating; fail loudly
        // rather than producing a GIF of two idle units.
        if (projectileFrames == 0)
        {
            reporter.Error("No projectile was observed in flight; the capture does not show the engagement.");
            Environment.ExitCode = 5;
        }
    }

    private const int CivilianPlayerIndex = 1;
    private const int AttackerPlayerIndex = 2;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private static float FacingTowards(Vector2 from, Vector2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);

    private static GameObject? Spawn(Game game, string definitionName, Vector2 planarPosition, Player owner, float facingYaw)
    {
        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        if (definition == null)
        {
            return null;
        }

        var height = game.Scene3D.Terrain.GetGroundHeight(planarPosition) ?? 0f;
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        gameObject.UpdateTransform(
            new Vector3(planarPosition.X, planarPosition.Y, height),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, facingYaw));
        // Colliders are built at the origin; without an immediate refresh the model is frustum-culled
        // until the next logic tick, which would corrupt the camera fit below.
        gameObject.UpdateColliders();
        return gameObject;
    }

    /// <summary>
    /// Chooses a camera height where the two actors span <see cref="EngagementCaptureOptions.DesiredSpanFraction"/>
    /// of the viewport width and both project inside a safe inner margin. On-screen size is very
    /// nearly inversely proportional to height, so each iteration scales the height by the ratio of
    /// measured to desired span and converges in a handful of steps.
    /// </summary>
    private static (bool Success, string Message) FitCamera(
        Game game,
        EngagementCaptureOptions options,
        GameObject attacker,
        GameObject target,
        ITestHarnessReporter reporter)
    {
        var view = game.Scene3D.TacticalView;
        var lookAt = new Vector3(options.CenterX, options.CenterY, 0f);
        var desiredSpan = ViewportWidth * Math.Clamp(options.DesiredSpanFraction, 0.1f, 0.9f);
        var height = Math.Clamp(options.InitialCameraHeight, options.MinCameraHeight, options.MaxCameraHeight);

        view.InitHeightForMap();
        view.SetAngleAndPitchToDefault();

        for (var attempt = 0; attempt < 12; attempt++)
        {
            // Height first, then aim: the look-at is clamped against the constraint computed for the
            // CURRENT height, so aiming before zooming lands the camera off the subject.
            view.SnapHeightAboveGround(height);
            view.LookAt(lookAt);
            Step(game);
            RenderFrame(game);

            var a = game.Scene3D.Camera.WorldToScreenPoint(attacker.Translation);
            var t = game.Scene3D.Camera.WorldToScreenPoint(target.Translation);
            var span = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(t.X, t.Y));
            if (span < 1f)
            {
                return (false, "The two actors project to the same screen point; check their separation.");
            }

            var ratio = span / desiredSpan;
            var onScreen = IsInsideSafeArea(a) && IsInsideSafeArea(t);
            if (onScreen && MathF.Abs(ratio - 1f) < 0.08f)
            {
                return (true, FormattableString.Invariant(
                    $"Camera fitted at height {height:0} (actors span {span:0}px of {ViewportWidth}px)."));
            }

            var next = Math.Clamp(height * ratio, options.MinCameraHeight, options.MaxCameraHeight);
            if (MathF.Abs(next - height) < 0.5f && onScreen)
            {
                return (true, FormattableString.Invariant(
                    $"Camera fitted at clamped height {height:0} (actors span {span:0}px)."));
            }
            height = next;
        }

        return (false, "Could not fit both actors on screen; adjust Separation, CenterX/CenterY or the height bounds.");
    }

    private static bool IsInsideSafeArea(Vector3 screenPoint) =>
        screenPoint.X > ViewportWidth * 0.08f && screenPoint.X < ViewportWidth * 0.92f
        && screenPoint.Y > ViewportHeight * 0.08f && screenPoint.Y < ViewportHeight * 0.92f;

    private static ActorRecord? Describe(Game game, GameObject gameObject, string role, string label)
    {
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

        return new ActorRecord(
            role,
            label,
            gameObject.Definition.Name,
            gameObject.Owner?.Side ?? "Civilian",
            screen.X,
            screen.Y,
            radius,
            gameObject.BodyModule?.Health ?? 0f,
            gameObject.BodyModule?.MaxHealth ?? 0f);
    }

    private static List<ActorRecord> DescribeProjectiles(Game game, GameObject attacker, GameObject target)
    {
        var camera = game.Scene3D.Camera;
        var results = new List<ActorRecord>();

        foreach (var candidate in game.Scene3D.GameObjects.Objects)
        {
            if (candidate == attacker || candidate == target)
            {
                continue;
            }

            if (!candidate.Definition.KindOf.Get(ObjectKinds.Projectile))
            {
                continue;
            }

            var screen = camera.WorldToScreenPoint(candidate.Translation);
            results.Add(new ActorRecord(
                "projectile",
                "MISSILE",
                candidate.Definition.Name,
                candidate.Owner?.Side ?? "Civilian",
                screen.X,
                screen.Y,
                MinimumMarkerRadiusPixels * 2f,
                0f,
                0f));
        }

        return results;
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

    private static void RenderFrame(Game game)
    {
        game.Panel.EnsureFrame(new Rectangle(0, 0, ViewportWidth, ViewportHeight));
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

    private sealed record ActorRecord(
        string Role,
        string Label,
        string Definition,
        string Side,
        float X,
        float Y,
        float RadiusPixels,
        float Health,
        float MaxHealth);

    private sealed record FrameRecord(
        int Index,
        string File,
        IReadOnlyList<ActorRecord> Actors,
        IReadOnlyList<ActorRecord> Projectiles);

    private sealed record EngagementManifest(
        int ViewportWidth,
        int ViewportHeight,
        string AttackerDefinition,
        string TargetDefinition,
        string AttackerLabel,
        string TargetLabel,
        IReadOnlyList<FrameRecord> Frames);
}
