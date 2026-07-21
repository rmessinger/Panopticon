// P4-1 economy-loop verification: proves the supply -> BankAccount.Deposit side
// of the money cycle. The gather -> dump path is wired through SupplyAIUpdate ->
// SupplyCenterDockUpdate.DumpBoxes -> BankAccount.Deposit; these tests exercise
// the docked deposit path directly (the production code that credits the owner)
// plus the BankAccount invariants, so the cycle is regression-protected without
// needing a full skirmish boot.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P4-1.
//
// Run with: dotnet test src --filter "FullyQualifiedName~GatherDepositTests"

using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Economy;

public class GatherDepositTests : MockedGameTest
{
    private const uint StartingMoney = 1_000u;

    [Fact, Trait("Category", "Roadmap")]
    public void BankAccount_Deposit_IncreasesBalance()
    {
        var player = CreatePlayerWithMoney(Generals, StartingMoney);

        player.BankAccount.Deposit(250, playSound: false);

        Assert.Equal((uint)(StartingMoney + 250), player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BankAccount_Deposit_Zero_IsNoOp()
    {
        var player = CreatePlayerWithMoney(Generals, StartingMoney);

        player.BankAccount.Deposit(0, playSound: false);

        Assert.Equal(StartingMoney, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BankAccount_Withdraw_ClampsToBalance_NeverGoesNegative()
    {
        var player = CreatePlayerWithMoney(Generals, 100u);

        // Withdraw more than the balance: Withdraw returns what it could take and
        // leaves Money at zero, the universal "balance never < 0" invariant.
        var taken = player.BankAccount.Withdraw(500u, playSound: false);

        Assert.Equal(100u, taken);
        Assert.Equal(0u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SupplyCenterDockUpdate_DumpBoxes_CreditsOwnerBankAccount()
    {
        // The deposit side of the gather -> dump cycle. A harvester carrying N boxes
        // dumps at a supply center; the center's dock update multiplies boxes by the
        // per-box value (from GameData.ValuePerSupplyBox) and credits the owner.
        SetValuePerSupplyBox(Generals, 100);

        var owner = CreatePlayerWithMoney(Generals, StartingMoney);
        var supplyCenter = CreateSupplyCenter(Generals, owner);
        var dockUpdate = supplyCenter.FindBehavior<SupplyCenterDockUpdate>();
        Assert.NotNull(dockUpdate);

        var numBoxes = 3;
        var amount = dockUpdate!.DumpBoxes(Generals.AssetStore, ref numBoxes, additionalAmountPerBox: 0);

        Assert.Equal(0, numBoxes);                       // boxes cleared after dump
        Assert.Equal(3 * 100, amount);                   // 3 boxes x 100 each x 1.0 multiplier
        Assert.Equal((uint)(StartingMoney + 300), owner.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SupplyCenterDockUpdate_DumpBoxes_AppliesValueMultiplier()
    {
        SetValuePerSupplyBox(Generals, 50);

        var owner = CreatePlayerWithMoney(Generals, 0u);
        var supplyCenter = CreateSupplyCenter(Generals, owner, valueMultiplier: 2.0f);
        var dockUpdate = supplyCenter.FindBehavior<SupplyCenterDockUpdate>()!;

        var numBoxes = 4;
        var amount = dockUpdate.DumpBoxes(Generals.AssetStore, ref numBoxes, additionalAmountPerBox: 0);

        Assert.Equal(4 * 50 * 2, amount);
        Assert.Equal((uint)(4 * 50 * 2), owner.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SupplyCenterDockUpdate_DumpBoxes_AppliesAdditionalAmountPerBox()
    {
        // GLA worker upgrade (BonusIcons etc.) routes through the additionalAmountPerBox
        // parameter rather than ValueMultiplier, so the deposit path covers that vector too.
        SetValuePerSupplyBox(Generals, 100);

        var owner = CreatePlayerWithMoney(Generals, 0u);
        var supplyCenter = CreateSupplyCenter(Generals, owner);
        var dockUpdate = supplyCenter.FindBehavior<SupplyCenterDockUpdate>()!;

        var numBoxes = 2;
        var amount = dockUpdate.DumpBoxes(Generals.AssetStore, ref numBoxes, additionalAmountPerBox: 25);

        Assert.Equal(2 * (100 + 25), amount);
        Assert.Equal((uint)(2 * 125), owner.BankAccount.Money);
    }

    private static Player CreatePlayerWithMoney(TestGame game, uint money)
    {
        // Use the neutral player (index 0) already registered by the mocked TestGame
        // so BankAccount.Deposit's PlayerManager.GetPlayerByIndex(PlayerIndex) resolves.
        var player = game.PlayerManager.GetPlayerByIndex(0);
        player.BankAccount.Money = money;
        return player;
    }

    private static void SetValuePerSupplyBox(TestGame game, int value)
    {
        // GameData.ValuePerSupplyBox has a private setter (INI-populated); reflect to
        // pin a known per-box value so dock-deposit assertions have deterministic amounts.
        typeof(GameData)
            .GetProperty(nameof(GameData.ValuePerSupplyBox))!
            .SetValue(game.AssetStore.GameData.Current, value);
    }

    private static GameObject CreateSupplyCenter(TestGame game, Player owner, float valueMultiplier = 1.0f)
    {
        var definition = new ObjectDefinition("P4TestSupplyCenter");
        definition.KindOf.Set(ObjectKinds.Structure, true);

        var moduleData = new SupplyCenterDockUpdateModuleData();
        if (valueMultiplier != 1.0f)
        {
            typeof(SupplyCenterDockUpdateModuleData)
                .GetProperty(nameof(SupplyCenterDockUpdateModuleData.ValueMultiplier))!
                .SetValue(moduleData, valueMultiplier);
        }

        definition.Behaviors.Add("ModuleTag_DockUpdate",
            new ModuleDataContainer("ModuleTag_DockUpdate", moduleData, ModuleInheritanceMode.Default, inherited: false));

        return new GameObject(definition, game.GameEngine, owner);
    }
}
