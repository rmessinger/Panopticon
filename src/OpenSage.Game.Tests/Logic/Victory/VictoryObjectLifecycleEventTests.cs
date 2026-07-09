using System.Collections.Generic;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic.Victory;

public sealed class VictoryObjectLifecycleEventTests : MockedGameTest
{
    [Fact]
    public void OnDie_RaisesLifecycleEventOnce()
    {
        var player = new Player(1, null, default, Generals);
        var gameObject = CreateGameObject(player);
        var events = CaptureLifecycleEvents();

        gameObject.OnDie(new DamageInfoInput());
        gameObject.OnDie(new DamageInfoInput());

        var lifecycleEvent = Assert.Single(events);
        Assert.Same(gameObject, lifecycleEvent.GameObject);
        Assert.Same(player, lifecycleEvent.PreviousOwner);
        Assert.Same(player, lifecycleEvent.CurrentOwner);
        Assert.Equal(GameObjectLifecycleEventReason.Died, lifecycleEvent.Reason);
    }

    [Fact]
    public void Destroy_RaisesLifecycleEventOnce()
    {
        var player = new Player(1, null, default, Generals);
        var gameObject = CreateGameObject(player);
        var events = CaptureLifecycleEvents();

        gameObject.Destroy();
        gameObject.Destroy();

        var lifecycleEvent = Assert.Single(events);
        Assert.Same(gameObject, lifecycleEvent.GameObject);
        Assert.Same(player, lifecycleEvent.PreviousOwner);
        Assert.Same(player, lifecycleEvent.CurrentOwner);
        Assert.Equal(GameObjectLifecycleEventReason.Destroyed, lifecycleEvent.Reason);
    }

    [Fact]
    public void OwnerChange_RaisesLifecycleEventOnceAndUpdatesCounts()
    {
        var playerA = new Player(1, null, default, Generals);
        var playerB = new Player(2, null, default, Generals);
        var gameObject = CreateGameObject(playerA);
        playerA.AddOwnedObject(gameObject);
        var events = CaptureLifecycleEvents();

        gameObject.Owner = playerB;
        gameObject.Owner = playerB;

        var lifecycleEvent = Assert.Single(events);
        Assert.Same(gameObject, lifecycleEvent.GameObject);
        Assert.Same(playerA, lifecycleEvent.PreviousOwner);
        Assert.Same(playerB, lifecycleEvent.CurrentOwner);
        Assert.Equal(GameObjectLifecycleEventReason.OwnerChanged, lifecycleEvent.Reason);
        Assert.DoesNotContain(gameObject, playerA.OwnedObjects);
        Assert.Contains(gameObject, playerB.OwnedObjects);
        Assert.Equal(0, playerA.UnitCount);
        Assert.Equal(1, playerB.UnitCount);
    }

    [Fact]
    public void VictorySystem_QueuesAffectedPlayersFromLifecycleEvents()
    {
        var playerA = new Player(1, null, default, Generals);
        var playerB = new Player(2, null, default, Generals);
        var gameObject = CreateGameObject(playerA);
        playerA.AddOwnedObject(gameObject);

        gameObject.Owner = playerB;

        Assert.Equal(1, Generals.VictorySystem.LifecycleNotificationCount);
        Assert.Equal(2, Generals.VictorySystem.PendingPlayerRecheckCount);

        for (var i = 0u; i < VictorySystem.DefaultCheckCadence.Value; i++)
        {
            Generals.GameLogic.Update();
        }

        Assert.Equal(2, Generals.VictorySystem.LastProcessedPlayerCount);
        Assert.Equal(0, Generals.VictorySystem.PendingPlayerRecheckCount);
    }

    private List<GameObjectLifecycleEventArgs> CaptureLifecycleEvents()
    {
        var result = new List<GameObjectLifecycleEventArgs>();
        Generals.GameLogic.ObjectLifecycleChanged += (_, args) => result.Add(args);
        return result;
    }

    private GameObject CreateGameObject(Player owner)
    {
        return new GameObject(new ObjectDefinition("VictoryLifecycleTestObject"), Generals.GameEngine, owner);
    }
}
