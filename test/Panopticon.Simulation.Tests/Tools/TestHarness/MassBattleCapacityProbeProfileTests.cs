using System;
using System.IO;
using Panopticon.Tools.TestHarness;
using Xunit;

namespace Panopticon.Tests.Tools.TestHarness;

// WI-M1-01: install-free coverage of the capacity-probe profile shape and validation. The live
// spawn/engage run is the Interactive scenario test and the TestHarness built-in scenario.

public class MassBattleCapacityProbeProfileTests
{
    [Fact]
    public void EmbeddedCapacityProbeProfile_SumsToUnitsPerSide()
    {
        var profile = MassBattleDeploymentProfile.Load(FindProfile("mass-battle-deepdivide-capacity-probe.json"));

        Assert.Equal(2, profile.Sides.Count);
        Assert.Equal(1000, profile.Capacity.UnitsPerSide);
        Assert.Equal(992, profile.Capacity.GroundUnitsPerSide);
        Assert.NotNull(profile.Capacity.Aircraft);
        Assert.Equal("AmericaJetAurora", profile.Capacity.Aircraft!.Definition);
        Assert.Equal(8, profile.Capacity.Aircraft.Count);
        Assert.Equal(1000, profile.Capacity.TotalUnitsPerSide);
        Assert.Null(MassBattleCapacityProbeRunner.ValidateProfile(profile));
    }

    [Fact]
    public void DeploymentProfile_HasNoAircraftAndFailsProbeValidation()
    {
        var profile = MassBattleDeploymentProfile.Load(FindProfile("mass-battle-deepdivide-deployment.json"));

        Assert.Null(profile.Capacity.Aircraft);
        Assert.Equal(1000, profile.Capacity.TotalUnitsPerSide);
        Assert.NotNull(MassBattleCapacityProbeRunner.ValidateProfile(profile));
    }

    [Fact]
    public void ValidateProfile_RejectsMismatchedAircraftSums()
    {
        var loaded = MassBattleDeploymentProfile.Load(FindProfile("mass-battle-deepdivide-capacity-probe.json"));
        var profile = loaded with
        {
            Capacity = loaded.Capacity with
            {
                Aircraft = loaded.Capacity.Aircraft! with { Count = 9 },
            },
        };

        var error = MassBattleCapacityProbeRunner.ValidateProfile(profile);
        Assert.NotNull(error);
        Assert.Contains("unitsPerSide", error);
    }

    [Fact]
    public void ValidateProfile_RejectsSingleSideProfiles()
    {
        var loaded = MassBattleDeploymentProfile.Load(FindProfile("mass-battle-deepdivide-capacity-probe.json"));
        var profile = loaded with { Sides = [loaded.Sides[0]] };

        var error = MassBattleCapacityProbeRunner.ValidateProfile(profile);
        Assert.NotNull(error);
        Assert.Contains("2 sides", error);
    }

    private static string FindProfile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && current.Name != "bin")
        {
            current = current.Parent;
        }

        var testProject = current?.Parent ?? throw new InvalidOperationException("Could not locate test output root.");
        var repoRoot = testProject.Parent?.Parent
            ?? throw new InvalidOperationException("Could not locate the repository root.");
        var path = Path.Combine(repoRoot.FullName, "src", "Panopticon.Tools.TestHarness", fileName);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Profile '{path}' not found.");
    }
}
