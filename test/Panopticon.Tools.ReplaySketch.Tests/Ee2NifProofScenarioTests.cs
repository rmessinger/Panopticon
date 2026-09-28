using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Tools.TestHarness;
using Xunit;
using ActionType = Panopticon.Tools.ReplaySketch.Model.ActionType;

namespace Panopticon.Tools.ReplaySketch.Tests;

public sealed class Ee2NifProofScenarioTests
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
    public void CapturePlanCoversRigidAndSkinnedProofModels()
    {
        Assert.Equal(3, Ee2NifProofScenario.CapturePlan.Length);
        Assert.Equal("nwg1_galley", Ee2NifProofScenario.CapturePlan[0].Key);
        Assert.Equal("ab11_TwinEngineBomber", Ee2NifProofScenario.CapturePlan[1].Key);
        Assert.Equal("CIV1_Druid_british", Ee2NifProofScenario.CapturePlan[2].Key);
        Assert.All(
            Ee2NifProofScenario.CapturePlan,
            entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void AnimatedCapturePlanCoversDeepDiveSectionsEightAndEleven()
    {
        var plan = Ee2NifProofScenario.AnimatedCapturePlan;
        Assert.Equal(13, plan.Length);

        var expected = new[]
        {
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f),
            ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 0f),
            ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f),
            ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0f),
            ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.2f),
            ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.4f),
            ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_attack01.kf", 0.9333f),
            ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_death01.kf", 1.2333f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, plan[i].ModelKey);
            Assert.Equal(expected[i].Item2, plan[i].KfPath);
            Assert.Equal(expected[i].Item3, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void AnimatedCaptureFileNameUsesKfStemAndPaddedMilliseconds()
    {
        Assert.Equal(
            "ee2-nwg1_galley_walk_0000.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 0f));
        Assert.Equal(
            "ee2-nwg1_galley_walk_0767.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f));
        Assert.Equal(
            "ee2-nwg1_galley_walk_1433.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName("Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f));
        Assert.Equal(
            "ee2-ab11_TwinEngineBomber_walk_0083.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f));
        Assert.Equal(
            "ee2-ab11_TwinEngineBomber_takeoff_1333.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f));
        Assert.Equal(
            "ee2-CIV1_Druid_british_walk01_0200.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.2f));
        Assert.Equal(
            "ee2-CIV1_Druid_british_attack01_0933.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Humans/CIV1_Druid_british_attack01.kf", 0.9333f));
        Assert.Equal(
            "ee2-CIV1_Druid_british_death01_1233.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Humans/CIV1_Druid_british_death01.kf", 1.2333f));
    }

    [Fact]
    public void ValidationGuideListsAnimatedCapturesAndDeterminismNote()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ee2-proof-guide-test");
        Directory.CreateDirectory(directory);

        try
        {
            var captures = new List<(string FileName, string Check)>
            {
                ("ee2-nwg1_galley.png", "static hull"),
                ("ee2-nwg1_galley_walk_0767.png", "mid-stroke oars"),
                ("ee2-ab11_TwinEngineBomber_walk_0083.png", "quarter-turn props"),
            };

            Ee2NifProofScenario.WriteValidationGuide(directory, captures);

            var guide = File.ReadAllText(Path.Combine(directory, "validation_guide.md"));
            Assert.Contains("ee2-nwg1_galley_walk_0767.png", guide);
            Assert.Contains("ee2-ab11_TwinEngineBomber_walk_0083.png", guide);
            Assert.Contains("NifAnimationImporter", guide);
            Assert.Contains("Deterministic repeat", guide);
            Assert.Contains("synchronously", guide);
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
            var exitCode = Ee2NifProofScenario.Run(null!, new TestHarnessConfig(), reporter);
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
        var tempRoot = Path.Combine(Path.GetTempPath(), "ee2-proof-missing-test");
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("EMPIRE_EARTH_II_PATH", tempRoot);
        try
        {
            var reporter = new StringReporter();
            var exitCode = Ee2NifProofScenario.Run(null!, new TestHarnessConfig(), reporter);
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
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2NifProof));
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2PropertyProof));
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2ScaleProof));
        Assert.True(Enum.IsDefined<BuiltInScenario>(BuiltInScenario.Ee2MorphProof));
    }

    [Fact]
    public void MorphCapturePlanCoversTheFarmYam()
    {
        var plan = Ee2NifProofScenario.MorphCapturePlan;
        var entry = Assert.Single(plan);
        Assert.Equal(Ee2NifProofScenario.FarmYamMorphModelKey, entry.Key);
        Assert.False(string.IsNullOrWhiteSpace(entry.Check));
    }

    [Fact]
    public void MorphAnimatedCapturePlanCoversWeightsZeroHalfAndOne()
    {
        var plan = Ee2NifProofScenario.MorphAnimatedCapturePlan;
        Assert.Equal(3, plan.Length);

        var expected = new[]
        {
            (Ee2NifProofScenario.FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0f),
            (Ee2NifProofScenario.FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0.4833f),
            (Ee2NifProofScenario.FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0.9667f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, plan[i].ModelKey);
            Assert.Equal(expected[i].Item2, plan[i].KfPath);
            Assert.Equal(expected[i].Item3, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void MorphCaptureFileNamesUseStemAndMorphSuffix()
    {
        Assert.Equal(
            "bld_farm_yam-morph-0000.png",
            Ee2NifProofScenario.GetMorphCaptureFileName("Graphics/Building/bld_farm_yam.kf", 0f));
        Assert.Equal(
            "bld_farm_yam-morph-0483.png",
            Ee2NifProofScenario.GetMorphCaptureFileName("Graphics/Building/bld_farm_yam.kf", 0.4833f));
        Assert.Equal(
            "bld_farm_yam-morph-0967.png",
            Ee2NifProofScenario.GetMorphCaptureFileName("Graphics/Building/bld_farm_yam.kf", 0.9667f));
    }

    [Fact]
    public void ScaleCapturePlanCoversTheThreeAircraft()
    {
        var plan = Ee2NifProofScenario.ScaleCapturePlan;
        Assert.Equal(3, plan.Length);
        Assert.Equal("ab14_stealthbomber", plan[0].Key);
        Assert.Equal("ab15_orbital", plan[1].Key);
        Assert.Equal("afb13_StrikeFighter", plan[2].Key);
        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void ScaleAnimatedCapturePlanCoversFirstAndFinalTakeoffKeys()
    {
        var plan = Ee2NifProofScenario.ScaleAnimatedCapturePlan;
        Assert.Equal(6, plan.Length);

        var expected = new[]
        {
            ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 0f),
            ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 3.3333f),
            ("ab15_orbital", "Graphics/Vehicles/ab15_orbital_takeoff.kf", 0f),
            ("ab15_orbital", "Graphics/Vehicles/ab15_orbital_takeoff.kf", 0.6667f),
            ("afb13_StrikeFighter", "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf", 0f),
            ("afb13_StrikeFighter", "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf", 3.3333f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, plan[i].ModelKey);
            Assert.Equal(expected[i].Item2, plan[i].KfPath);
            Assert.Equal(expected[i].Item3, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void ScaleCaptureFileNamesUseStemAndScaleSuffix()
    {
        Assert.Equal(
            "ab14_stealthbomber_takeoff-scale-0000.png",
            Ee2NifProofScenario.GetScaleCaptureFileName(
                "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 0f));
        Assert.Equal(
            "ab14_stealthbomber_takeoff-scale-3333.png",
            Ee2NifProofScenario.GetScaleCaptureFileName(
                "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 3.3333f));
        Assert.Equal(
            "ab15_orbital_takeoff-scale-0667.png",
            Ee2NifProofScenario.GetScaleCaptureFileName(
                "Graphics/Vehicles/ab15_orbital_takeoff.kf", 0.6667f));
        Assert.Equal(
            "afb13_StrikeFighter_takeoff-scale-3333.png",
            Ee2NifProofScenario.GetScaleCaptureFileName(
                "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf", 3.3333f));
    }

    [Fact]
    public void PropertyCapturePlanCoversAllFourFamilies()
    {
        var plan = Ee2NifProofScenario.PropertyCapturePlan;
        Assert.Equal(5, plan.Length);
        Assert.Equal("civ12_EIFV_egypt", plan[0].Key);
        Assert.Equal("afb14_StealthFighterBomber", plan[1].Key);
        Assert.Equal("faufish", plan[2].Key);
        Assert.Equal("LHA10_siegegun", plan[3].Key);
        Assert.Equal("bld_market8", plan[4].Key);
        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void PropertyAnimatedCapturePlanCoversAllFourFamilies()
    {
        var plan = Ee2NifProofScenario.PropertyAnimatedCapturePlan;
        Assert.Equal(8, plan.Length);

        var expected = new[]
        {
            ("civ12_EIFV_egypt", "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf", 0f),
            ("civ12_EIFV_egypt", "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf", 1f),
            ("afb14_StealthFighterBomber", "Graphics/Vehicles/afb14_StealthFighterBomber_idle.kf", 0f),
            ("afb14_StealthFighterBomber", "Graphics/Vehicles/afb14_StealthFighterBomber_idle.kf", 0.0333f),
            ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 0f),
            ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 1f),
            ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 2f),
            ("LHA10_siegegun", "Graphics/Vehicles/LHA10_siegegun_idle01.kf", 1f),
        };

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, plan[i].ModelKey);
            Assert.Equal(expected[i].Item2, plan[i].KfPath);
            Assert.Equal(expected[i].Item3, plan[i].TimeSeconds);
        }

        Assert.All(plan, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Check)));
    }

    [Fact]
    public void PropertyCaptureFileNamesUseKfStemAndPaddedMilliseconds()
    {
        Assert.Equal(
            "ee2-civ12_EIFV_egypt_walk_1000.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf", 1f));
        Assert.Equal(
            "ee2-sh_faufish_idle01_0200.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Animals/sh_faufish_idle01.kf", 0.2f));
        Assert.Equal(
            "ee2-LHA10_siegegun_idle01_1000.png",
            Ee2NifProofScenario.GetAnimatedCaptureFileName(
                "Graphics/Vehicles/LHA10_siegegun_idle01.kf", 1f));
    }

    [Fact]
    public void PropertyValidationGuideListsMarketBindLimitation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ee2-property-guide-test");
        Directory.CreateDirectory(directory);

        try
        {
            var captures = new List<(string FileName, string Check)>
            {
                ("ee2-bld_market8.png", "static market"),
                ("ee2-sh_faufish_idle01_1000.png", "mid-fade fish"),
            };

            Ee2NifProofScenario.WriteValidationGuide(
                directory,
                captures,
                title: "# EE2 property animation proof - validation guide",
                preamble: new[] { "market_open_* targets exist in no cooked model" });

            var guide = File.ReadAllText(Path.Combine(directory, "validation_guide.md"));
            Assert.Contains("# EE2 property animation proof - validation guide", guide);
            Assert.Contains("market_open_*", guide);
            Assert.Contains("ee2-sh_faufish_idle01_1000.png", guide);
            Assert.Contains("Deterministic repeat", guide);
        }

        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
