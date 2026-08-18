namespace OpenSage.Logic.Object;

public abstract class DamageModule : BehaviorModule, IDamageModule
{
    protected DamageModule(GameObject gameObject, IGameEngine gameEngine) : base(gameObject, gameEngine)
    {
    }

    public virtual void OnDamage(in DamageInfo damageInfo) { }

    public virtual void OnHealing(in DamageInfo damageInfo) { }

    public virtual void OnBodyDamageStateChange(
        in DamageInfo damageInfo,
        BodyDamageType oldState,
        BodyDamageType newState)
    {

    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();
    }
}

internal interface IDamageModule
{
    void OnDamage(in DamageInfo damageInfo) { }

    void OnHealing(in DamageInfo damageInfo) { }

    void OnBodyDamageStateChange(
        in DamageInfo damageInfo,
        BodyDamageType oldState,
        BodyDamageType newState)
    {

    }
}

public abstract class DamageModuleData : ContainModuleData
{
    public override ModuleKinds ModuleKinds => ModuleKinds.Damage;
}

public static class DamageConstants
{
    public const float HugeDamageAmount = 999999.0f;

    // Generals' FLESHY_SNIPER and Zero Hour's SUBDUAL_MISSILE share enum value 31 (the original
    // engine reused the slot), so value-based checks must be disambiguated by the running game.

    public static bool IsHealthDamagingDamage(this DamageType damageType, SageGame game) => damageType switch
    {
        DamageType.Status => false,
        DamageType.SubdualMissile => game == SageGame.CncGenerals,
        DamageType.SubdualVehicle => false,
        DamageType.SubdualBuilding => false,
        DamageType.SubdualUnresistable => false,
        DamageType.KillPilot => false,
        DamageType.KillGarrisoned => false,
        _ => true,
    };

    public static bool IsSubdualDamage(this DamageType damageType, SageGame game) => damageType switch
    {
        DamageType.SubdualMissile => game != SageGame.CncGenerals,
        DamageType.SubdualVehicle => true,
        DamageType.SubdualBuilding => true,
        DamageType.SubdualUnresistable => true,
        _ => false,
    };
}
