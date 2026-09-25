#nullable enable

using System.Linq;
using Panopticon.Input;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.OrderGenerators;

// Context-mode targeting generator started by the ATTACK_MOVE command button.
// Waits for a destination click, issues an AttackMove order, then returns to
// the default UnitOrderGenerator. Right-click cancels via OrderGeneratorInputHandler.
public sealed class AttackMoveOrderGenerator : OrderGenerator
{
    public override bool CanDrag => false;

    public AttackMoveOrderGenerator(IGame game) : base(game) { }

    public override OrderGeneratorResult TryActivate(IScene3D scene, KeyModifiers keyModifiers)
    {
        if (SelectedUnits is null || SelectedUnits.Count == 0)
        {
            return OrderGeneratorResult.Inapplicable();
        }

        var playerId = scene.GetPlayerIndex(LocalPlayer);
        var order = Order.CreateAttackMove(playerId, WorldPosition);

        return OrderGeneratorResult.SuccessAndExit(new[] { order });
    }

    public override string? GetCursor(KeyModifiers keyModifiers)
    {
        return Cursors.CursorForOrder(OrderType.AttackMove);
    }
}
