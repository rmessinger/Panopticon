#nullable enable

using System;
using System.Linq;
using OpenSage.Tools.TestHarness;
using Xunit;

namespace OpenSage.Tests.Tools;

public class GltfRuntimeProofScenarioTests
{
    [Fact]
    public void BuiltInScenario_ParsesGltfRuntimeProof()
    {
        Assert.True(Enum.TryParse<BuiltInScenario>("GltfRuntimeProof", ignoreCase: true, out _));
    }

    [Fact]
    public void RequiredFixtures_CoverEveryCapturedFixture()
    {
        var captured = GltfRuntimeProofScenario.CapturePlan.Select(p => p.Fixture).Distinct();
        Assert.All(captured, fixture => Assert.Contains(fixture, GltfRuntimeProofScenario.RequiredFixtures));
    }

    [Fact]
    public void CapturePlan_HasStaticPbr_TwoRigidTimes_AndThreeSkinTimesIncludingStepMidpoint()
    {
        var plan = GltfRuntimeProofScenario.CapturePlan;

        Assert.Equal(3, plan.Length);

        var pbr = plan.Single(p => p.Fixture == "pbr-materials.gltf");
        Assert.Null(pbr.AnimationIndex);
        Assert.Equal([0f], pbr.Times);

        var rigid = plan.Single(p => p.Fixture == "rigid-scene.gltf");
        Assert.Equal(0, rigid.AnimationIndex);
        Assert.Equal(2, rigid.Times.Length);

        var skin = plan.Single(p => p.Fixture == "animated-skin.glb");
        Assert.Equal(0, skin.AnimationIndex);
        Assert.Equal(3, skin.Times.Length);
        Assert.Contains(0.5f, skin.Times);
    }

    [Fact]
    public void MissingFixtures_ReportsAbsentFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gltf-proof-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var missing = GltfRuntimeProofScenario.MissingFixtures(temp);

            Assert.Equal(GltfRuntimeProofScenario.RequiredFixtures.Length, missing.Count);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Config_CarriesGltfFixtureRoot()
    {
        var config = new TestHarnessConfig { GltfFixtureRoot = "/tmp/fixtures" };

        Assert.Equal("/tmp/fixtures", config.GltfFixtureRoot);
    }
}
