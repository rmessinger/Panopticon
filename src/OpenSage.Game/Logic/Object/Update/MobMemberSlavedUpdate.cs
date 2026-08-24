#nullable enable annotations
using System.Numerics;
using OpenSage.Data.Ini;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object;

/// <summary>
/// Keeps an Angry Mob member with its nexus: the member follows the nexus when it falls behind
/// (beyond <see cref="MobMemberSlavedUpdateModuleData.MustCatchUpRadius"/>) and stops once back
/// within <see cref="MobMemberSlavedUpdateModuleData.NoNeedToCatchUpRadius"/>, and attacks
/// whatever the nexus's (harmless, controller) weapon targets. The nexus cannot deal damage
/// itself; the member weapons are the mob's damage output.
/// </summary>
public sealed class MobMemberSlavedUpdate : UpdateModule
{
    private readonly MobMemberSlavedUpdateModuleData _moduleData;

    private GameObject? _master;

    internal MobMemberSlavedUpdate(GameObject gameObject, IGameEngine gameEngine, MobMemberSlavedUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;
    }

    internal void SetMaster(GameObject master)
    {
        _master = master;
    }

    public override UpdateSleepTime Update()
    {
        if (GameObject.IsEffectivelyDead)
        {
            return UpdateSleepTime.Forever;
        }

        if (_master == null || _master.IsEffectivelyDead)
        {
            return UpdateSleepTime.None;
        }

        var masterTarget = _master.CurrentWeapon?.CurrentTarget;
        var masterTargetObject = masterTarget != null && !masterTarget.IsDestroyed
            ? masterTarget.GetTargetObject()
            : null;

        if (masterTargetObject != null && GameObject.CurrentWeapon != null)
        {
            // Attack propagation: mirror the nexus's target onto the member. AIUpdate stops the
            // member and the weapon fires once the target is inside the member's own range;
            // until then, walk toward the target.
            if (GameObject.CurrentWeapon.CurrentTarget != masterTarget)
            {
                GameObject.CurrentWeapon.SetTarget(masterTarget!);
            }

            if (!GameObject.CurrentWeapon.HasValidTarget && GameObject.AIUpdate?.TargetPoints.Count == 0)
            {
                GameObject.AIUpdate.SetTargetPoint(masterTargetObject.Translation);
            }

            return UpdateSleepTime.None;
        }

        // No nexus target: stay with the nexus. Catch up when beyond MustCatchUpRadius, stop
        // wandering once back inside NoNeedToCatchUpRadius.
        var distanceToMaster = Vector3.Distance(GameObject.Translation, _master.Translation);
        if (distanceToMaster > _moduleData.MustCatchUpRadius)
        {
            if (GameObject.AIUpdate?.TargetPoints.Count == 0)
            {
                GameObject.AIUpdate.SetTargetPoint(_master.Translation);
            }
        }
        else if (distanceToMaster < _moduleData.NoNeedToCatchUpRadius)
        {
            GameObject.AIUpdate?.Stop();
        }

        return UpdateSleepTime.None;
    }
}

public sealed class MobMemberSlavedUpdateModuleData : UpdateModuleData
{
    internal static MobMemberSlavedUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<MobMemberSlavedUpdateModuleData> FieldParseTable = new IniParseTable<MobMemberSlavedUpdateModuleData>
    {
        { "MustCatchUpRadius", (parser, x) => x.MustCatchUpRadius = parser.ParseInteger() },
        { "NoNeedToCatchUpRadius", (parser, x) => x.NoNeedToCatchUpRadius = parser.ParseInteger() },
        { "Squirrelliness", (parser, x) => x.Squirrelliness = parser.ParseFloat() },
        { "CatchUpCrisisBailTime", (parser, x) => x.CatchUpCrisisBailTime = parser.ParseInteger() },
    };

    public int MustCatchUpRadius { get; private set; }
    public int NoNeedToCatchUpRadius { get; private set; }

    /// <summary>Random wander amplitude while idle; not implemented (members walk straight).</summary>
    public float Squirrelliness { get; private set; }

    public int CatchUpCrisisBailTime { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new MobMemberSlavedUpdate(gameObject, gameEngine, this);
    }
}
