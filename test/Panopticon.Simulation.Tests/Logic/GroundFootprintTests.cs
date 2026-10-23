using System;
using System.Collections.Generic;
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

    [Fact]
    public void MovingObstacleDropsOldBucketsAndKeepsItsLatestPosition()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var follower = NewUnit(1, new Vector3(10, 10, 0));
        var obstacle = NewUnit(2, new Vector3(20, 10, 0));
        manager.OnObjectAdded(follower);
        manager.OnObjectAdded(obstacle);
        manager.Update();

        obstacle.UpdateTransform(new Vector3(100, 10, 0));
        Assert.True(manager.GroundFootprintFits(follower, new Vector2(30, 10)));
        Assert.Equal(1, manager.LastGroundFootprintCandidateCount);

        obstacle.UpdateTransform(new Vector3(21, 10, 0));
        obstacle.UpdateTransform(new Vector3(22, 10, 0));
        Assert.False(manager.GroundFootprintFits(follower, new Vector2(30, 10)));
        Assert.Equal(2, manager.LastGroundFootprintCandidateCount);
    }

    [Fact]
    public void RemovedObstacleDoesNotBlockOrReappearOnMovement()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var follower = NewUnit(1, new Vector3(10, 10, 0));
        var obstacle = NewUnit(2, new Vector3(20, 10, 0));
        manager.OnObjectAdded(follower);
        manager.OnObjectAdded(obstacle);
        manager.Update();

        obstacle.PartitionObject.Remove();
        obstacle.UpdateTransform(new Vector3(21, 10, 0));
        Assert.True(manager.GroundFootprintFits(follower, new Vector2(30, 10)));
        Assert.Equal(1, manager.LastGroundFootprintCandidateCount);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(230)]
    public void IndexCoversSpawnsOutsideRegularPartitionBounds(float x)
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(x, 10, 0));
        var second = NewUnit(2, new Vector3(x + 10, 10, 0));
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);

        Assert.False(manager.GroundFootprintFits(first, new Vector2(x + 20, 10)));
    }

    [Fact]
    public void MultipleCellMembershipsProduceOneCandidatePerObject()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(50, 50, 0));
        var second = NewUnit(2, new Vector3(80, 50, 0));
        first.Geometry.Shapes[0].MajorRadius = 20;
        second.Geometry.Shapes[0].MajorRadius = 20;
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);

        Assert.False(manager.GroundFootprintFits(first, new Vector2(60, 50)));
        Assert.Equal(2, manager.LastGroundFootprintCandidateCount);
    }

    [Fact]
    public void EnlargedFootprintRefreshesCoverageWhenObjectIsMarkedDirty()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(10, 10, 0));
        var second = NewUnit(2, new Vector3(50, 10, 0));
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);
        manager.Update();

        second.Geometry.Shapes[0].MajorRadius = 30;
        second.PartitionObject.SetDirty();
        Assert.False(manager.GroundFootprintFits(first, new Vector2(20, 10)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    public void DistantDirtyObjectsDoNotIncreaseLocalCandidateCount(int distantCount)
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(10, 10, 0));
        manager.OnObjectAdded(first);
        for (var i = 0; i < distantCount; i++)
        {
            var other = NewUnit((uint)i + 2, new Vector3(1000 + i * 20, 1000, 0));
            manager.OnObjectAdded(other);
            other.UpdateTransform(other.Translation + Vector3.UnitX);
        }

        Assert.True(manager.GroundFootprintFits(first, new Vector2(15, 10)));
        Assert.Equal(1, manager.LastGroundFootprintCandidateCount);
    }

    [Fact]
    public void LiveIndexMatchesBruteForceAfterMovesSpawnsResizesAndRemovals()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var units = new List<GameObject>();
        var random = new Random(17);
        uint id = 1;
        for (var i = 0; i < 60; i++)
        {
            Spawn();
        }

        for (var step = 0; step < 150; step++)
        {
            var moving = units[random.Next(units.Count)];
            moving.UpdateTransform(Position());
            if (step % 7 == 0)
            {
                moving.Geometry.Shapes[0].MajorRadius = random.Next(1, 25);
                moving.PartitionObject.SetDirty();
            }

            if (step % 11 == 0)
            {
                Spawn();
            }

            if (step % 13 == 0)
            {
                units[^1].PartitionObject.Remove();
                units.RemoveAt(units.Count - 1);
            }

            if (step % 17 == 0)
            {
                manager.Update();
            }

            for (var query = 0; query < 3; query++)
            {
                var unit = units[random.Next(units.Count)];
                var start = new Vector2(unit.Translation.X, unit.Translation.Y);
                var end = start + new Vector2(random.Next(-80, 81), random.Next(-80, 81));
                var expected = true;
                foreach (var other in units)
                {
                    if (other != unit && GroundFootprint.BlocksAdvance(start, end,
                        new Vector2(other.Translation.X, other.Translation.Y),
                        unit.Geometry.BoundingCircleRadius + other.Geometry.BoundingCircleRadius + 2.5f))
                    {
                        expected = false;
                        break;
                    }
                }

                Assert.Equal(expected, manager.GroundFootprintFits(unit, end));
            }
        }

        Vector3 Position() => new(random.Next(-200, 600), random.Next(-200, 600), 0);

        void Spawn()
        {
            var unit = NewUnit(id++, Position());
            manager.OnObjectAdded(unit);
            units.Add(unit);
        }
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
