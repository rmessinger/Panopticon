#nullable enable annotations
using System;
using OpenSage.Data.Ini;

namespace OpenSage.Logic.Object;

public sealed class SpecialAbilityModule : SpecialPowerModule
{
    internal SpecialAbilityModule(GameObject gameObject, IGameEngine gameEngine, SpecialAbilityModuleData moduleData) : base(gameObject, gameEngine, moduleData)
    {
    }

    /// <summary>
    /// Executes this ability's effect on the target object. The effect per power type mirrors the
    /// original game's outcome: building capture and the vehicle/building disable and cash-steal
    /// hacks. Timing data (duration, steal amount) comes from the paired
    /// <see cref="SpecialAbilityUpdate"/> module, matched by special-power template name.
    /// </summary>
    internal void Activate(GameObject target)
    {
        var abilityData = FindPairedAbilityUpdateData();

        switch (SpecialPowerType)
        {
            case SpecialPowerType.BlackLotusCaptureBuilding:
            case SpecialPowerType.InfantryCaptureBuilding:
                CaptureObject(target);
                break;

            case SpecialPowerType.HackerDisableBuilding:
            case SpecialPowerType.BlackLotusDisableVehicleHack:
                DisableTarget(target, abilityData);
                break;

            case SpecialPowerType.BlackLotusStealCashHack:
                StealCash(target, abilityData);
                break;

            default:
                throw new NotImplementedException($"{SpecialPowerType} has no implemented ability effect");
        }

        base.Activate(target.Transform.Translation);
    }

    private SpecialAbilityUpdateModuleData? FindPairedAbilityUpdateData()
    {
        var templateName = Template.Name;
        foreach (var update in GameObject.FindBehaviors<SpecialAbilityUpdate>())
        {
            if (update.ModuleData?.SpecialPowerTemplate == templateName)
            {
                return update.ModuleData;
            }
        }
        return null;
    }

    private void CaptureObject(GameObject target)
    {
        // Owner equality is the guard: directly-spawned sandbox objects often carry no Team, so a
        // reference-equal team check would treat every teamless object as friendly.
        if (target.Owner == GameObject.Owner)
        {
            return;
        }

        target.Owner.DeselectUnit(target);
        if (GameObject.Owner.DefaultTeam != null)
        {
            target.Team = GameObject.Owner.DefaultTeam;
        }
        target.Owner = GameObject.Owner;
    }

    private void DisableTarget(GameObject target, SpecialAbilityUpdateModuleData? abilityData)
    {
        var durationMs = abilityData?.EffectDuration ?? 0;
        if (durationMs > 0)
        {
            var duration = LogicFrameSpan.FromMilliseconds(durationMs, GameEngine.SageGame);
            target.Disable(DisabledType.UserParalyzed, GameEngine.GameLogic.CurrentFrame + duration);
        }
        else
        {
            target.SetDisabled(DisabledType.UserParalyzed);
        }
    }

    private void StealCash(GameObject target, SpecialAbilityUpdateModuleData? abilityData)
    {
        var amount = abilityData?.EffectValue ?? 0;
        if (amount <= 0)
        {
            return;
        }

        var victim = target.Owner.BankAccount;
        var toTransfer = Math.Min(victim.Money, (uint)amount);
        victim.Withdraw(toTransfer);
        GameObject.Owner.BankAccount.Deposit(toTransfer);
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();
    }
}

public sealed class SpecialAbilityModuleData : SpecialPowerModuleData
{
    internal static new SpecialAbilityModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<SpecialAbilityModuleData> FieldParseTable = SpecialPowerModuleData.FieldParseTable
        .Concat(new IniParseTable<SpecialAbilityModuleData>());

    internal override SpecialAbilityModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new SpecialAbilityModule(gameObject, gameEngine, this);
    }
}
