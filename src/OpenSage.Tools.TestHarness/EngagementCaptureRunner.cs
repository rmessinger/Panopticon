using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Data;
using OpenSage.Mathematics;
using OpenSage.Tools.ReplaySketch.Model.Capture;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Headless capture of a single two-unit engagement (an attacker firing at a target) as a PNG
/// sequence plus a manifest describing, per frame, where every actor projected on screen and what
/// its state was. The manifest is what makes an annotated GIF correct: labels are drawn from the
/// engine's own projection of each actor, not guessed after the fact.
///
/// The implementation is an adapter onto <see cref="ScenarioCaptureRunner"/>: it translates the
/// legacy <see cref="EngagementCaptureOptions"/> into a generic capture profile (attacker and
/// target actors, one Attack command, a projectile expectation, and target-destroyed stop
/// behavior), preserving the defaults, artifact shape, annotator contract, and exit codes.
/// </summary>
public static class EngagementCaptureRunner
{
    public static void Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var options = config.EngagementCapture ?? new EngagementCaptureOptions();
        var profile = ToProfile(options);
        var settings = new CaptureRunSettings(
            config.Renderer,
            config.FullHud,
            string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(Path.GetTempPath(), "opensage_engagement")
                : options.OutputDirectory!);

        var result = ScenarioCaptureRunner.Run(installation, profile, settings, reporter);

        if (result.Manifest is { } manifest)
        {
            // Legacy reporter line: the health bookends of the shot cycle.
            var start = manifest.Frames.SelectMany(f => f.Actors).FirstOrDefault(a => a.Role == "target")?.Health;
            var end = manifest.Frames.LastOrDefault()?.Actors.FirstOrDefault(a => a.Role == "target")?.Health;
            reporter.Info(FormattableString.Invariant(
                $"Target health: {options.TargetHp:0} -> {(end ?? start ?? 0f):0}"));
        }

        Environment.ExitCode = result.ExitCode;
    }

    internal static CaptureProfile ToProfile(EngagementCaptureOptions options)
    {
        var separation = MathF.Max(10f, options.Separation);
        var axis = new Vector2(
            MathF.Cos(MathUtility.ToRadians(options.AxisAngleDegrees)),
            MathF.Sin(MathUtility.ToRadians(options.AxisAngleDegrees)));
        var center = new Vector2(options.CenterX, options.CenterY);
        var attackerPosition = center - axis * (separation / 2f);
        var targetPosition = center + axis * (separation / 2f);

        var attackerFacing = MathUtility.ToDegrees(FacingTowards(attackerPosition, targetPosition));
        var targetFacing = MathUtility.ToDegrees(FacingTowards(targetPosition, attackerPosition));

        return new CaptureProfile
        {
            Name = "engagement",
            MapPath = options.MapPath,
            DurationFrames = options.Frames,
            SettleFrames = options.SettleFrames,
            CaptureStride = 1,
            HideHud = options.HideHud,
            // Target is civilian-owned: a hostile target would return fire and could kill the
            // attacker mid-capture, and the point of the capture is one side's shot cycle.
            Players =
            [
                new CapturePlayerSlot { Id = "attacker-side", Side = "FactionAmerica", Name = "USA" },
                new CapturePlayerSlot { Id = "unused-china", Side = "FactionChina", Name = "China" },
                new CapturePlayerSlot { Id = "unused-gla", Side = "FactionGLA", Name = "GLA" },
            ],
            Actors =
            [
                new CaptureActor
                {
                    Id = "attacker",
                    Definition = options.AttackerDefinition,
                    Owner = "attacker-side",
                    X = attackerPosition.X,
                    Y = attackerPosition.Y,
                    FacingDegrees = attackerFacing,
                    Label = options.AttackerLabel,
                    Role = "attacker",
                },
                new CaptureActor
                {
                    Id = "target",
                    Definition = options.TargetDefinition,
                    Owner = CaptureProfile.CivilianOwnerId,
                    X = targetPosition.X,
                    Y = targetPosition.Y,
                    FacingDegrees = targetFacing,
                    Label = options.TargetLabel,
                    Role = "target",
                    MaxHealth = options.TargetHp,
                },
            ],
            Camera = new CaptureCamera
            {
                Mode = CaptureCameraMode.AutoFit,
                DesiredSpanFraction = options.DesiredSpanFraction,
                InitialCameraHeight = options.InitialCameraHeight,
                MinCameraHeight = options.MinCameraHeight,
                MaxCameraHeight = options.MaxCameraHeight,
            },
            Commands =
            [
                new CaptureAttackCommand { Frame = 0, SourceActorId = "attacker", TargetActorId = "target" },
            ],
            Expectations = [new CaptureProjectileObservedExpectation()],
            StopWhenActorDestroyed = "target",
        };
    }

    private static float FacingTowards(Vector2 from, Vector2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);
}
