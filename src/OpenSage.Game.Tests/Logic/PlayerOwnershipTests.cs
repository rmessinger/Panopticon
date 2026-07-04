using System.Runtime.CompilerServices;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic;

public class PlayerOwnershipTests : MockedGameTest
{
    [Fact]
    public void AddOwnedObject_TracksOwnedObjectsAndCounts()
    {
        var player = new Player(1, null, default, Generals);
        var gameObject = CreateGameObject(isStructure: true);

        player.AddOwnedObject(gameObject);

        Assert.Contains(gameObject, player.OwnedObjects);
        Assert.Equal(1, player.BuildingCount);
        Assert.Equal(0, player.UnitCount);

        player.RemoveOwnedObject(gameObject);

        Assert.Empty(player.OwnedObjects);
        Assert.Equal(0, player.BuildingCount);
        Assert.Equal(0, player.UnitCount);
    }

    [Fact]
    public void OwnerReassignment_MovesObjectBetweenPlayersAndFixesCounts()
    {
        var playerA = new Player(1, null, default, Generals);
        var playerB = new Player(2, null, default, Generals);
        var gameObject = CreateGameObject(isStructure: false);

        // Simulate the creation path: GameLogic.AddObject sets Owner in the constructor (not
        // through the setter's transfer logic, since the object isn't tracked yet), then explicitly
        // registers it with the owner.
        gameObject.Owner = playerA;
        playerA.AddOwnedObject(gameObject);

        Assert.Contains(gameObject, playerA.OwnedObjects);
        Assert.Equal(1, playerA.UnitCount);

        // Reassigning Owner (e.g. capture / team change) should move the object across, not
        // double-count it on either side.
        gameObject.Owner = playerB;

        Assert.DoesNotContain(gameObject, playerA.OwnedObjects);
        Assert.Equal(0, playerA.UnitCount);
        Assert.Contains(gameObject, playerB.OwnedObjects);
        Assert.Equal(1, playerB.UnitCount);

        // Destroying the transferred object should decrement the current (new) owner's count, not
        // the original owner's.
        playerB.RemoveOwnedObject(gameObject);

        Assert.Empty(playerB.OwnedObjects);
        Assert.Equal(0, playerB.UnitCount);
        Assert.Equal(0, playerA.UnitCount);
    }

    [Fact]
    public void OwnerAssignment_AtConstructionDoesNotDoubleCount()
    {
        var player = new Player(1, null, default, Generals);
        var gameObject = CreateGameObject(isStructure: true);

        // First assignment (as happens in the GameObject constructor, before GameLogic.AddObject
        // has registered the object) must not itself call AddOwnedObject.
        gameObject.Owner = player;

        Assert.Empty(player.OwnedObjects);
        Assert.Equal(0, player.BuildingCount);

        // GameLogic.AddObject performs the one, explicit registration.
        player.AddOwnedObject(gameObject);

        Assert.Contains(gameObject, player.OwnedObjects);
        Assert.Equal(1, player.BuildingCount);
    }

    private static GameObject CreateGameObject(bool isStructure)
    {
        var gameObject = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
        typeof(GameObject).GetProperty(nameof(GameObject.IsStructure))!.SetValue(gameObject, isStructure);
        return gameObject;
    }
}
