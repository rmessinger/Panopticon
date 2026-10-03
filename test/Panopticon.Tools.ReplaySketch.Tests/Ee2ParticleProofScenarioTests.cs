using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Tools.TestHarness;
using Xunit;
using ActionType = Panopticon.Tools.ReplaySketch.Model.ActionType;

namespace Panopticon.Tools.ReplaySketch.Tests;

public sealed class Ee2ParticleProofScenarioTests
{
    private sealed class StringReporter : ITestHarnessReporter
    {
        public System.Text.StringBuilder Errors { get; } = new();

        public void Info(string message)
        {
        }

        public void Error(string message)
        {
            Errors.AppendLine(message);
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(
            Panopticon.Logic.OutcomeStatus status,
            Panopticon.Logic.OutcomeReason reason,
            string? winnerName,
            uint frame)
        {
        }
    }

    [Fact]
    public void CapturePlanCoversThreeEffectsAtThreeTimes()
    {
        var plan = Ee2ParticleProofScenario.CapturePlan;
        Assert.Equal(9, plan.Length);

        var expected = new (string EffectName, float TimeSeconds)[]
        {
            ("fx_explosion_1", 0f),
            ("fx_explosion_1", 0.5f),
            ("fx_explosion_1", 1f),
            ("fx_muzzle_tank", 0f),
            ("fx_muzzle_tank", 0.5f),
            ("fx_muzzle_tank", 1f),
            ("fx_watersplash_1", 0f),
            ("fx_watersplash_1", 0.5f),
            ("fx_watersplash_1", 1f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].EffectName, plan[i].EffectName);
            Assert.Equal(expected[i].TimeSeconds, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void CapturePlanEffectsMatchTheProofBindings()
    {
        var planEffects = new HashSet<string>();
        foreach (var entry in Ee2ParticleProofScenario.CapturePlan)
        {
            planEffects.Add(entry.EffectName);
        }

        var bindingEffects = new HashSet<string>();
        foreach (var binding in Panopticon.Content.Importing.Nif.NifParticleEffectBinding.GetProofBindings())
        {
            bindingEffects.Add(binding.EffectName);
        }

        Assert.Equal(bindingEffects, planEffects);
    }

    [Fact]
    public void ParticleCaptureFileNamesUseEffectAndPaddedMilliseconds()
    {
        Assert.Equal(
            "fx_explosion_1-particle-0000.png",
            Ee2ParticleProofScenario.GetParticleCaptureFileName("fx_explosion_1", 0f));
        Assert.Equal(
            "fx_muzzle_tank-particle-0500.png",
            Ee2ParticleProofScenario.GetParticleCaptureFileName("fx_muzzle_tank", 0.5f));
        Assert.Equal(
            "fx_watersplash_1-particle-1000.png",
            Ee2ParticleProofScenario.GetParticleCaptureFileName("fx_watersplash_1", 1f));
    }

    [Fact]
    public void EffectSeedsAreStableAndDistinctPerEffect()
    {
        foreach (var effectName in new[] { "fx_explosion_1", "fx_muzzle_tank", "fx_watersplash_1" })
        {
            Assert.Equal(Ee2ParticleProofScenario.GetEffectSeed(effectName), Ee2ParticleProofScenario.GetEffectSeed(effectName));
        }

        Assert.NotEqual(
            Ee2ParticleProofScenario.GetEffectSeed("fx_explosion_1"),
            Ee2ParticleProofScenario.GetEffectSeed("fx_muzzle_tank"));
        Assert.NotEqual(
            Ee2ParticleProofScenario.GetEffectSeed("fx_muzzle_tank"),
            Ee2ParticleProofScenario.GetEffectSeed("fx_watersplash_1"));
    }

    [Fact]
    public void ValidationGuideListsParticleCapturesAndSeedDeterminism()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ee2-particle-guide-test");
        Directory.CreateDirectory(directory);

        try
        {
            var captures = new List<(string FileName, string Check)>
            {
                ("fx_explosion_1-particle-0500.png", "expanded fading cloud"),
                ("fx_muzzle_tank-particle-0000.png", "first smoke burst at the tag_muzzle_1 firing tag"),
                ("fx_watersplash_1-particle-1000.png", "collapsing splash"),
            };

            Ee2NifProofScenario.WriteValidationGuide(
                directory,
                captures,
                title: "# EE2 particle proof - validation guide",
                preamble: new[] { "BeginSeedScope seeded from the effect key" });

            var guide = File.ReadAllText(Path.Combine(directory, "validation_guide.md"));
            Assert.Contains("# EE2 particle proof - validation guide", guide);
            Assert.Contains("fx_explosion_1-particle-0500.png", guide);
            Assert.Contains("fx_muzzle_tank-particle-0000.png", guide);
            Assert.Contains("fx_watersplash_1-particle-1000.png", guide);
            Assert.Contains("BeginSeedScope", guide);
            Assert.Contains("Deterministic repeat", guide);
        }

        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingEe2InstallIsReportedDirectly()
    {
        var previous = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", null);
        try
        {
            var reporter = new StringReporter();
            var exitCode = Ee2ParticleProofScenario.Run(null!, new TestHarnessConfig(), reporter);
            Assert.Equal(1, exitCode);
            Assert.Contains("EMPIRE_EARTH_II_PATH", reporter.Errors.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", previous);
        }
    }

    [Fact]
    public void MissingGraphicsArchiveIsReportedDirectly()
    {
        var previous = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        var tempRoot = Path.Combine(Path.GetTempPath(), "ee2-particle-missing-test");
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", tempRoot);
        try
        {
            var reporter = new StringReporter();
            var exitCode = Ee2ParticleProofScenario.Run(null!, new TestHarnessConfig(), reporter);
            Assert.Equal(1, exitCode);
            Assert.Contains("graphics.zip", reporter.Errors.ToString());
        }

        finally
        {
            Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", previous);
            Directory.Delete(tempRoot, recursive: false);
        }
    }

    [Fact]
    public void ScenarioIsDispatchedByBuiltInScenario()
    {
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2ParticleProof));
    }
}
