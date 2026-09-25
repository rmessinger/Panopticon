using System.Linq;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Structural (non-interactive) tests for the "build one of everything" variant of the
/// Alpine Assault scenario. These verify the profile JSON parses correctly and that its
/// action graph matches the intended design: build every building, train every unit, and
/// only attack once the last unit in the chain has been produced. They do not require a
/// real game installation.
/// </summary>
public class ReplaySketchFullBuildTests
{
    [Fact]
    public void FullBuildScenario_Usa_BuildsEveryCatalogBuilding()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var usa = scenario.Players[0];

        var constructedBuildings = usa.Actions
            .Where(a => a.Type == ActionType.Construct)
            .Select(a => a.BuildingDefinitionName!)
            .ToList();

        var expectedBuildings = BuildingCatalog.ForFaction(usa.FactionIndex)
            .Select(b => b.DefinitionName)
            .ToList();

        Assert.Equal(expectedBuildings.Count, constructedBuildings.Count);
        Assert.Equal(expectedBuildings.ToHashSet(), constructedBuildings.ToHashSet());
    }

    [Fact]
    public void FullBuildScenario_Usa_TrainsEveryCatalogUnit_WithQuantityOne()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var usa = scenario.Players[0];

        var recruitActions = usa.Actions.Where(a => a.Type == ActionType.RecruitBasicUnit).ToList();
        var recruitedUnits = recruitActions.Select(a => a.UnitDefinitionName!).ToList();

        var expectedUnits = UnitCatalog.ForFaction(usa.FactionIndex)
            .Select(u => u.DefinitionName)
            .ToList();

        Assert.Equal(expectedUnits.Count, recruitedUnits.Count);
        Assert.Equal(expectedUnits.ToHashSet(), recruitedUnits.ToHashSet());
        Assert.All(recruitActions, a => Assert.Equal(1, a.Quantity));
    }

    [Fact]
    public void FullBuildScenario_Gla_BuildsEveryCatalogBuilding()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var gla = scenario.Players[1];

        var constructedBuildings = gla.Actions
            .Where(a => a.Type == ActionType.Construct)
            .Select(a => a.BuildingDefinitionName!)
            .ToList();

        var expectedBuildings = BuildingCatalog.ForFaction(gla.FactionIndex)
            .Select(b => b.DefinitionName)
            .ToList();

        Assert.Equal(expectedBuildings.Count, constructedBuildings.Count);
        Assert.Equal(expectedBuildings.ToHashSet(), constructedBuildings.ToHashSet());
    }

    [Fact]
    public void FullBuildScenario_Gla_TrainsEveryCatalogUnit_WithQuantityOne()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var gla = scenario.Players[1];

        var recruitActions = gla.Actions.Where(a => a.Type == ActionType.RecruitBasicUnit).ToList();
        var recruitedUnits = recruitActions.Select(a => a.UnitDefinitionName!).ToList();

        var expectedUnits = UnitCatalog.ForFaction(gla.FactionIndex)
            .Select(u => u.DefinitionName)
            .ToList();

        Assert.Equal(expectedUnits.Count, recruitedUnits.Count);
        Assert.Equal(expectedUnits.ToHashSet(), recruitedUnits.ToHashSet());
        Assert.All(recruitActions, a => Assert.Equal(1, a.Quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FullBuildScenario_AllBuildingsPrecedeAllUnits(int playerIndex)
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var player = scenario.Players[playerIndex];

        var lastBuildingIndex = player.Actions.FindLastIndex(a => a.Type == ActionType.Construct);
        var firstUnitIndex = player.Actions.FindIndex(a => a.Type == ActionType.RecruitBasicUnit);

        Assert.True(lastBuildingIndex < firstUnitIndex,
            "Every building must be queued before the first unit is recruited, so producer " +
            "buildings (Barracks/War Factory/Airfield/Arms Dealer) are always complete in time.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FullBuildScenario_AttackOnlyTriggersOnLastUnitCompleted(int playerIndex)
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var player = scenario.Players[playerIndex];

        var attack = Assert.Single(player.Actions, a => a.Type == ActionType.AttackEnemyBase);
        var lastUnit = player.Actions.Last(a => a.Type == ActionType.RecruitBasicUnit);

        var timing = Assert.IsType<AfterActionTiming>(attack.Timing);
        Assert.Equal(lastUnit.Id, timing.TriggerActionId);
        Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FullBuildScenario_ConstructActionsChainOnPreviousCompleted(int playerIndex)
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var player = scenario.Players[playerIndex];

        var buildings = player.Actions.Where(a => a.Type == ActionType.Construct).ToList();
        for (var i = 1; i < buildings.Count; i++)
        {
            var timing = Assert.IsType<AfterActionTiming>(buildings[i].Timing);
            Assert.Equal(buildings[i - 1].Id, timing.TriggerActionId);
            Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FullBuildScenario_RecruitActionsChainOnPreviousCompleted(int playerIndex)
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLAFullBuild();
        var player = scenario.Players[playerIndex];

        var units = player.Actions.Where(a => a.Type == ActionType.RecruitBasicUnit).ToList();
        for (var i = 1; i < units.Count; i++)
        {
            var timing = Assert.IsType<AfterActionTiming>(units[i].Timing);
            Assert.Equal(units[i - 1].Id, timing.TriggerActionId);
            Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
        }
    }
}
