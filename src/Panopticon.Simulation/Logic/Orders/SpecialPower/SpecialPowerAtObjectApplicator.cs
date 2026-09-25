#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.Orders.SpecialPower;

// cash hack                 SpecialPowerAtObject(Integer:14, ObjectId:674, Integer:643, ObjectId:657)
// missile laser lock        SpecialPowerAtObject(Integer:23, ObjectId:700, Integer:771, ObjectId:0)
// tank hunter tnt           SpecialPowerAtObject(Integer:24, ObjectId:6, Integer:259, ObjectId:0)
// burton remote demo charge SpecialPowerAtObject(Integer:25, ObjectId:2, Integer:771, ObjectId:0)
// burton timed demo charge  SpecialPowerAtObject(Integer:26, ObjectId:3, Integer:259, ObjectId:0)
// hacker disable building   SpecialPowerAtObject(Integer:27, ObjectId:8, Integer:259, ObjectId:0)
// lotus capture building    SpecialPowerAtObject(Integer:28, ObjectId:8, Integer:259, ObjectId:0)
// usa ranger capture        SpecialPowerAtObject(Integer:29, ObjectId:8, Integer:323, ObjectId:0)
// china red guard capture   SpecialPowerAtObject(Integer:30, ObjectId:8, Integer:323, ObjectId:0)
// gla rebel capture         SpecialPowerAtObject(Integer:31, ObjectId:8, Integer:323, ObjectId:0)
// lotus disable vehicle     SpecialPowerAtObject(Integer:32, ObjectId:6, Integer:259, ObjectId:0)
// lotus cash hack           SpecialPowerAtObject(Integer:33, ObjectId:674, Integer:259, ObjectId:0)
// bomb truck disguise       SpecialPowerAtObject(Integer:36, ObjectId:1176, Integer:263, ObjectId:0)
internal abstract class SpecialPowerAtObjectApplicator
{
    internal static void Execute(SpecialPowerType specialPower, in ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        Action<ObjectArguments, IReadOnlyCollection<GameObject>> executor = specialPower switch
        {
            SpecialPowerType.CashHack => ExecuteCashHack,
            SpecialPowerType.MissileDefenderLaserGuidedMissiles => throw new NotImplementedException(),
            SpecialPowerType.TankHunterTntAttack => throw new NotImplementedException(),
            SpecialPowerType.RemoteCharges => throw new NotImplementedException(),
            SpecialPowerType.TimedCharges => throw new NotImplementedException(),
            SpecialPowerType.HackerDisableBuilding => ExecuteHackerDisableBuilding,
            SpecialPowerType.BlackLotusCaptureBuilding => ExecuteBlackLotusCaptureBuilding,
            SpecialPowerType.InfantryCaptureBuilding => ExecuteInfantryCaptureBuilding,
            SpecialPowerType.BlackLotusDisableVehicleHack => ExecuteBlackLotusDisableVehicleHack,
            SpecialPowerType.BlackLotusStealCashHack => ExecuteBlackLotusStealCashHack,
            SpecialPowerType.DisguiseAsVehicle => throw new NotImplementedException(),
            _ => throw new ArgumentOutOfRangeException(nameof(specialPower),
                $"{specialPower} is not a supported object power"),
        };

        executor(arguments, selectedObjects);
    }

    private static void ExecuteCashHack(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        arguments.CommandCenter?.FindBehavior<CashHackSpecialPower>()?.Activate(arguments.Target);
    }

    private static void ExecuteHackerDisableBuilding(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        ExecuteSpecialAbility(SpecialPowerType.HackerDisableBuilding, arguments, selectedObjects);
    }

    private static void ExecuteBlackLotusCaptureBuilding(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        ExecuteSpecialAbility(SpecialPowerType.BlackLotusCaptureBuilding, arguments, selectedObjects);
    }

    private static void ExecuteInfantryCaptureBuilding(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        ExecuteSpecialAbility(SpecialPowerType.InfantryCaptureBuilding, arguments, selectedObjects);
    }

    private static void ExecuteBlackLotusDisableVehicleHack(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        ExecuteSpecialAbility(SpecialPowerType.BlackLotusDisableVehicleHack, arguments, selectedObjects);
    }

    private static void ExecuteBlackLotusStealCashHack(ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        ExecuteSpecialAbility(SpecialPowerType.BlackLotusStealCashHack, arguments, selectedObjects);
    }

    private static void ExecuteSpecialAbility(SpecialPowerType specialPower, ObjectArguments arguments, IReadOnlyCollection<GameObject> selectedObjects)
    {
        // todo: first object where special power is ready, since we could have multiple selected
        selectedObjects.FirstOrDefault()?.FindSpecialPowerBehavior<SpecialAbilityModule>(specialPower)
            ?.Activate(arguments.Target);
    }
}

// var specialPowerDefinitionId = (SpecialPowerType) order.Arguments[0].Value.Integer;
// var targetId = order.Arguments[1].Value.ObjectId;
// var commandFlags = (SpecialPowerOrderFlags) order.Arguments[2].Value.Integer;
// var commandCenterSource = order.Arguments[3].Value.ObjectId;
internal readonly record struct ObjectArguments(Player Caller, GameObject Target, SpecialPowerOrderFlags OrderFlags, GameObject? CommandCenter);
