using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

[Collection("Navigation")]
public class BlockedMovementResolverTests : MockedGameTest
{
    private NavigationClass CreateNavigation()
    {
        var blendTileData = BlendTileData.Create(16, 16);
        var heightMapData = HeightMapData.Create(0, new ushort[16, 16]);
        var heightMap = new HeightMap(heightMapData);
        return new NavigationClass(blendTileData, heightMap);
    }

    private static Vector3 World(int x, int y) => new(x * 10, y * 10, 0);

    [Fact]
    public void Resolve_DetoursAroundAStationaryBlocker()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var unit = NewUnit(World(8, 2), game: game);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game);
        blocker.Geometry.Shapes[0].MajorRadius = 2;
        blocker.Geometry.Shapes[0].MinorRadius = 2;

        var ai = unit.AIUpdate!;
        var goal = World(1, 2);
        ai.TargetPoints.Add(World(6, 2));
        ai.TargetPoints.Add(World(5, 2));
        ai.TargetPoints.Add(World(4, 2));
        ai.TargetPoints.Add(goal);

        var statsBefore = navigation.Statistics;
        // First step establishes the baseline, the next two trip the wait threshold.
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        var stats = navigation.Statistics;
        Assert.Equal(1, stats.BlockedRepathRequests - statsBefore.BlockedRepathRequests);
        Assert.True(ai.TargetPoints.Count > 0);
        Assert.All(ai.TargetPoints, point => Assert.True(navigation.IsPassable(point)));
        Assert.All(ai.TargetPoints, point =>
            Assert.NotEqual(World(5, 2), point));
        Assert.Equal(goal, ai.TargetPoints[^1]);
    }

    [Fact]
    public void Resolve_BoundsExpansionToTheWindowSquare()
    {
        var navigation = CreateNavigation();
        var window = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var unit = NewUnit(World(8, 2), game: game);
        var ai = unit.AIUpdate!;

        var statsBefore = navigation.Statistics;
        var detour = navigation.CalculateLocalPath(
            World(8, 2), World(8, 2) + new Vector3(60, 0, 0), window,
            LocalOccupancyWindow.Collect(unit, 0, 0, 160, 160));
        var stats = navigation.Statistics;

        Assert.True(detour.Count > 0);
        var expanded = stats.NodesExpanded - statsBefore.NodesExpanded;
        Assert.True(expanded <= (2 * window + 1) * (2 * window + 1), $"expanded {expanded}");

        var failed = navigation.CalculateLocalPath(
            World(8, 2), new Vector3(1_000_000, 0, 0), window, new LocalOccupancyWindow());
        Assert.Empty(failed);
        // The first call succeeded; only the off-graph rejoin failed.
        Assert.Equal(statsBefore.FailedSearches + 1, navigation.Statistics.FailedSearches);
    }

    [Fact]
    public void Resolve_RespectsTheAttemptCooldownAndProgressingBlockers()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var unit = NewUnit(World(8, 2), game: game);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game);
        blocker.Geometry.Shapes[0].MajorRadius = 2;
        blocker.Geometry.Shapes[0].MinorRadius = 2;

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(1, 2));
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        var afterFirst = navigation.Statistics.BlockedRepathRequests;

        // The very next frame is inside the cooldown: no second search.
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        Assert.Equal(afterFirst, navigation.Statistics.BlockedRepathRequests);

        // A blocker that is itself advancing is waited out, never repathed around.
        blocker.AIUpdate!.TargetPoints.Add(World(1, 2));
        blocker.ModelConditionFlags.Set(ModelConditionFlag.Moving, true);
        ai.ResetLocalRepath();
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        Assert.Equal(afterFirst, navigation.Statistics.BlockedRepathRequests);
    }

    [Fact]
    public void Resolve_GivesUpAndSettlesGoalCrowdedWithinTheArrivalRadius()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        navigation.Options.MaxResolutionAttempts = 1;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var unit = NewUnit(World(8, 2), game: game);
        var blocker = NewRegisteredUnit(World(7, 2), ObjectKinds.Vehicle, game);
        blocker.Geometry.Shapes[0].MajorRadius = 2;
        blocker.Geometry.Shapes[0].MinorRadius = 2;

        var ai = unit.AIUpdate!;
        // Goal three cells away, inside the default 60-unit crowd arrival radius.
        ai.TargetPoints.Add(World(5, 2));
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        // Attempt budget exhausted: the next blocked wait settles the unit.
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        Assert.Empty(ai.TargetPoints);
        Assert.Equal(NavigationStallReason.GoalCrowded, ai.Progress.Reason);
    }

    [Fact]
    public void Resolve_GivesUpButKeepsTheRouteWhenTheGoalIsFar()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        navigation.Options.MaxResolutionAttempts = 1;
        var game = new TestGame(SageGame.CncGenerals, navigation);

        var unit = NewUnit(World(8, 2), game: game);
        var blocker = NewRegisteredUnit(World(7, 2), ObjectKinds.Vehicle, game);
        blocker.Geometry.Shapes[0].MajorRadius = 2;
        blocker.Geometry.Shapes[0].MinorRadius = 2;

        var ai = unit.AIUpdate!;
        var goal = World(1, 12);
        ai.TargetPoints.Add(goal);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        var pointsAfterFirst = ai.TargetPoints.Count;

        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        ai.Progress.RecordMoveStep(100f, true, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        Assert.Equal(pointsAfterFirst, ai.TargetPoints.Count);
        Assert.Equal(goal, ai.TargetPoints[^1]);
        Assert.Equal(NavigationStallReason.BlockedByUnit, ai.Progress.Reason);
    }

    [Fact]
    public void NavigationOptions_DefaultToResolutionOff()
    {
        var options = new NavigationOptions();
        Assert.False(options.BlockedUnitResolution);
        Assert.Equal(15, options.BlockedWaitFrames);
        Assert.Equal(12, options.LocalRepathWindowCells);
        Assert.Equal(3, options.MaxResolutionAttempts);
        Assert.Equal(60.0f, options.CrowdArrivalRadius);
    }

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry, TestGame? game = null)
    {
        var host = game ?? Generals;
        var gameObject = new GameObject(BuildDefinition(kind), host.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    // Blockers must be registered: the resolver resolves them through the object collection.
    private GameObject NewRegisteredUnit(Vector3 position, ObjectKinds kind, TestGame game)
    {
        var gameObject = game.GameLogic.CreateObject(BuildDefinition(kind), NewPlayer());
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private ObjectDefinition BuildDefinition(ObjectKinds kind)
    {
        var definition = new ObjectDefinition($"BlockedMovement_{kind}_{_nextObjectId++}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        return definition;
    }

    private Panopticon.Logic.Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
