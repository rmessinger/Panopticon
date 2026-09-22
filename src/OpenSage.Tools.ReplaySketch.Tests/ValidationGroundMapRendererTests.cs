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
    public void ComputeMarkers_DisplacesTargetSquareWhenPairOverlapsScreen()
    {
        // A proximity-pinned pair (WI-VG-04 terrorist: target 8 world units away) projects ~1.6px
        // apart at this scale, under the circle; the drawn square must be nudged out to the
        // minimum separation while keeping its identity (drag/hit-test still resolve to it).
        var plan = PlanWith((new Vector3(500f, 500f, 0f), new Vector3(508f, 500f, 0f)));
        var map = MapMetadataService.CreateForTesting(Min, Max);

        var markers = ValidationGroundMapRenderer.ComputeMarkers(map, plan, 0, 0, 200f);

        var unit = markers.Single(m => !m.IsTarget);
        var target = markers.Single(m => m.IsTarget);
        var distance = MathF.Sqrt(
            MathF.Pow(unit.ScreenX - target.ScreenX, 2f) + MathF.Pow(unit.ScreenY - target.ScreenY, 2f));
        Assert.True(distance >= ValidationGroundMapRenderer.MinimumMarkerSeparationPx - 0.01f,
            $"pair drawn {distance:F1}px apart, need {ValidationGroundMapRenderer.MinimumMarkerSeparationPx}");
        Assert.Equal((2, 0, true), (target.FactionIndex, target.RosterIndex, target.IsTarget));
    }

    [Fact]
    public void ComputeMarkers_EveryUnitTargetPairClearsMinimumSeparation()
    {
        // Stress: the full standard plan (all three factions, 80-unit standoffs and the pinned
        // terrorist) rendered into a small preview where even normal pairs project inside the
        // marker sizes; every pair must still be drawn at or beyond the minimum separation.
        var scenario = ValidationGroundScenarioStore.Standard();
        scenario.Layout.Mode = LayoutMode.Clusters;
        var plan = ValidationGroundBuilder.Build(scenario, MapMetadataService.CreateForTesting(
            new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f)));
        var map = MapMetadataService.CreateForTesting(new Vector3(0f, 0f, 0f), new Vector3(2390f, 2590f, 0f));

        var markers = ValidationGroundMapRenderer.ComputeMarkers(map, plan, 0, 0, 200f);

        for (var i = 0; i < markers.Count - 1; i++)
        {
            var target = markers[i];
            if (!target.IsTarget) continue;
            var unit = markers[i + 1];
            if (unit.IsTarget || unit.FactionIndex != target.FactionIndex || unit.RosterIndex != target.RosterIndex)
            {
                continue;
            }

            var distance = MathF.Sqrt(
                MathF.Pow(unit.ScreenX - target.ScreenX, 2f) + MathF.Pow(unit.ScreenY - target.ScreenY, 2f));
            Assert.True(distance >= ValidationGroundMapRenderer.MinimumMarkerSeparationPx - 0.01f,
                $"faction {unit.FactionIndex} roster {unit.RosterIndex} pair drawn {distance:F1}px apart");
        }
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

    [Fact]
    public void ComputeStructureRects_ProjectsEveryCivilianStructure()
    {
        var structures = new[]
        {
            new MapStructure("GermanPub01", new Vector3(500f, 500f, 0f)),
            new MapStructure("CivilianBunker01", new Vector3(1000f, 0f, 0f)),
        };
        var map = MapMetadataService.CreateForTesting(Min, Max, structures);

        var rects = ValidationGroundMapRenderer.ComputeStructureRects(map, 0, 0, 200f);

        Assert.Equal(2, rects.Count);
        var center = ValidationGroundMapRenderer.Project(Min, Max, structures[0].Position, 0, 0, 200f);
        Assert.Equal(center.X, (rects[0].Left + rects[0].Right) / 2f, precision: 1);
        Assert.Equal(center.Y, (rects[0].Top + rects[0].Bottom) / 2f, precision: 1);
    }

    [Fact]
    public void CivilianStructureCatalog_MatchesCivilianFamiliesOnly()
    {
        foreach (var civilian in new[]
                 {
                     "GermanTownHouse1", "GermanTownHouseBib2", "GermanPub01", "GermanCotage01",
                     "GermanCottageBib1", "CivilianBunker01", "StanSchoolhouse02", "StanGasStation",
                     "QuonsetHut01", "CivilianTEROilDepot", "SupplyDock", "SupplyPile",
                     "SupplyPileSmall", "SupplyWarehouse",
                 })
        {
            Assert.True(CivilianStructureCatalog.IsCivilianStructure(civilian), civilian);
        }

        foreach (var other in new[]
                 {
                     "AmericaBarracks", "ChinaCommandCenter", "GLAInfantryRebel", "CarAsian1",
                     "MonumentTrainBridge", "BridgeTowerConcreteLeft01",
                 })
        {
            Assert.False(CivilianStructureCatalog.IsCivilianStructure(other), other);
        }
    }
}
