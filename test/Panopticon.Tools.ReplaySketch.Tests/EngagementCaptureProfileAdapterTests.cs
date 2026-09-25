using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class EngagementCaptureProfileAdapterTests
{
    [Fact]
    public void LegacyOptions_MapToEquivalentGenericProfile()
    {
        var options = new EngagementCaptureOptions();
        var profile = EngagementCaptureRunner.ToProfile(options);

        // Manifest compatibility contract: roles must be exactly attacker/target.
        Assert.Equal("attacker", profile.Actors[0].Role);
        Assert.Equal("target", profile.Actors[1].Role);
        Assert.Equal("attacker", profile.Actors[0].Id);
        Assert.Equal("target", profile.Actors[1].Id);

        // Defaults preserved: definition names, HP override, durations, camera bounds.
        Assert.Equal(options.AttackerDefinition, profile.Actors[0].Definition);
        Assert.Equal(options.TargetDefinition, profile.Actors[1].Definition);
        Assert.Equal(options.TargetHp, profile.Actors[1].MaxHealth);
        Assert.Equal(options.Frames, profile.DurationFrames);
        Assert.Equal(options.SettleFrames, profile.SettleFrames);
        Assert.Equal(1, profile.CaptureStride);
        Assert.Equal(options.DesiredSpanFraction, profile.Camera.DesiredSpanFraction);
        Assert.Equal(options.MinCameraHeight, profile.Camera.MinCameraHeight);
        Assert.Equal(options.MaxCameraHeight, profile.Camera.MaxCameraHeight);

        // Target is civilian-owned; attacker is owned by the first player slot.
        Assert.Equal(CaptureProfile.CivilianOwnerId, profile.Actors[1].Owner);
        Assert.Equal(profile.Players[0].Id, profile.Actors[0].Owner);

        // One attack command at frame 0, a projectile expectation, and stop-on-target-death.
        var attack = Assert.IsType<CaptureAttackCommand>(Assert.Single(profile.Commands));
        Assert.Equal(0, attack.Frame);
        Assert.Equal("attacker", attack.SourceActorId);
        Assert.Equal("target", attack.TargetActorId);
        Assert.IsType<CaptureProjectileObservedExpectation>(Assert.Single(profile.Expectations));
        Assert.Equal("target", profile.StopWhenActorDestroyed);

        // Placement: default center (600, 600), separation 120 along the 180-degree axis
        // puts the attacker east and the target west, facing each other.
        Assert.Equal(660f, profile.Actors[0].X, precision: 2);
        Assert.Equal(600f, profile.Actors[0].Y, precision: 2);
        Assert.Equal(540f, profile.Actors[1].X, precision: 2);
        Assert.Equal(180f, profile.Actors[0].FacingDegrees, precision: 1);
        Assert.Equal(0f, profile.Actors[1].FacingDegrees, precision: 1);
    }

    [Fact]
    public void LegacyOptions_RespectSeparationFloor()
    {
        var options = new EngagementCaptureOptions { Separation = 0.5f, CenterX = 100, CenterY = 100 };
        var profile = EngagementCaptureRunner.ToProfile(options);

        var dx = profile.Actors[0].X - profile.Actors[1].X;
        Assert.True(MathF.Abs(dx) >= 9.9f, $"separation floor not applied: dx={dx}");
    }
}
