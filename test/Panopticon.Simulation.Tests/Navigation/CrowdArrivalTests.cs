// WI-N9-02: crowd arrival. A final-segment unit held off behind an arrived friendly standing
// within CrowdArrivalRadius of its goal stops and reports CrowdArrived; a unit with a
// reachable free goal never settles that way. Active behind BlockedUnitResolution or
// DestinationSlots.
//
// Run with: dotnet test src --filter "FullyQualifiedName~CrowdArrivalTests"

using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;
using Player = Panopticon.Logic.Player;

namespace Panopticon.Tests.Navigation;

[Collection("Navigation")]
public class CrowdArrivalTests : MockedGameTest
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
    public void SettlesCrowdArrivedBehindAnArrivedFriendlyNearTheGoal()
    {
        var navigation = CreateNavigation();
        navigation.Options.DestinationSlots = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.CrowdArrivalRadius = 60.0f;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        // Arrived friendly standing inside the 60-unit arrival radius of the unit's goal.
        var arrived = NewRegisteredUnit(World(4, 2), ObjectKinds.Vehicle, game, owner);
        var goal = World(3, 2);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(goal);
        DriveBlocked(ai, arrived.Id);

        Assert.True(ai.TryCrowdArrive(navigation));
        Assert.Empty(ai.TargetPoints);
        Assert.Equal(NavigationStallReason.CrowdArrived, ai.Progress.Reason);
    }

    [Fact]
    public void NeverSettlesWhenTheGoalIsReachable()
    {
        var navigation = CreateNavigation();
        navigation.Options.DestinationSlots = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.CrowdArrivalRadius = 60.0f;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        // The blocker stands 70 units from the goal, outside the 60-unit radius.
        var unit = NewUnit(World(12, 2), game: game, player: owner);
        var farBlocker = NewRegisteredUnit(World(8, 2), ObjectKinds.Vehicle, game, owner);
        var goal = World(1, 2);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(goal);
        DriveBlocked(ai, farBlocker.Id);
        Assert.False(ai.TryCrowdArrive(navigation));
        Assert.Equal(NavigationStallReason.BlockedByUnit, ai.Progress.Reason);
        Assert.Single(ai.TargetPoints);
    }

    [Fact]
    public void NeverSettlesForMovingEnemyOrMultiSegmentCases()
    {
        var navigation = CreateNavigation();
        navigation.Options.DestinationSlots = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.CrowdArrivalRadius = 60.0f;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        // Not the final segment.
        var multi = NewUnit(World(12, 2), game: game, player: owner);
        var blockerA = NewRegisteredUnit(World(4, 2), ObjectKinds.Vehicle, game, owner);
        var multiAi = multi.AIUpdate!;
        // The extra waypoint sits 80 units from the goal, outside the pinned 60-unit disk.
        multiAi.TargetPoints.Add(World(9, 2));
        multiAi.TargetPoints.Add(World(1, 2));
        DriveBlocked(multiAi, blockerA.Id);
        Assert.False(multiAi.TryCrowdArrive(navigation));

        // Enemy blocker near the goal.
        var enemy = NewUnit(World(8, 2), game: game, player: owner);
        var enemyBlocker = NewRegisteredUnit(World(4, 2), ObjectKinds.Vehicle, game, new Player(2, null, default, Generals));
        var enemyAi = enemy.AIUpdate!;
        enemyAi.TargetPoints.Add(World(3, 2));
        DriveBlocked(enemyAi, enemyBlocker.Id);
        Assert.False(enemyAi.TryCrowdArrive(navigation));

        // Blocker still under its own order.
        var busy = NewUnit(World(8, 2), game: game, player: owner);
        var busyBlocker = NewRegisteredUnit(World(4, 2), ObjectKinds.Vehicle, game, owner);
        busyBlocker.AIUpdate!.TargetPoints.Add(World(4, 12));
        var busyAi = busy.AIUpdate!;
        busyAi.TargetPoints.Add(World(3, 2));
        DriveBlocked(busyAi, busyBlocker.Id);
        Assert.False(busyAi.TryCrowdArrive(navigation));
    }

    [Fact]
    public void BothOptionsOffDisableCrowdArrival()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.CrowdArrivalRadius = 60.0f;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        var arrived = NewRegisteredUnit(World(4, 2), ObjectKinds.Vehicle, game, owner);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(3, 2));
        DriveBlocked(ai, arrived.Id);
        Assert.False(ai.TryCrowdArrive(navigation));
        Assert.Single(ai.TargetPoints);
    }

    private static void DriveBlocked(AIUpdate ai, ObjectId blockerId)
    {
        // First step establishes the baseline, the next two trip the wait threshold.
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
    }

    private GameObject NewUnit(Vector3 position, ObjectKinds kind = ObjectKinds.Infantry, TestGame? game = null, Player? player = null)
    {
        var host = game ?? Generals;
        var gameObject = new GameObject(BuildDefinition(kind), host.GameEngine, player ?? NewPlayer())
        {
            // Manual ids start high so they never collide with the game's counter.
            Id = new ObjectId(_nextManualObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    // Blockers must be registered: crowd arrival resolves them through the object collection.
    private GameObject NewRegisteredUnit(Vector3 position, ObjectKinds kind, TestGame game, Player player)
    {
        var gameObject = game.GameLogic.CreateObject(BuildDefinition(kind), player);
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private ObjectDefinition BuildDefinition(ObjectKinds kind)
    {
        var definition = new ObjectDefinition($"CrowdArrival_{kind}_{_nextDefinitionOrdinal++}");
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

    private Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextManualObjectId = 100;
    private int _nextDefinitionOrdinal;
}
