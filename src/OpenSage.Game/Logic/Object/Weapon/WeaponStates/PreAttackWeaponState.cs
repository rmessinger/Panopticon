namespace OpenSage.Logic.Object;

internal sealed class PreAttackWeaponState : FixedDurationWeaponState
{
    protected override RangeDuration Duration => Context.Weapon.Template.PreAttackDelay;

    public PreAttackWeaponState(WeaponStateContext context)
        : base(context)
    {
    }

    protected override ModelConditionFlag[] GetModelConditionFlags(int weaponIndex) =>
        new[]
        {
            ModelConditionFlagUtility.GetPreAttackFlag(weaponIndex),
            ModelConditionFlagUtility.GetFiringOrPreAttackFlag(weaponIndex)
        };

    public override WeaponState? GetNextState()
    {
        if (!Context.Weapon.HasValidTarget)
        {
            return WeaponState.Inactive;
        }

        // Turret-controlled weapons wait for the turret to train on the target (including
        // pitch elevation, e.g. the SCUD launcher raising its erector arm) so the projectile
        // launches from the aimed launch bone instead of the stowed one.
        if (Context.GameObject.AIUpdate is { IsTurretAimed: false })
        {
            return null;
        }

        if (IsTimeToExitState())
        {
            return WeaponState.Firing;
        }

        return null;
    }
}
