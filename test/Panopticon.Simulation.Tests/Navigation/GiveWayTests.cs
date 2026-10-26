using System;
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
public class GiveWayTests : MockedGameTest
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
    public void MutualBlock_HigherObjectIdYieldsAndLowerWaits()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        // Registered so the resolver finds each through the object collection.
        var lower = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game, owner);
        var higher = NewRegisteredUnit(World(8, 2), ObjectKinds.Vehicle, game, owner);
        SetRadii(lower);
        SetRadii(higher);

        var lowerAi = lower.AIUpdate!;
        var higherAi = higher.AIUpdate!;
        lowerAi.TargetPoints.Add(World(8, 2));
        higherAi.TargetPoints.Add(World(5, 2));
        Assert.True(lower.Id.Index < higher.Id.Index);

        DriveBlocked(lowerAi, higher.Id);
        DriveBlocked(higherAi, lower.Id);

        // The lower id waits: nothing changes for it.
        BlockedMovementResolver.TryResolve(lower, lowerAi, navigation);
        Assert.Single(lowerAi.TargetPoints);
        Assert.Equal(World(8, 2), lowerAi.TargetPoints[0]);
        Assert.Equal(0, navigation.Statistics.BlockedRepathRequests);

        // The higher id sidesteps off the lower unit's segment while keeping its route.
        BlockedMovementResolver.TryResolve(higher, higherAi, navigation);
        Assert.Equal(2, higherAi.TargetPoints.Count);
        var sidestep = higherAi.TargetPoints[0];
        Assert.NotEqual(World(8, 2), sidestep);
        Assert.True(navigation.IsPassable(sidestep));
        // Off the lower unit's row-2 segment (clearance 6.5, a full cell is 10).
        Assert.True(MathF.Abs(sidestep.Y - 20f) >= 6.5f, $"sidestep {sidestep}");
        Assert.Equal(World(5, 2), higherAi.TargetPoints[^1]);
        Assert.True(higherAi.RecentlyGaveWay(navigation.Options.BlockedWaitFrames));
        Assert.Equal(0, navigation.Statistics.BlockedRepathRequests);
    }

    [Fact]
    public void IdleFriendlyBlockerSidestepsInsteadOfForcingARepath()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        SetRadii(unit);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game, owner);
        SetRadii(blocker);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(6, 2));
        ai.TargetPoints.Add(World(5, 2));
        ai.TargetPoints.Add(World(4, 2));
        ai.TargetPoints.Add(World(1, 2));
        DriveBlocked(ai, blocker.Id);

        var statsBefore = navigation.Statistics;
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        Assert.Equal(0, navigation.Statistics.BlockedRepathRequests - statsBefore.BlockedRepathRequests);
        Assert.Equal(4, ai.TargetPoints.Count);
        Assert.Equal(World(6, 2), ai.TargetPoints[0]);

        var blockerAi = blocker.AIUpdate!;
        Assert.Single(blockerAi.TargetPoints);
        var sidestep = blockerAi.TargetPoints[0];
        Assert.True(navigation.IsPassable(sidestep));
        Assert.True(MathF.Abs(sidestep.Y - 20f) >= 6.5f, $"sidestep {sidestep}");
    }

    [Fact]
    public void SecondRequestWithinTheWaitWindowIsHeldBack()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        SetRadii(unit);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game, owner);
        SetRadii(blocker);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(1, 2));
        DriveBlocked(ai, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        Assert.Single(blocker.AIUpdate!.TargetPoints);

        // Inside the wait window the in-flight sidestep is waited out: no second move,
        // no cascade, and no repath is spent.
        DriveBlocked(ai, blocker.Id);
        BlockedMovementResolver.TryResolve(unit, ai, navigation);
        Assert.Single(blocker.AIUpdate!.TargetPoints);
        Assert.Equal(0, navigation.Statistics.BlockedRepathRequests);
    }

    [Fact]
    public void EnemyBlockerNeverGivesWayAndFallsBackToTheLocalRepath()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();
        var enemy = new Player(2, null, default, Generals);

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        SetRadii(unit);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game, enemy);
        SetRadii(blocker);

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(1, 2));
        DriveBlocked(ai, blocker.Id);

        var statsBefore = navigation.Statistics;
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        Assert.Empty(blocker.AIUpdate!.TargetPoints);
        Assert.Equal(1, navigation.Statistics.BlockedRepathRequests - statsBefore.BlockedRepathRequests);
    }

    [Fact]
    public void BlockerWithItsOwnOrderRefusesAndTheRequesterRepaths()
    {
        var navigation = CreateNavigation();
        navigation.Options.BlockedUnitResolution = true;
        navigation.Options.BlockedWaitFrames = 2;
        navigation.Options.LocalRepathWindowCells = 4;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();

        var unit = NewUnit(World(8, 2), game: game, player: owner);
        SetRadii(unit);
        var blocker = NewRegisteredUnit(World(5, 2), ObjectKinds.Vehicle, game, owner);
        SetRadii(blocker);
        blocker.AIUpdate!.TargetPoints.Add(World(5, 12));

        var ai = unit.AIUpdate!;
        ai.TargetPoints.Add(World(1, 2));
        DriveBlocked(ai, blocker.Id);

        var statsBefore = navigation.Statistics;
        BlockedMovementResolver.TryResolve(unit, ai, navigation);

        // The busy blocker keeps its own single order; the requester detours instead.
        Assert.Single(blocker.AIUpdate!.TargetPoints);
        Assert.Equal(World(5, 12), blocker.AIUpdate!.TargetPoints[0]);
        Assert.Equal(1, navigation.Statistics.BlockedRepathRequests - statsBefore.BlockedRepathRequests);
    }

    private static void DriveBlocked(AIUpdate ai, ObjectId blockerId)
    {
        // First step establishes the baseline, the next two trip the wait threshold.
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
        ai.Progress.RecordMoveStep(100f, blockedByUnit: true, blockerId);
    }

    private static void SetRadii(GameObject gameObject)
    {
        gameObject.Geometry.Shapes[0].MajorRadius = 2;
        gameObject.Geometry.Shapes[0].MinorRadius = 2;
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

    // Blockers must be registered: the resolver resolves them through the object collection.
    private GameObject NewRegisteredUnit(Vector3 position, ObjectKinds kind, TestGame game, Player player)
    {
        var gameObject = game.GameLogic.CreateObject(BuildDefinition(kind), player);
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private ObjectDefinition BuildDefinition(ObjectKinds kind)
    {
        var definition = new ObjectDefinition($"GiveWay_{kind}_{_nextDefinitionOrdinal++}");
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
