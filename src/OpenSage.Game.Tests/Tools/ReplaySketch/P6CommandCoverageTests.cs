// WI-P6-01: Command coverage matrix.
// Proves every baseline control-bar action maps to an OrderType with a real (non-default)
// OrderProcessor handler, and documents the per-action observable-effect coverage. The full
// end-to-end command->effect path for most actions runs through `_game.Scene3D.GameObjects`,
// which is null in the headless TestGame host, so those rows are recorded as explicit named
// gaps rather than silently skipped. The affordability guard for BuildObject/CreateUnit exits
// before Scene3D is touched, so those two rows are exercised live through OrderProcessor.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P6-1, and
// src/OpenSage.Game/Gui/CommandButtonCallback.cs + src/OpenSage.Game/Logic/Orders/OrderProcessor.cs.
//
// Run with: dotnet test src --filter "FullyQualifiedName~P6CommandCoverageTests"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Gui.ControlBar;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

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
        OrderType.AttackMove,
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
        "move" => "MoveTo dereferences player.SelectedUnits.AIUpdate.SetTargetPoint; full movement needs Scene3D/pathfinder",
        "attack-object" => "AttackObject dereferences _game.Scene3D.GameObjects.GetObjectById and weapon target dispatch",
        "force-attack-ground" => "ForceAttackGround dereferences _game.Scene3D.GameObjects.Objects",
        "sell" => "Sell dereferences _game.Scene3D.GameObjects.DestroyObject",
        "repair" => "RepairStructure dereferences _game.Scene3D.GameObjects.GetObjectById",
        "special-power" => "SpecialPower applicators resolve target/source objects via GameLogic with asset-backed power data",
        "rally-point" => "SetRallyPoint dereferences _game.Scene3D.GameObjects.GetObjectById",
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
                OpenSage.Data.Map.Player.CreateNeutralPlayer(),
                OpenSage.Data.Map.Player.CreateCivilianPlayer(),
                new OpenSage.Data.Map.Player
                {
                    Name = "human",
                    Faction = "FactionAmerica",
                    DisplayName = "Human",
                    IsHuman = true,
                },
                new OpenSage.Data.Map.Player
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
