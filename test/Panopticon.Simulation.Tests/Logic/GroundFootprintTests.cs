using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic;

[CollectionDefinition("GroundFootprint", DisableParallelization = true)]
public sealed class GroundFootprintCollection;

[Collection("GroundFootprint")]
public class GroundFootprintTests : MockedGameTest
{
    [Theory]
    [InlineData(0, 20, 10, true)]
    [InlineData(0, 5, 10, false)]
    [InlineData(0, 8, 10, true)]
    [InlineData(8, 7, 10, false)]
    [InlineData(8, 9, 10, true)]
    public void SweptStepBlocksContactButAllowsEscape(float start, float end, float obstacle, bool blocked)
    {
        Assert.Equal(blocked, GroundFootprint.BlocksAdvance(new Vector2(start, 0),
            new Vector2(end, 0), new Vector2(obstacle, 0), 3));
    }

    [Fact]
    public void FollowingUnitQueuesAndResumesWhenLeaderMoves()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var follower = NewUnit(1, new Vector3(10, 10, 0));
        var leader = NewUnit(2, new Vector3(30, 10, 0));
        manager.OnObjectAdded(follower);
        manager.OnObjectAdded(leader);
        manager.Update();

        for (var frame = 0; frame < 40; frame++)
        {
            var next = new Vector2(follower.Translation.X + 1, 10);
            if (manager.GroundFootprintFits(follower, next))
            {
                follower.UpdateTransform(new Vector3(next, 0));
            }

            Assert.True(Vector3.Distance(follower.Translation, leader.Translation) >= 6.5f);
        }

        Assert.False(manager.GroundFootprintFits(follower, new Vector2(follower.Translation.X + 1, 10)));
        leader.UpdateTransform(new Vector3(50, 10, 0));
        Assert.True(manager.GroundFootprintFits(follower, new Vector2(follower.Translation.X + 1, 10)));
    }

    [Fact]
    public void CrossingTrafficSeesMovedObjectBeforeMembershipRebuild()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(10, 50, 0));
        var second = NewUnit(2, new Vector3(50, 10, 0));
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);
        manager.Update();

        first.UpdateTransform(new Vector3(50, 50, 0));
        Assert.False(manager.GroundFootprintFits(second, new Vector2(50, 90)));
    }

    [Fact]
    public void NewSpawnBlocksBeforeFirstPartitionUpdate()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(10, 10, 0));
        var second = NewUnit(2, new Vector3(20, 10, 0));
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);
        Assert.False(manager.GroundFootprintFits(first, new Vector2(30, 10)));
    }

    private GameObject NewUnit(uint id, Vector3 position)
    {
        var definition = new ObjectDefinition($"GroundFootprint_{id}");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.Behaviors.Add("Body", new ModuleDataContainer("Body",
            new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
            ModuleInheritanceMode.Default, inherited: false));
        var obj = new GameObject(definition, Generals.GameEngine, new Player(1, null, default, Generals))
        {
            Id = new ObjectId(id),
        };
        obj.Geometry.Shapes[0].MajorRadius = 2;
        obj.Geometry.Shapes[0].MinorRadius = 2;
        obj.UpdateTransform(position);
        return obj;
    }
}
