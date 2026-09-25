using System.Runtime.CompilerServices;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.AI;

public sealed class SkirmishAIMilitaryBehaviorTests : MockedGameTest
{
    [Fact]
    public void DecisionTick_WhenForceMeetsThreshold_EmitsAttackOrderAgainstEnemyStructure()
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        var aiPlayer = manager.Players[3];
        var enemyPlayer = manager.Players[2];

        for (var i = 0; i < SkirmishAIPlayer.DefaultAttackForceThreshold; i++)
        {
            var attackerDef = new ObjectDefinition($"AITestAttacker{i}");
            attackerDef.KindOf.Set(ObjectKinds.CanAttack, true);
            var attacker = CreateFakeGameObject(
                attackerDef,
                aiPlayer,
                objectId: (uint)(2000 + i),
                isStructure: false,
                canAttack: true);
            aiPlayer.AddOwnedObject(attacker);
        }

        var enemyStructureDef = new ObjectDefinition("AITestEnemyStructure");
        enemyStructureDef.KindOf.Set(ObjectKinds.Structure, true);
        var enemyStructure = CreateFakeGameObject(
            enemyStructureDef,
            enemyPlayer,
            objectId: 3000,
            isStructure: true,
            canAttack: false);
        enemyPlayer.AddOwnedObject(enemyStructure);

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.LogicTick();
        }

        Assert.Contains(
            Generals.InjectedOrders,
            x =>
                x.PlayerIndex == 3
                && (x.OrderType == OrderType.AttackObject || x.OrderType == OrderType.ForceAttackObject)
                && x.Arguments.Count > 0
                && x.Arguments[0].Value.ObjectId == enemyStructure.Id);
    }

    [Fact]
    public void DecisionTick_WhenForceBelowThreshold_DoesNotEmitAttackOrder()
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        var aiPlayer = manager.Players[3];
        var enemyPlayer = manager.Players[2];

        for (var i = 0; i < SkirmishAIPlayer.DefaultAttackForceThreshold - 1; i++)
        {
            var attackerDef = new ObjectDefinition($"AITestSmallForce{i}");
            attackerDef.KindOf.Set(ObjectKinds.CanAttack, true);
            var attacker = CreateFakeGameObject(
                attackerDef,
                aiPlayer,
                objectId: (uint)(4000 + i),
                isStructure: false,
                canAttack: true);
            aiPlayer.AddOwnedObject(attacker);
        }

        var enemyStructureDef = new ObjectDefinition("AITestEnemyStructureThreshold");
        enemyStructureDef.KindOf.Set(ObjectKinds.Structure, true);
        var enemyStructure = CreateFakeGameObject(
            enemyStructureDef,
            enemyPlayer,
            objectId: 5000,
            isStructure: true,
            canAttack: false);
        enemyPlayer.AddOwnedObject(enemyStructure);

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.LogicTick();
        }

        Assert.DoesNotContain(
            Generals.InjectedOrders,
            x => x.PlayerIndex == 3 && (x.OrderType == OrderType.AttackObject || x.OrderType == OrderType.ForceAttackObject));
    }

    private static GameObject CreateFakeGameObject(
        ObjectDefinition definition,
        Player owner,
        uint objectId,
        bool isStructure,
        bool canAttack)
    {
        var gameObject = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));

        SetField(gameObject, "_id", new ObjectId(objectId));
        SetField(gameObject, "Definition", definition);
        SetProperty(gameObject, nameof(GameObject.IsStructure), isStructure);
        SetProperty(gameObject, nameof(GameObject.CanAttack), canAttack);
        gameObject.Owner = owner;

        return gameObject;
    }

    private static void SetField<TInstance, TValue>(TInstance instance, string fieldName, TValue value)
    {
        var field = typeof(TInstance).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetProperty<TInstance, TValue>(TInstance instance, string propertyName, TValue value)
    {
        var property = typeof(TInstance).GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static Panopticon.Data.Map.Player[] CreateMapPlayers() =>
    [
        Panopticon.Data.Map.Player.CreateNeutralPlayer(),
        Panopticon.Data.Map.Player.CreateCivilianPlayer(),
        new Panopticon.Data.Map.Player
        {
            Name = "enemyHuman",
            Faction = "FactionAmerica",
            DisplayName = "Enemy Human",
            IsHuman = true,
        },
        new Panopticon.Data.Map.Player
        {
            Name = "ai",
            Faction = "FactionGLA",
            DisplayName = "AI",
            IsHuman = false,
        },
    ];

    private static PlayerSetting[] CreatePlayerSettings() =>
    [
        new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "enemyHuman", true),
        new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), 2, PlayerOwner.EasyAi, "ai"),
    ];

    private void RegisterPlayerTemplates()
    {
        Generals.AssetStore.PlayerTemplates.Add(CreatePlayerTemplate("FactionAmerica"));
        Generals.AssetStore.PlayerTemplates.Add(CreatePlayerTemplate("FactionGLA"));
    }

    private static PlayerTemplate CreatePlayerTemplate(string name)
    {
        var template = new PlayerTemplate();
        var setNameAndInstanceId = typeof(PlayerTemplate).GetMethod(
            "SetNameAndInstanceId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        setNameAndInstanceId!.Invoke(template, new object[] { "PlayerTemplate", name });
        return template;
    }
}
