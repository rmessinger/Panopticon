using System;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Ini;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

/// <summary>
/// Just does damage.
/// </summary>
[AddedIn(SageGame.Bfme)]
public class DamageNugget : WeaponEffectNugget
{
    internal static DamageNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private protected static new readonly IniParseTable<DamageNugget> FieldParseTable = WeaponEffectNugget.FieldParseTable
        .Concat(new IniParseTable<DamageNugget>
        {
            { "Damage", (parser, x) => x.Damage = parser.ParseFloat() },
            { "Radius", (parser, x) => x.Radius = parser.ParseFloat() },
            { "DelayTime", (parser, x) => x.DelayTime = parser.ParseInteger() },
            { "DamageType", (parser, x) => x.DamageType = parser.ParseEnum<DamageType>() },
            { "DamageFXType", (parser, x) => x.DamageFXType = parser.ParseEnum<DamageFXType>() },
            { "DeathType", (parser, x) => x.DeathType = parser.ParseEnum<DeathType>() },
            { "DamageSpeed", (parser, x) => x.DamageSpeed = parser.ParseFloat() },
            { "DamageArc", (parser, x) => x.DamageArc = parser.ParseInteger() },
            { "DamageScalar", (parser, x) => x.DamageScalar = DamageScalar.Parse(parser) },
            { "DamageMaxHeight", (parser, x) => x.DamageMaxHeight = parser.ParseInteger() },
            { "AcceptDamageAdd", (parser, x) => x.AcceptDamageAdd = parser.ParseBoolean() },
            { "FlankingBonus", (parser, x) => x.FlankingBonus = parser.ParsePercentage() },
            { "DamageTaperOff", (parser, x) => x.DamageTaperOff = parser.ParseInteger() },
            { "DamageSubType", (parser, x) => x.DamageSubType = parser.ParseEnum<DamageType>() },
            { "DrainLife", (parser, x) => x.DrainLife = parser.ParseBoolean() },
            { "DrainLifeMultiplier", (parser, x) => x.DrainLifeMultiplier = parser.ParseFloat() },
            { "CylinderAOE", (parser, x) => x.CylinderAOE = parser.ParseBoolean() },
            { "DamageArcInverted", (parser, x) => x.DamageArcInverted = parser.ParseBoolean() },
            { "ForceKillObjectFilter", (parser, x) => x.ForceKillObjectFilter = ObjectFilter.Parse(parser) },
            { "DamageMaxHeightAboveTerrain", (parser, x) => x.DamageMaxHeightAboveTerrain = parser.ParseInteger() },
            { "MinRadius", (parser, x) => x.MinRadius = parser.ParseInteger() },
            { "LostLeadershipUselessAgainst", (parser, x) => x.LostLeadershipUselessAgainst = parser.ParseEnum<ObjectKinds>() }
        });

    /// <summary>
    /// Damage for a normal hit.
    /// </summary>
    public float Damage { get; internal set; }

    /// <summary>
    /// Radius of damage.
    /// </summary>
    public float Radius { get; internal set; }

    // Generals secondary damage occupies an outer ring, not an additional overlapping blast.
    internal DamageNugget InnerDamageNugget { get; set; }

    /// <summary>
    /// Delay after hit till the damage is applied.
    /// </summary>
    public int DelayTime { get; private set; }
    public DamageType DamageType { get; internal set; }

    /// <summary>
    /// Used when <see cref="DamageType" is <see cref="DamageType.Status"/> />.
    /// </summary>
    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public DamageStatusType DamageStatusType { get; internal set; }

    public DamageFXType DamageFXType { get; private set; }

    /// <summary>
    /// Damage type used only to select damage effects; <see cref="DamageType.Unresistable"/>
    /// means no override.
    /// </summary>
    public DamageType DamageFXOverride { get; internal set; } = DamageType.Unresistable;
    public DeathType DeathType { get; internal set; }

    [AddedIn(SageGame.Bfme)]
    public float DamageSpeed { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int DamageArc { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public DamageScalar DamageScalar { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public int DamageMaxHeight { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool AcceptDamageAdd { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public ObjectKinds LostLeadershipUselessAgainst { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public Percentage FlankingBonus { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public int DamageTaperOff { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public DamageType DamageSubType { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool DrainLife { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public float DrainLifeMultiplier { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool CylinderAOE { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool DamageArcInverted { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public ObjectFilter ForceKillObjectFilter { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public int DamageMaxHeightAboveTerrain { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public int MinRadius { get; private set; }

    internal override void Execute(WeaponEffectExecutionContext context)
    {
        var weaponBonus = context.Weapon.ParentGameObject.FindBehavior<WeaponBonusUpgrade>();
        if (weaponBonus?.Triggered ?? false)
        {
            //TODO: increase damage with context.Weapon.Template.WeaponBonuses
        }

        var target = context.Weapon.CurrentTarget;
        if (target == null)
        {
            return;
        }

        var source = context.Weapon.ParentGameObject;
        var damageDealer = source.IsProjectile
            ? context.GameEngine.GameLogic.GetObjectById(source.CreatedByObjectID) ?? source
            : source;
        var directTarget = target.TargetType == WeaponTargetType.Object ? target.GetTargetObject() : null;
        var damageInput = new DamageInfoInput(damageDealer)
        {
            DamageType = DamageType,
            DamageFXOverride = DamageFXOverride,
            DeathType = DeathType,
            Amount = Damage,
        };
        if (Radius <= 0)
        {
            if (InnerDamageNugget == null)
            {
                directTarget?.AttemptDamage(damageInput);
            }

            return;
        }

        if (!source.IsProjectile && target.IsDestroyed)
        {
            return;
        }

        // Object hits center the blast on the victim; ground hits and orphaned missiles use the impact position.
        var position = directTarget?.Translation ?? (source.IsProjectile ? source.Translation : target.TargetPosition);
        var victims = context.GameEngine.Game.PartitionCellManager
            .QueryObjects(null, position, Radius, new PartitionQueries.TrueQuery())
            .Distinct()
            .OrderBy(victim => victim.Id.Index)
            .ToArray();
        foreach (var victim in victims)
        {
            if (victim.IsEffectivelyDead || victim.BodyModule == null || victim.IsProjectile)
            {
                continue;
            }

            var bounds = victim.RoughCollider?.WorldBounds;
            var distance = MathF.Max(0f, Vector3.Distance(position, bounds?.Center ?? victim.Translation) - (bounds?.Radius ?? 0f));
            if (distance > Radius || (InnerDamageNugget != null && distance <= InnerDamageNugget.Radius))
            {
                continue;
            }

            if (victim == directTarget || AffectsSplashTarget(victim, source, context.Weapon.Template.RadiusDamageAffects))
            {
                victim.AttemptDamage(damageInput);
            }
        }
    }

    private static bool AffectsSplashTarget(GameObject victim, GameObject source, ObjectFilter filter)
    {
        var isSelf = victim == source || victim.Id == source.CreatedByObjectID;
        if (isSelf && filter?.Rules.Get(ObjectFilterRule.Self) != true && filter?.Rules.Get(ObjectFilterRule.Suicide) != true)
        {
            return false;
        }

        if (filter == null)
        {
            return true;
        }

        if (filter.Rules.Get(ObjectFilterRule.None) || filter.Exclude.Intersects(victim.Definition.KindOf)
            || filter.ExcludeThings?.Contains(victim.Definition.Name, StringComparer.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        var allied = victim.Owner == source.Owner || source.Owner?.Allies.Contains(victim.Owner) == true;
        if ((filter.Rules.Get(ObjectFilterRule.NotSimilar) && allied && victim.Definition == source.Definition)
            || (filter.Rules.Get(ObjectFilterRule.NotAirborne) && victim.IsSignificantlyAboveTerrain))
        {
            return false;
        }

        var relationship = allied ? ObjectFilterRule.Allies
            : source.Owner?.Enemies.Contains(victim.Owner) == true ? ObjectFilterRule.Enemies
            : ObjectFilterRule.Neutrals;
        return filter.Rules.Get(ObjectFilterRule.All) || filter.Rules.Get(relationship)
            || (filter.Rules.Get(ObjectFilterRule.SamePlayer) && victim.Owner == source.Owner)
            || (isSelf && (filter.Rules.Get(ObjectFilterRule.Self) || filter.Rules.Get(ObjectFilterRule.Suicide)))
            || filter.Include.Intersects(victim.Definition.KindOf)
            || filter.IncludeThings?.Contains(victim.Definition.Name, StringComparer.OrdinalIgnoreCase) == true;
    }
}
