#nullable enable

using System;
using System.Numerics;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

/// <summary>
/// Unit coverage for the generated-bridge corridor: pose weight inside, along the toes,
/// across the lateral band and for multi-sample vehicles, plus constructor validation
/// and the TerrainLogic registration surface (maximum weight over corridors, cleared
/// with the roadway surfaces).
/// </summary>
public sealed class BridgeCorridorTests
{
    private const double Tolerance = 1e-5;

    private static BridgeRoute Route => new(new Vector2(0f, 0f), new Vector2(0f, 100f));

    private static BridgeCorridor CreateCorridor(double blendDistance = 30)
    {
        return new BridgeCorridor(Route, -40, 40, -200, 200, blendDistance);
    }

    [Fact]
    public void PoseWeight_InsideCorridorIsOne()
    {
        var corridor = CreateCorridor();

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 0f)]));
        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 150f)]));
        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, -150f)]));
        Assert.Equal(1f, corridor.PoseWeight([new Vector2(39.9f, 100f)]));
    }

    [Fact]
    public void PoseWeight_BlendsPastEndToe()
    {
        var corridor = CreateCorridor();

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 200f)]));
        Assert.Equal(0.5, (double)corridor.PoseWeight([new Vector2(0f, 215f)]), Tolerance);
        Assert.Equal(0f, corridor.PoseWeight([new Vector2(0f, 230f)]));
        Assert.Equal(0f, corridor.PoseWeight([new Vector2(0f, 240f)]));
    }

    [Fact]
    public void PoseWeight_BlendsBeforeStartToe()
    {
        var corridor = CreateCorridor();

        Assert.Equal(0.5, (double)corridor.PoseWeight([new Vector2(0f, -215f)]), Tolerance);
        Assert.Equal(0f, corridor.PoseWeight([new Vector2(0f, -230f)]));
    }

    [Fact]
    public void PoseWeight_BlendsAcrossLateralBand()
    {
        var corridor = CreateCorridor();

        Assert.Equal(0.925926, (double)corridor.PoseWeight([new Vector2(45f, 100f)]), Tolerance);
        Assert.Equal(0f, corridor.PoseWeight([new Vector2(70f, 100f)]));
    }

    [Fact]
    public void PoseWeight_MultipliesAlongAndAcrossFalloff()
    {
        var corridor = CreateCorridor();

        Assert.Equal(0.462963, (double)corridor.PoseWeight([new Vector2(45f, 215f)]), Tolerance);
    }

    [Fact]
    public void PoseWeight_MultipleSamples()
    {
        var corridor = CreateCorridor();

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 240f), new Vector2(5f, 100f)]));
        // The lateral extent [0, 45] still overlaps the band, so only the along gap of 15 counts.
        Assert.Equal(0.5, (double)corridor.PoseWeight([new Vector2(0f, 215f), new Vector2(45f, 215f)]), Tolerance);
        // The nearest sample decides the along falloff.
        Assert.Equal(0.5, (double)corridor.PoseWeight([new Vector2(0f, 215f), new Vector2(0f, 230f)]), Tolerance);
    }

    [Fact]
    public void PoseWeight_LateralExitIsContinuous()
    {
        var corridor = CreateCorridor();

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(39.9f, 100f), new Vector2(54.3f, 100f)]));
        Assert.Equal(1f, corridor.PoseWeight([new Vector2(40f, 100f), new Vector2(54.3f, 100f)]));
        Assert.Equal(0.999175, (double)corridor.PoseWeight([new Vector2(40.5f, 100f), new Vector2(54.3f, 100f)]), Tolerance);
        Assert.Equal(0.925926, (double)corridor.PoseWeight([new Vector2(45f, 100f), new Vector2(47f, 100f)]), Tolerance);
    }

    [Fact]
    public void PoseWeight_LateralExitOtherSideIsContinuous()
    {
        var corridor = CreateCorridor();

        Assert.Equal(0.996741, (double)corridor.PoseWeight([new Vector2(-45f, 100f), new Vector2(-41f, 100f)]), Tolerance);
    }

    [Fact]
    public void PoseWeight_StraddleWithoutSampleInsideIsOne()
    {
        var corridor = CreateCorridor();

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 100f), new Vector2(45f, 100f)]));
    }

    [Fact]
    public void PoseWeight_EmptySpanIsZero()
    {
        var corridor = CreateCorridor();

        Assert.Equal(0f, corridor.PoseWeight(ReadOnlySpan<Vector2>.Empty));
    }

    [Fact]
    public void PoseWeight_ZeroBlendDistanceIsStep()
    {
        var corridor = CreateCorridor(0);

        Assert.Equal(1f, corridor.PoseWeight([new Vector2(0f, 0f)]));
        Assert.Equal(0f, corridor.PoseWeight([new Vector2(0f, 201f)]));
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BridgeCorridor(Route, 41, 40, -200, 200, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BridgeCorridor(Route, -40, 40, 201, 200, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BridgeCorridor(Route, -40, 40, -200, 200, -1));
        Assert.Throws<ArgumentException>(() => new BridgeCorridor(default, -40, 40, -200, 200, 30));
    }

    [Fact]
    public void TerrainLogic_RegistersClearsAndTakesMaximumWeight()
    {
        var logic = new TerrainLogic();
        Assert.Equal(0f, logic.GetBridgeCorridorPoseWeight([new Vector2(5f, 5f)]));

        var corridor = CreateCorridor();
        logic.RegisterBridgeCorridor(corridor);
        AssertWeightsMatch(logic, corridor);

        logic.ClearBridgeSurfaces();
        Assert.Equal(0f, logic.GetBridgeCorridorPoseWeight([new Vector2(5f, 5f)]));

        var nearCorridor = CreateCorridor();
        var farCorridor = new BridgeCorridor(Route, -40, 40, 500, 600, 30);
        logic.RegisterBridgeCorridor(nearCorridor);
        logic.RegisterBridgeCorridor(farCorridor);

        // Near corridor gives 0.5, far corridor 0: the maximum is 0.5.
        Assert.Equal(0.5, (double)logic.GetBridgeCorridorPoseWeight([new Vector2(0f, 215f)]), Tolerance);
        // Far corridor gives 1, near corridor 0: the maximum is 1.
        Assert.Equal(1f, logic.GetBridgeCorridorPoseWeight([new Vector2(0f, 530f)]));
    }

    private static void AssertWeightsMatch(TerrainLogic logic, BridgeCorridor corridor)
    {
        foreach (var samples in new[]
        {
            new[] { new Vector2(0f, 0f) },
            new[] { new Vector2(0f, 215f) },
            new[] { new Vector2(0f, 230f) },
            new[] { new Vector2(45f, 100f) },
            new[] { new Vector2(45f, 215f) },
            new[] { new Vector2(0f, 215f), new Vector2(45f, 215f) },
            new[] { new Vector2(0f, 215f), new Vector2(0f, 230f) }
        })
        {
            Assert.Equal((double)corridor.PoseWeight(samples), (double)logic.GetBridgeCorridorPoseWeight(samples), Tolerance);
        }
    }
}
