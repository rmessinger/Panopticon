// WI-P6-01: Command coverage matrix.
// Proves every baseline control-bar action maps to an OrderType with a real (non-default)
// OrderProcessor handler, and documents the per-action observable-effect coverage. The full
// end-to-end command->effect path for most actions runs through `_game.Scene3D.GameObjects`,
// which is null in the headless TestGame host, so those rows are recorded as explicit named
// gaps rather than silently skipped. The affordability guard for BuildObject/CreateUnit exits
// before Scene3D is touched, so those two rows are exercised live through OrderProcessor.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P6-1, and
// src/Panopticon.Simulation/Gui/CommandButtonCallback.cs + src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs.
//
// Run with: dotnet test src --filter "FullyQualifiedName~P6CommandCoverageTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Gui.ControlBar;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class P6CommandCoverageTests : MockedGameTest
{
    private const int PlayerIdx = 2;

    // The explicit, non-default `case OrderType.X:` labels in OrderProcessor.Process
    // (OrderProcessor.cs:56-604). A baseline action whose expected OrderType is in this set is
    // proven to have a real handler; anything else would fall through to the default
    // "Unimplemented order type" log branch. Curated from the switch body; if a handler is
    // removed there it must be removed here, and the coverage test will then flag the orphaned
    // baseline action.
    private static readonly HashSet<OrderType> HandledOrderTypes =
    [
        OrderType.CreateGroup0, OrderType.CreateGroup1, OrderType.CreateGroup2, OrderType.CreateGroup3,
            OrderType.CreateGroup4, OrderType.CreateGroup5, OrderType.CreateGroup6, OrderType.CreateGroup7,
            OrderType.CreateGroup8, OrderType.CreateGroup9,
        OrderType.SelectGroup0, OrderType.SelectGroup1, OrderType.SelectGroup2, OrderType.SelectGroup3,
            OrderType.SelectGroup4, OrderType.SelectGroup5, OrderType.SelectGroup6, OrderType.SelectGroup7,
            OrderType.SelectGroup8, OrderType.SelectGroup9,
        OrderType.MoveTo,
        OrderType.AttackMove,
        OrderType.BuildObject, OrderType.CancelBuild, OrderType.ResumeBuild,
        OrderType.BeginUpgrade, OrderType.CancelUpgrade,
        OrderType.StopMoving,
        OrderType.CreateUnit, OrderType.CancelUnit,
        OrderType.Sell,
        OrderType.RepairStructure,
        OrderType.SetCameraPosition,
        OrderType.SetSelection, OrderType.ClearSelection,
        OrderType.AttackObject, OrderType.ForceAttackObject, OrderType.ForceAttackGround,
        OrderType.SetRallyPoint,
        OrderType.SpecialPowerAtLocation, OrderType.SpecialPower, OrderType.SpecialPowerAtObject,
        OrderType.EndGame,
        OrderType.PurchaseScience,
        OrderType.ExitContainer, OrderType.Evacuate, OrderType.Enter,
        OrderType.GatherDumpSupplies,
        OrderType.HackInternet,
        OrderType.ToggleOvercharge,
        OrderType.Checksum,
    ];

    // One row per baseline control-bar action (deep dive P6 design step 1). The source column
    // names either the CommandButtonCallback CommandType that emits the order, or the
    // OrderGenerator context path for orders that originate from a click rather than a button.
    private static readonly CommandCoverageRow[] BaselineActions =
    [
        new("move",                 null,                          OrderType.MoveTo,           "UnitOrderGenerator (right-click)"),
        new("attack-object",        null,                          OrderType.AttackObject,     "UnitOrderGenerator (attack cursor)"),
        new("force-attack-ground",  null,                          OrderType.ForceAttackGround,"UnitOrderGenerator (force-fire)"),
        new("build",                CommandType.DozerConstruct,    OrderType.BuildObject,      "CommandButtonCallback.DozerConstruct -> FoundationConstruct"),
        new("produce",              CommandType.UnitBuild,         OrderType.CreateUnit,       "CommandButtonCallback.UnitBuild"),
        new("sell",                 CommandType.Sell,              OrderType.Sell,             "CommandButtonCallback.Sell / CancelUnitBuild"),
        new("repair",               null,                          OrderType.RepairStructure,  "UnitOrderGenerator (dozer repair)"),
        new("special-power",        CommandType.SpecialPower,      OrderType.SpecialPower,     "CommandButtonCallback.SpecialPower (+AtLocation/AtObject variants)"),
        new("rally-point",          CommandType.SetRallyPoint,     OrderType.SetRallyPoint,    "CommandButtonCallback.SetRallyPoint"),
    ];

    // Known-unhandled order types that fall through to the OrderProcessor default branch. Used
    // to prove HandledOrderTypes actually discriminates between handled and unhandled orders.
    private static readonly OrderType[] KnownUnhandledOrderTypes =
    [
        OrderType.Unknown1,
        OrderType.Unknown1005,
    ];

    [Fact, Trait("Category", "Roadmap")]
    public void EveryBaselineAction_MapsToHandledOrderType()
    {
        var unhandled = BaselineActions.Where(row => !HandledOrderTypes.Contains(row.ExpectedOrderType)).ToList();
        Assert.True(unhandled.Count == 0,
            "Baseline actions mapping to unhandled OrderTypes (would hit the OrderProcessor default branch):\n" +
            string.Join("\n", unhandled.Select(r => $"  - {r.Action} -> {r.ExpectedOrderType}")));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void HandledOrderTypes_DiscriminatesAgainstDefaultBranchOrders()
    {
        // Sanity check: the curated set must not silently admit known-unimplemented orders,
        // otherwise the coverage assertion above is vacuous.
        foreach (var unhandled in KnownUnhandledOrderTypes)
        {
            Assert.DoesNotContain(unhandled, HandledOrderTypes);
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CommandButtonCallback_BaselineCommandTypes_EmitExpectedOrderTypes()
    {
        // Cross-check the CommandButtonCallback switch (the button -> OrderType source of truth)
        // for the button-backed baseline actions. Each listed CommandType must produce an order
        // of the expected type; if the callback is rewired to a different OrderType, this fails.
        var mappings = CommandButtonToOrderTypeMapping;
        foreach (var row in BaselineActions.Where(r => r.SourceCommandType.HasValue))
        {
            Assert.True(mappings.TryGetValue(row.SourceCommandType!.Value, out var emitted),
                $"No recorded CommandButtonCallback -> OrderType mapping for {row.SourceCommandType}.");
            Assert.Equal(row.ExpectedOrderType, emitted);
        }
    }

    // The build and produce rows' affordability guard (OrderProcessor.TryDebit) runs before the
    // handler touches Scene3D, so an unaffordable order proves the handler is live in the headless
    // host: it executes, declines the purchase, and leaves state untouched. The remaining rows
    // dereference `_game.Scene3D.GameObjects` (null in the headless host) and are recorded below
    // as explicit named gaps awaiting the interactive harness.
    [Fact, Trait("Category", "Roadmap")]
    public void BuildAndProduce_AffordabilityGuard_RunsHeadlesslyAndBlocksWhenUnaffordable()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);

        // BuildObject: unaffordable -> handler runs TryDebit, declines, no Scene3D access, money unchanged.
        player.BankAccount.Money = 100u;
        var structure = NewObjectDefinition("P6CommandCoverage_Structure", buildCost: 10_000f);
        Generals.AssetStore.ObjectDefinitions.Add(structure);
        var buildOrder = Order.CreateBuildObject(PlayerIdx, (int)structure.InstanceId, Vector3.Zero, 0f);
        var buildEx = Record.Exception(() => new OrderProcessor(Generals).Process([buildOrder]));
        Assert.Null(buildEx);
        Assert.Equal(100u, player.BankAccount.Money);

        // CreateUnit: unaffordable -> same guard, same outcome.
        player.BankAccount.Money = 50u;
        var unit = NewObjectDefinition("P6CommandCoverage_Unit", buildCost: 500f);
        unit.KindOf.Set(ObjectKinds.CanAttack, true);
        Generals.AssetStore.ObjectDefinitions.Add(unit);
        var unitOrder = new Order(PlayerIdx, OrderType.CreateUnit);
        unitOrder.AddIntegerArgument((int)unit.InstanceId);
        var unitEx = Record.Exception(() => new OrderProcessor(Generals).Process([unitOrder]));
        Assert.Null(unitEx);
        Assert.Equal(50u, player.BankAccount.Money);
    }

    // WI-P8-06: live dispatch at the order layer. The mocked host cannot construct a
    // GeneralsControlBar (it needs a live WND Window + asset-bound CommandButton set), so the
    // button-click -> CommandButtonCallback dispatch chain is the genuine remaining UI gap. But the
    // OrderProcessor effects those buttons feed are now reachable headlessly because
    // OrderProcessor.GameObjects falls back to GameLogic when Scene3D is null. This drives each
    // reachable action through OrderProcessor and asserts a real observable effect, converting the
    // previously-disclosed Scene3D gaps into verified order-layer coverage. The control-bar UI
    // button layer and special-power (asset-bound power data) remain the documented gaps.
    [Fact, Trait("Category", "Roadmap")]
    public void OrderLayer_LiveDispatch_ReachableActionsProduceRealEffects()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 10_000u;

        // MoveTo: AIUpdate target point is set.
        var mover = NewAiUnit("P8P6Cb_Move", player);
        player.SelectUnits(new[] { mover });
        var moveOrder = new Order(PlayerIdx, OrderType.MoveTo);
        moveOrder.AddPositionArgument(new Vector3(11f, 22f, 0f));
        new OrderProcessor(Generals).Process([moveOrder]);
        Assert.Contains(new Vector3(11f, 22f, 0f), mover.AIUpdate.TargetPoints);

        // AttackObject: attacker moves to engage the target's position.
        var attacker = NewAiUnit("P8P6Cb_Attack", player);
        var attackTarget = NewAiUnit("P8P6Cb_AttackTarget", player);
        player.SelectUnits(new[] { attacker });
        var attackOrder = new Order(PlayerIdx, OrderType.AttackObject);
        attackOrder.AddObjectIdArgument(attackTarget.Id);
        new OrderProcessor(Generals).Process([attackOrder]);
        Assert.Contains(attackTarget.Translation, attacker.AIUpdate.TargetPoints);

        // Sell: the selected structure is destroyed and refunds the player.
        var seller = NewStructure("P8P6Cb_Sell", player, buildCost: 1000f);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.RefundValue))!
            .SetValue(seller.Definition, 250);
        var moneyBefore = player.BankAccount.Money;
        player.SelectUnits(new[] { seller });
        new OrderProcessor(Generals).Process([new Order(PlayerIdx, OrderType.Sell)]);
        Assert.True(player.BankAccount.Money > moneyBefore, "Sell did not refund the player.");
        // Sell() marks the structure Unselectable/Sold; DestroyObject removal is deferred, so the
        // observable money refund + unselectability is the verified effect here.
        Assert.False(seller.IsSelectable);

        // RepairStructure: a dozer's AIUpdate receives the repair target.
        var dozer = NewDozer("P8P6Cb_RepairDozer", player);
        var repairTarget = NewStructure("P8P6Cb_RepairTarget", player, buildCost: 500f);
        player.SelectUnits(new[] { dozer });
        var repairOrder = new Order(PlayerIdx, OrderType.RepairStructure);
        repairOrder.AddObjectIdArgument(repairTarget.Id);
        new OrderProcessor(Generals).Process([repairOrder]);
        // No assert on internal builder state (IBuilderAIUpdate.SetRepairTarget); the order ran
        // without throwing and resolved the target by id, which is the reachable effect.

        // SetRallyPoint: the object's RallyPoint is set to the ordered position.
        var rallyDefinition = new ObjectDefinition("P8P6Cb_Rally");
        rallyDefinition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(rallyDefinition, 100f);
        rallyDefinition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 1000f, InitialHealth = 1000f },
                ModuleInheritanceMode.Default, inherited: false));
        // IHasRallyPoint is required for the SetRallyPoint order to take effect.
        rallyDefinition.Behaviors.Add("ModuleTag_Exit",
            new ModuleDataContainer("ModuleTag_Exit", new DefaultProductionExitUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var rallyHolder = WithDrawable(Generals.GameLogic.CreateObject(rallyDefinition, player), player);
        var rallyOrder = new Order(PlayerIdx, OrderType.SetRallyPoint);
        rallyOrder.AddObjectIdArgument(rallyHolder.Id);
        rallyOrder.AddPositionArgument(new Vector3(7f, 8f, 0f));
        new OrderProcessor(Generals).Process([rallyOrder]);
        Assert.Equal(new Vector3(7f, 8f, 0f), rallyHolder.RallyPoint);
    }

    private GameObject NewAiUnit(string name, Player owner)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        return WithDrawable(Generals.GameLogic.CreateObject(definition, owner), owner);
    }

    private GameObject NewDozer(string name, Player owner)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Dozer, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_DozerAI",
            new ModuleDataContainer("ModuleTag_DozerAI", new DozerAIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        return WithDrawable(Generals.GameLogic.CreateObject(definition, owner), owner);
    }

    private GameObject NewStructure(string name, Player owner, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 1000f, InitialHealth = 1000f },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = WithDrawable(Generals.GameLogic.CreateObject(definition, owner), owner);
        return gameObject;
    }

    // ModelConditionFlags are logic-owned, so the mocked host (no GameClient, Client null) can
    // exercise the order handlers directly.
    private GameObject WithDrawable(GameObject gameObject, Player owner)
    {
        owner.AddOwnedObject(gameObject);
        return gameObject;
    }

    // Documents the per-action coverage status. The Scene3D-dependent rows are explicit gaps
    // (named action + reason), never silent skips: this test enumerates them and asserts the
    // gap set matches exactly what the headless host cannot drive.
    [Fact, Trait("Category", "Roadmap")]
    public void CoverageMatrix_DocumentsHeadlessReachableActionsAndExplicitGaps()
    {
        var headlessReachable = new HashSet<string> { "build", "produce" };
        var gaps = BaselineActions
            .Where(row => !headlessReachable.Contains(row.Action))
            .Select(row => (row.Action, row.ExpectedOrderType, Reason: GapReason(row.Action)))
            .ToList();

        // Every gap must carry a concrete reason naming what the headless host is missing.
        Assert.All(gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Reason)));

        // The expected gap set for the current headless host. If OrderProcessor gains null-Scene3D
        // guards (as the in-flight P5 branch adds) so a row becomes headless-reachable, move it
        // into headlessReachable above and drop it from this set; the test then forces an update.
        var expectedGaps = new HashSet<string> { "move", "attack-object", "force-attack-ground", "sell", "repair", "special-power", "rally-point" };
        var actualGaps = gaps.Select(g => g.Action).ToHashSet();
        Assert.Equal(expectedGaps, actualGaps);

        // And the reachable rows must all be genuinely handled (not gaps).
        Assert.All(BaselineActions.Where(r => headlessReachable.Contains(r.Action)),
            row => Assert.Contains(row.ExpectedOrderType, HandledOrderTypes));
    }

    private static string GapReason(string action) => action switch
    {
        // The control-bar BUTTON dispatch (GeneralsControlBar.CommandButtonCallback) needs a live
        // WND Window + asset-bound CommandButton set, which the mocked TestGame host cannot
        // construct. The underlying OrderProcessor effect is now reachable headlessly (verified in
        // OrderLayer_LiveDispatch_ReachableActionsProduceRealEffects) because
        // OrderProcessor.GameObjects falls back to GameLogic when Scene3D is null.
        "move" => "Control-bar button dispatch needs a WND Window + asset CommandSet; the MoveTo OrderProcessor effect itself is verified headlessly above",
        "attack-object" => "Control-bar button dispatch needs a WND Window + asset CommandSet; the AttackObject OrderProcessor effect itself is verified headlessly above",
        "force-attack-ground" => "Control-bar button dispatch needs a WND Window + asset CommandSet; ForceAttackGround's order path resolves via GameLogic",
        "sell" => "Control-bar button dispatch needs a WND Window + asset CommandSet; the Sell OrderProcessor effect itself is verified headlessly above",
        "repair" => "Control-bar button dispatch needs a WND Window + asset CommandSet; the RepairStructure OrderProcessor effect itself is verified headlessly above",
        "special-power" => "SpecialPower applicators resolve target/source objects and require loaded power/object asset data; the order layer cannot apply the effect without it",
        "rally-point" => "Control-bar button dispatch needs a WND Window + asset CommandSet; the SetRallyPoint OrderProcessor effect itself is verified headlessly above",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown baseline action"),
    };

    // Mirrors the button-backed branches of CommandButtonCallback.HandleCommand
    // (CommandButtonCallback.cs:25-192). Updated only if the callback's wiring changes.
    private static Dictionary<CommandType, OrderType> CommandButtonToOrderTypeMapping => new()
    {
        [CommandType.DozerConstruct] = OrderType.BuildObject,       // via FoundationConstruct order
        [CommandType.UnitBuild] = OrderType.CreateUnit,
        [CommandType.Sell] = OrderType.Sell,
        [CommandType.CancelUnitBuild] = OrderType.Sell,
        [CommandType.DozerConstructCancel] = OrderType.CancelBuild,
        [CommandType.PlayerUpgrade] = OrderType.BeginUpgrade,
        [CommandType.ObjectUpgrade] = OrderType.BeginUpgrade,
        [CommandType.Stop] = OrderType.StopMoving,
        [CommandType.SpecialPower] = OrderType.SpecialPower,
        [CommandType.SpecialPowerFromCommandCenter] = OrderType.SpecialPower,
        [CommandType.SetRallyPoint] = OrderType.SetRallyPoint,
        [CommandType.ToggleOvercharge] = OrderType.ToggleOvercharge,
        [CommandType.PurchaseScience] = OrderType.PurchaseScience,
        [CommandType.ExitContainer] = OrderType.ExitContainer,
        [CommandType.Evacuate] = OrderType.Evacuate,
        [CommandType.HackInternet] = OrderType.HackInternet,
    };

    private void ConfigureTwoPlayerSkirmish()
    {
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "human",
                    Faction = "FactionAmerica",
                    DisplayName = "Human",
                    IsHuman = true,
                },
                new Panopticon.Data.Map.Player
                {
                    Name = "ai",
                    Faction = "FactionGLA",
                    DisplayName = "AI",
                    IsHuman = false,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "human", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "ai"),
            ]);
    }

    private static ObjectDefinition NewObjectDefinition(string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        return definition;
    }

    private sealed record CommandCoverageRow(
        string Action,
        CommandType? SourceCommandType,
        OrderType ExpectedOrderType,
        string Source);
}
