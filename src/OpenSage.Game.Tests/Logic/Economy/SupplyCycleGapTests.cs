// P4-4 supply/worker/dozer cycle gap closure. Each cycle is driven through its
// production code path with the movement-dependent transitions bypassed by
// white-box state injection (the mocked host has no pathfinder, locomotor, or
// GameClient/Drawable wiring). The state-machine transitions that DO run are
// the ones the card cares about: build-advance + completion-clear for the
// builder cycle, and the dump transition + DumpBoxes credit for the supply
// gather->dump cycle. Stuck-state guards fail loudly if the state fails to
// advance within a bounded number of ticks.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P4-4.
//
// Run with: dotnet test src --filter "FullyQualifiedName~SupplyCycleGapTests"

using System;
using System.Collections.Generic;
using System.Reflection;
using OpenSage.Audio;
using OpenSage.Client;
using OpenSage.Content;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Economy;

public class SupplyCycleGapTests : MockedGameTest
{
    private const uint PlayerIdx = 2;
    private const int MaxTicksBeforeStall = 64;

    [Fact, Trait("Category", "Roadmap")]
    public void Dozer_BuilderCycle_AdvancesAndClearsOnCompletion()
    {
        var player = CreateSkirmishPlayer();
        var dozer = CreateDozer(player);
        var target = CreateStructure(player, buildTimeFrames: 5);

        // Bypass SetBuildTarget's pathfinder-dependent SetTargetPoint and inject the
        // build target straight into _state._dozerTargets[0]. Mark the dozer as
        // ActivelyConstructing and the target as ActivelyBeingConstructed so the
        // UpdateBuildTarget branch takes the else-if and calls AdvanceConstruction.
        InjectBuildTarget(dozer, dozer.AIUpdate!, target);
        dozer.ModelConditionFlags.Set(ModelConditionFlag.ActivelyConstructing, true);
        target.ModelConditionFlags.Set(ModelConditionFlag.ActivelyBeingConstructed, true);

        var states = StateTickHistory();
        for (var i = 0; i < MaxTicksBeforeStall; i++)
        {
            DozerState(dozer.AIUpdate!).Update();
            states.Add((BuildTargetId(dozer.AIUpdate!), target.BuildProgress));

            if (BuildTargetId(dozer.AIUpdate!) == ObjectId.Invalid)
            {
                break;
            }
        }

        Assert.True(target.BuildProgress >= 1.0f,
            $"dozer build cycle stalled: BuildProgress={target.BuildProgress}, trace=[{string.Join(", ", states)}]");
        Assert.Equal(ObjectId.Invalid, BuildTargetId(dozer.AIUpdate!));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Worker_BuilderCycle_AdvancesAndClearsOnCompletion()
    {
        var player = CreateSkirmishPlayer();
        var worker = CreateWorker(player);
        var target = CreateStructure(player, buildTimeFrames: 5);

        InjectBuildTarget(worker, worker.AIUpdate!, target);
        worker.ModelConditionFlags.Set(ModelConditionFlag.ActivelyConstructing, true);
        target.ModelConditionFlags.Set(ModelConditionFlag.ActivelyBeingConstructed, true);

        var states = StateTickHistory();
        for (var i = 0; i < MaxTicksBeforeStall; i++)
        {
            WorkerBuilderState(worker.AIUpdate!).Update();
            states.Add((BuildTargetId(worker.AIUpdate!), target.BuildProgress));

            if (BuildTargetId(worker.AIUpdate!) == ObjectId.Invalid)
            {
                break;
            }
        }

        Assert.True(target.BuildProgress >= 1.0f,
            $"worker build cycle stalled: BuildProgress={target.BuildProgress}, trace=[{string.Join(", ", states)}]");
        Assert.Equal(ObjectId.Invalid, BuildTargetId(worker.AIUpdate!));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SupplyTruck_DumpTransition_CreditsOwnerAndReachesFinished()
    {
        var player = CreateSkirmishPlayer(money: 0);
        player.BankAccount.Money = 0u;
        var supplyCenter = CreateSupplyCenter(player);
        SetValuePerSupplyBox(100);

        var truck = CreateSupplyTruck(player);
        var dockUpdate = supplyCenter.FindBehavior<SupplyCenterDockUpdate>()!;

        // Park the state machine at DumpingSupplies with _waitUntil already expired,
        // _numBoxes pinned, and the dock update bound. TickSupplyState must transition
        // to FinishedDumpingSupplies and call DumpBoxes, crediting the owner.
        SetSupplyState(truck.AIUpdate!, SupplyAIUpdate.SupplyGatherStates.DumpingSupplies);
        SetField(truck.AIUpdate!, "_numBoxes", 3);
        SetField(truck.AIUpdate!, "_currentTargetDockUpdate", dockUpdate);
        // Wait until "now" so the DumpingSupplies case fires immediately.
        SetField(truck.AIUpdate!, "_waitUntil", Generals.GameLogic.CurrentFrame);

        var supplyUpdate = (SupplyAIUpdate)truck.AIUpdate!;
        supplyUpdate.TickSupplyState();

        Assert.Equal(SupplyAIUpdate.SupplyGatherStates.FinishedDumpingSupplies, supplyUpdate.SupplyGatherState);
        Assert.Equal(300u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Worker_DumpTransition_CreditsOwnerAndReachesFinished()
    {
        var player = CreateSkirmishPlayer(money: 0);
        var supplyCenter = CreateSupplyCenter(player);
        SetValuePerSupplyBox(50);

        var worker = CreateWorker(player);
        var dockUpdate = supplyCenter.FindBehavior<SupplyCenterDockUpdate>()!;

        SetSupplyState(worker.AIUpdate!, SupplyAIUpdate.SupplyGatherStates.DumpingSupplies);
        SetField(worker.AIUpdate!, "_numBoxes", 4);
        SetField(worker.AIUpdate!, "_currentTargetDockUpdate", dockUpdate);
        SetField(worker.AIUpdate!, "_waitUntil", Generals.GameLogic.CurrentFrame);

        var supplyUpdate = (SupplyAIUpdate)worker.AIUpdate!;
        supplyUpdate.TickSupplyState();

        Assert.Equal(SupplyAIUpdate.SupplyGatherStates.FinishedDumpingSupplies, supplyUpdate.SupplyGatherState);
        Assert.Equal(200u, player.BankAccount.Money);
    }

    // ---------- helpers ----------

    private Player CreateSkirmishPlayer(uint money = 1_000u)
    {
        Generals.PlayerManager.OnNewGame(
            [
                OpenSage.Data.Map.Player.CreateNeutralPlayer(),
                OpenSage.Data.Map.Player.CreateCivilianPlayer(),
                new OpenSage.Data.Map.Player
                {
                    Name = "human",
                    Faction = "FactionAmerica",
                    DisplayName = "Human",
                    IsHuman = true,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "human", true),
            ]);
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = money;
        return player;
    }

    private GameObject CreateDozer(Player owner)
    {
        var definition = new ObjectDefinition("P4CycleDozer");
        definition.KindOf.Set(ObjectKinds.Dozer, true);
        definition.Behaviors.Add("ModuleTag_DozerAI",
            new ModuleDataContainer("ModuleTag_DozerAI", new DozerAIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        AttachStubVoiceTaskComplete(definition);
        return InstantiateWithDrawable(definition, owner);
    }

    private GameObject CreateWorker(Player owner)
    {
        var definition = new ObjectDefinition("P4CycleWorker");
        definition.KindOf.Set(ObjectKinds.Dozer, true);
        definition.KindOf.Set(ObjectKinds.Harvester, true);
        definition.Behaviors.Add("ModuleTag_WorkerAI",
            new ModuleDataContainer("ModuleTag_WorkerAI", new WorkerAIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        AttachStubVoiceTaskComplete(definition);
        return InstantiateWithDrawable(definition, owner);
    }

    // DozerAndWorkerState.UpdateBuildTarget fires VoiceTaskComplete on completion; the
    // reference is null by default and would NRE before PlayAudioEvent can short-circuit
    // on _noAudio=true. Pin it to a non-null stub reference so the production code's eager
    // argument evaluation succeeds.
    private static void AttachStubVoiceTaskComplete(ObjectDefinition definition)
    {
        var stub = new LazyAssetReference<BaseAudioEventInfo>(new AudioEvent());
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.VoiceTaskComplete))!
            .SetValue(definition, stub);
    }

    private GameObject CreateSupplyTruck(Player owner)
    {
        var definition = new ObjectDefinition("P4CycleSupplyTruck");
        definition.KindOf.Set(ObjectKinds.Harvester, true);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.Behaviors.Add("ModuleTag_SupplyTruckAI",
            new ModuleDataContainer("ModuleTag_SupplyTruckAI", new SupplyTruckAIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        return InstantiateWithDrawable(definition, owner);
    }

    private GameObject CreateStructure(Player owner, int buildTimeFrames)
    {
        var definition = new ObjectDefinition($"P4CycleStructure_{owner.OwnedObjects.Count}");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildTime))!
            .SetValue(definition, new LogicFrameSpan((uint)buildTimeFrames));
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { InitialHealth = 100f, MaxHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = InstantiateWithDrawable(definition, owner);
        owner.AddOwnedObject(gameObject);
        // Register in GameLogic so DozerAndWorkerState.TryGetBuildTarget can resolve by id.
        gameObject.Id = new ObjectId((uint)owner.OwnedObjects.Count + 200);
        return gameObject;
    }

    private GameObject CreateSupplyCenter(Player owner)
    {
        var definition = new ObjectDefinition($"P4CycleSupplyCenter_{owner.OwnedObjects.Count}");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        definition.KindOf.Set(ObjectKinds.SupplyGatheringCenter, true);
        definition.Behaviors.Add("ModuleTag_DockUpdate",
            new ModuleDataContainer("ModuleTag_DockUpdate", new SupplyCenterDockUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = InstantiateWithDrawable(definition, owner);
        owner.AddOwnedObject(gameObject);
        owner.SupplyManager.AddSupplyCenter(gameObject);
        gameObject.Id = new ObjectId((uint)owner.OwnedObjects.Count + 100);
        return gameObject;
    }

    private GameObject InstantiateWithDrawable(ObjectDefinition definition, Player owner)
    {
        return new GameObject(definition, Generals.GameEngine, owner);
    }

    private static void InjectBuildTarget(GameObject builder, BehaviorModule aiUpdate, GameObject target)
    {
        var stateField = aiUpdate.GetType().GetField("_state",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var state = stateField!.GetValue(aiUpdate);
        var targetsField = state!.GetType().GetField("_dozerTargets",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var targets = (Array)targetsField!.GetValue(state)!;
        // DozerTarget is a private struct; reflect to set its public ObjectId/OrderFrame fields.
        var dozerTarget = targets.GetValue(0)!;
        dozerTarget.GetType().GetField("ObjectId")!.SetValue(dozerTarget, target.Id);
        dozerTarget.GetType().GetField("OrderFrame")!.SetValue(dozerTarget, 0u);
        targets.SetValue(dozerTarget, 0);

        // Mark target as built-by this builder, matching DozerAndWorkerState.SetBuildTarget.
        target.CreatedByObjectID = target.BuiltByObjectID = builder.Id;
    }

    private static dynamic DozerState(BehaviorModule aiUpdate)
    {
        return aiUpdate.GetType().GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(aiUpdate)!;
    }

    private static dynamic WorkerBuilderState(BehaviorModule aiUpdate) => DozerState(aiUpdate);

    private static void SetSupplyState(BehaviorModule aiUpdate, SupplyAIUpdate.SupplyGatherStates state)
    {
        var field = aiUpdate.GetType().GetField(nameof(SupplyAIUpdate.SupplyGatherState),
            BindingFlags.Instance | BindingFlags.Public);
        field!.SetValue(aiUpdate, state);
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        // Walk the type hierarchy: GetField does not return private fields declared in
        // base types (SupplyAIUpdate vs WorkerAIUpdate/SupplyTruckAIUpdate) without help.
        for (Type? t = instance.GetType(); t != null; t = t.BaseType)
        {
            var field = t.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found on {instance.GetType()}");
    }

    private void SetValuePerSupplyBox(int value)
    {
        typeof(GameData)
            .GetProperty(nameof(GameData.ValuePerSupplyBox))!
            .SetValue(Generals.AssetStore.GameData.Current, value);
    }

    private static ObjectId BuildTargetId(BehaviorModule aiUpdate)
    {
        var state = aiUpdate.GetType().GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(aiUpdate);
        var buildTargetProp = state!.GetType().GetProperty("BuildTarget");
        var buildTarget = buildTargetProp!.GetValue(state);
        return buildTarget == null ? ObjectId.Invalid : ((GameObject)buildTarget).Id;
    }

    private static List<(ObjectId TargetId, float BuildProgress)> StateTickHistory() => new();
}
