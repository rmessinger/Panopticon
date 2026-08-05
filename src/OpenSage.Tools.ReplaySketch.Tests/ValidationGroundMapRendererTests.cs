using System.Numerics;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Profiles;
using OpenSage.Tools.ReplaySketch.Services;
using SkiaSharp;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ValidationGroundMapRendererTests
{
    private static readonly Vector3 Min = new(0f, 0f, 0f);
    private static readonly Vector3 Max = new(1000f, 1000f, 0f);

    private static SpawnPlan PlanWith(params (Vector3 Unit, Vector3 Target)[] pairs)
    {
        var units = new List<UnitPlacement>();
        var targets = new List<TargetPlacement>();
        var idx = 0;
        foreach (var (unit, target) in pairs)
        {
            targets.Add(new TargetPlacement("Target", target));
            units.Add(new UnitPlacement("Unit", 2, PlayerIndex: 2, Position: unit, Action: PrimaryAction.Move(200), TargetIndex: idx, RosterIndex: idx));
            idx++;
        }
        return new SpawnPlan { UnitPlacements = units, TargetPlacements = targets };
    }

    [Fact]
    public void Project_MapsWorldToNormalizedScreen()
    {
        // Centre of a 0..1000 world into a 200px preview lands at (100, 100); Y is flipped.
        Assert.Equal(new SKPoint(100f, 100f), ValidationGroundMapRenderer.Project(Min, Max, new Vector3(500f, 500f, 0f), 0, 0, 200f));
        Assert.Equal(new SKPoint(0f, 0f), ValidationGroundMapRenderer.Project(Min, Max, new Vector3(0f, 1000f, 0f), 0, 0, 200f));
        Assert.Equal(new SKPoint(200f, 200f), ValidationGroundMapRenderer.Project(Min, Max, new Vector3(1000f, 0f, 0f), 0, 0, 200f));
    }

    [Fact]
    public void FactionColor_ReturnsDistinctColorPerRosterFaction()
    {
        Assert.NotEqual(ValidationGroundMapRenderer.FactionColor(2), ValidationGroundMapRenderer.FactionColor(3));
        Assert.NotEqual(ValidationGroundMapRenderer.FactionColor(2), ValidationGroundMapRenderer.FactionColor(4));
        Assert.NotEqual(ValidationGroundMapRenderer.FactionColor(3), ValidationGroundMapRenderer.FactionColor(4));
    }

    [Fact]
    public void ComputeMarkers_ReturnsOnePerUnitAndTarget()
    {
        var plan = PlanWith((new Vector3(500f, 500f, 0f), new Vector3(600f, 500f, 0f)));
        var map = MapMetadataService.CreateForTesting(Min, Max);

        var markers = ValidationGroundMapRenderer.ComputeMarkers(map, plan, 0, 0, 200f);

        Assert.Equal(2, markers.Count);
        Assert.Contains(markers, m => m is { FactionIndex: 2, RosterIndex: 0, IsTarget: false });
        Assert.Contains(markers, m => m is { FactionIndex: 2, RosterIndex: 0, IsTarget: true });
    }

    [Fact]
    public void ComputeMarkers_PositionsMatchProjection()
    {
        var unitPos = new Vector3(500f, 500f, 0f);
        var targetPos = new Vector3(600f, 500f, 0f);
        var plan = PlanWith((unitPos, targetPos));
        var map = MapMetadataService.CreateForTesting(Min, Max);

        var markers = ValidationGroundMapRenderer.ComputeMarkers(map, plan, 0, 0, 200f);

        var unit = markers.Single(m => !m.IsTarget);
        var target = markers.Single(m => m.IsTarget);
        Assert.Equal(ValidationGroundMapRenderer.Project(Min, Max, unitPos, 0, 0, 200f), new SKPoint(unit.ScreenX, unit.ScreenY));
        Assert.Equal(ValidationGroundMapRenderer.Project(Min, Max, targetPos, 0, 0, 200f), new SKPoint(target.ScreenX, target.ScreenY));
    }

    [Fact]
    public void ComputeMarkers_HandlesUnitWithoutReachableTarget()
    {
        // A unit whose TargetIndex is out of range still produces its own marker; only the target
        // marker is dropped.
        var plan = new SpawnPlan
        {
            UnitPlacements = new[]
            {
                new UnitPlacement("Unit", 2, PlayerIndex: 2, Position: new Vector3(500f, 500f, 0f),
                    Action: PrimaryAction.Move(200), TargetIndex: 99, RosterIndex: 0),
            },
            TargetPlacements = [],
        };
        var map = MapMetadataService.CreateForTesting(Min, Max);

        var markers = ValidationGroundMapRenderer.ComputeMarkers(map, plan, 0, 0, 200f);

        Assert.Single(markers);
        Assert.False(markers[0].IsTarget);
    }
}
