// WI-N9-01: destination slots behind NavigationOptions.DestinationSlots.
// Pins the outward ring packing (unique, passable, in-region, deterministic by distance then
// object id), the shared-point fallback past the bound, and the MoveUnits spread.
//
// Run with: dotnet test src --filter "FullyQualifiedName~DestinationSlotsTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Navigation;
using Panopticon.Terrain;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;
using Player = Panopticon.Logic.Player;

namespace Panopticon.Tests.Navigation;

// Serialized with the other Navigation classes: the MoveUnits tests assert deltas of the
// process-global Navigation.PathRequestCount.
[Collection("Navigation")]
public class DestinationSlotsTests : MockedGameTest
{
    private NavigationClass CreateNavigation()
    {
        var blendTileData = BlendTileData.Create(16, 16);
        var heightMapData = HeightMapData.Create(0, new ushort[16, 16]);
        var heightMap = new HeightMap(heightMapData);
        return new NavigationClass(blendTileData, heightMap);
    }

    private static Vector3 World(int x, int y) => new(x * 10f, y * 10f, 0f);

    [Fact]
    public void Assign_PacksUniquePassableSlotsOutward()
    {
        var navigation = CreateNavigation();
        var units = NewUnits(6);

        var slots = DestinationSlots.Assign(navigation, World(8, 8), units, maxCells: 6);

        Assert.Equal(6, slots.Length);
        Assert.All(slots, slot => Assert.True(navigation.IsPassable(slot)));
        // Ring 0 is the destination itself; every later slot is strictly farther out.
        Assert.Equal(World(8, 8), slots[0]);
        var distances = slots.Select(slot => Vector3.Distance(slot, World(8, 8))).ToList();
        for (var i = 1; i < distances.Count; i++)
        {
            Assert.True(distances[i] >= distances[i - 1] - 0.001f, $"slot {i} closer than {i - 1}");
        }

        var distinct = slots.Select(slot => navigation.GetNodeAt(slot)).ToHashSet();
        Assert.Equal(units.Count, distinct.Count);
    }

    [Fact]
    public void Assign_FallsBackToTheSharedPointPastTheBound()
    {
        var navigation = CreateNavigation();
        var units = NewUnits(4);
        var destination = World(8, 8);

        // Zero bound disables slotting entirely.
        var disabled = DestinationSlots.Assign(navigation, destination, units, maxCells: 0);
        Assert.All(disabled, slot => Assert.Equal(destination, slot));

        // A bound that admits only ring 0 leaves the rest on the shared point.
        var tight = DestinationSlots.Assign(navigation, destination, units, maxCells: 1);
        Assert.Equal(destination, tight[0]);
        Assert.All(tight.Skip(1), slot => Assert.Equal(destination, slot));
    }

    [Fact]
    public void Assign_IsDeterministicRegardlessOfInputOrder()
    {
        var navigation = CreateNavigation();
        var units = NewUnits(5);

        var forward = DestinationSlots.Assign(navigation, World(8, 8), units, maxCells: 8);
        var reversedInput = Enumerable.Reverse(units).ToList();
        var reversed = DestinationSlots.Assign(navigation, World(8, 8), reversedInput, maxCells: 8);

        var forwardById = units.Zip(forward).ToDictionary(pair => pair.First.Id, pair => pair.Second);
        foreach (var (unit, slot) in reversedInput.Zip(reversed))
        {
            Assert.Equal(forwardById[unit.Id], slot);
        }
    }

    [Fact]
    public void MoveUnits_WithSlotsSpreadsSquadlessSelections()
    {
        var navigation = CreateNavigation();
        navigation.Options.DestinationSlots = true;
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();
        var units = NewUnits(4, game, owner);
        var target = World(8, 8);

        OrderProcessor.MoveUnits(navigation, owner, units, target);

        var finals = units.Select(unit => unit.AIUpdate!.TargetPoints[^1]).ToList();
        Assert.All(finals, final => Assert.True(navigation.IsPassable(final)));
        Assert.Equal(units.Count, finals.ToHashSet().Count);
        // The lowest id rides ring 0: the order point itself.
        Assert.Equal(target, units.OrderBy(unit => unit.Id.Index).First().AIUpdate!.TargetPoints[^1]);
    }

    [Fact]
    public void MoveUnits_WithoutSlotsKeepsTheSharedPoint()
    {
        var navigation = CreateNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var owner = NewPlayer();
        var units = NewUnits(4, game, owner);
        var target = World(8, 8);

        OrderProcessor.MoveUnits(navigation, owner, units, target);

        // Option off changes nothing: every unit holds the one shared destination.
        Assert.All(units, unit => Assert.Equal(target, unit.AIUpdate!.TargetPoints[^1]));
    }

    private List<GameObject> NewUnits(int count, TestGame? game = null, Player? owner = null)
    {
        var units = new List<GameObject>(count);
        for (var i = 0; i < count; i++)
        {
            units.Add(NewUnit(World(2 + i, 2), game, owner));
        }

        return units;
    }

    private GameObject NewUnit(Vector3 position, TestGame? game = null, Player? owner = null)
    {
        var host = game ?? Generals;
        var definition = new ObjectDefinition($"DestinationSlots_Vehicle_{_nextObjectId}");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, host.GameEngine, owner ?? NewPlayer())
        {
            // Manual ids start high so they never collide with the game's counter.
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.Geometry.Shapes[0].MajorRadius = 2;
        gameObject.Geometry.Shapes[0].MinorRadius = 2;
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 100;
}
