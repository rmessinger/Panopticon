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

    private static GameObject CreateGameObject(bool isStructure)
    {
        var gameObject = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));
        typeof(GameObject).GetProperty(nameof(GameObject.IsStructure))!.SetValue(gameObject, isStructure);
        return gameObject;
    }
}
