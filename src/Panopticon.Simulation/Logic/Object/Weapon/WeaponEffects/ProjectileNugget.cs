using System;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Data.Ini;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

/// <summary>
/// Creates an object and sends it towards the target with a warhead.
/// </summary>
[AddedIn(SageGame.Bfme)]
public sealed class ProjectileNugget : WeaponEffectNugget
{
    internal static ProjectileNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<ProjectileNugget> FieldParseTable = WeaponEffectNugget.FieldParseTable
        .Concat(new IniParseTable<ProjectileNugget>
        {
            { "ProjectileTemplateName", (parser, x) => x.ProjectileTemplate = parser.ParseObjectReference() },
            { "WarheadTemplateName", (parser, x) => x.WarheadTemplate = parser.ParseWeaponTemplateReference() },

            { "AlwaysAttackHereOffset", (parser, x) => x.AlwaysAttackHereOffset = parser.ParseVector3() },
            { "UseAlwaysAttackOffset", (parser, x) => x.UseAlwaysAttackOffset = parser.ParseBoolean() },
            { "WeaponLaunchBoneSlotOverride", (parser, x) => x.WeaponLaunchBoneSlotOverride = parser.ParseEnum<WeaponSlot>() }
        });

    internal bool IsConvertedFromLegacyData;

    internal WeaponTemplate ParentWeaponTemplate;

    public LazyAssetReference<ObjectDefinition> ProjectileTemplate { get; internal set; }
    public LazyAssetReference<WeaponTemplate> WarheadTemplate { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public Vector3 AlwaysAttackHereOffset { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool UseAlwaysAttackOffset { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public WeaponSlot WeaponLaunchBoneSlotOverride { get; private set; } = WeaponSlot.NoWeapon;

    internal override void Execute(WeaponEffectExecutionContext context)
    {
        var projectileTemplate = ProjectileTemplate.Value;

        WeaponTemplate warheadTemplate;
        if (IsConvertedFromLegacyData)
        {
            warheadTemplate = new WeaponTemplate();
            warheadTemplate.ProjectileCollidesWith = ParentWeaponTemplate.ProjectileCollidesWith;
            warheadTemplate.Nuggets.AddRange(ParentWeaponTemplate.Nuggets.OfType<DamageNugget>());
        }
        else
        {
            warheadTemplate = WarheadTemplate.Value;
        }

        var projectileObject = context.GameEngine.GameLogic.CreateObject(
            projectileTemplate,
            context.Weapon.ParentGameObject.Owner);

        var launchBoneTransform = context.Weapon.ParentGameObject.Client.GetWeaponLaunchBoneTransform(
            context.Weapon.Slot, context.Weapon.WeaponIndex);

        // Models without a weapon-launch bone (e.g. the Angry Mob's rock and molotov throwers)
        // launch from the object's own transform, matching the original engine's fallback.
        projectileObject.SetTransformMatrix(
            launchBoneTransform ?? context.Weapon.ParentGameObject.Transform.Matrix);

        projectileObject.SetWarhead(warheadTemplate);

        projectileObject.CurrentWeapon.SetTarget(context.Weapon.CurrentTarget);

        projectileObject.Speed = ParentWeaponTemplate.WeaponSpeed;

        // Record the firing unit so projectile collision can skip it (otherwise a vehicle
        // launcher detonates its own projectile at the muzzle).
        var launcherId = context.Weapon.ParentGameObject.Id;
        projectileObject.CreatedByObjectID = launcherId;

        var bezierProjectileBehavior = projectileObject.FindBehavior<BezierProjectileBehavior>();
        if (bezierProjectileBehavior != null)
        {
            bezierProjectileBehavior.LauncherObjectId = launcherId;
        }

        var missileAIUpdate = projectileObject.FindBehavior<MissileAIUpdate>();
        if (missileAIUpdate != null)
        {
            missileAIUpdate.LauncherObjectId = launcherId;
        }

        var usesFlightPathOrientation = bezierProjectileBehavior != null
            ? bezierProjectileBehavior.UsesFlightPathOrientation
            : missileAIUpdate != null || projectileObject.Physics?.OrientsToFlightPath == true;
        if (usesFlightPathOrientation)
        {
            SetLeadingEdgeRotationPivot(projectileObject);
        }

        AttachMissileExhaust(context, missileAIUpdate, projectileObject);

        CreateShellTracer(context, projectileObject);
        CreateProjectileStream(context, projectileObject);

        if (IsConvertedFromLegacyData)
        {
            if (bezierProjectileBehavior != null)
            {
                bezierProjectileBehavior.DetonationFX = ParentWeaponTemplate.ProjectileDetonationFX?.Value;
            }

            if (missileAIUpdate != null)
            {
                missileAIUpdate.DetonationFX = ParentWeaponTemplate.ProjectileDetonationFX?.Value;
            }
        }
    }

    /// <summary>
    /// Arms the missile's rocket-motor exhaust (the weapon's ProjectileExhaust particle system).
    /// The system itself starts at ignition (MissileAIUpdate) and follows the model's tail: the
    /// leading-edge pivot moved the object origin to the nose, so the tail sits at
    /// Min.X - Max.X along local X of the shifted model space.
    /// </summary>
    private static void AttachMissileExhaust(
        WeaponEffectExecutionContext context,
        MissileAIUpdate? missileAIUpdate,
        GameObject projectileObject)
    {
        var exhaustName = context.Weapon.Template.ProjectileExhaust;
        if (missileAIUpdate == null
            || string.IsNullOrEmpty(exhaustName)
            || projectileObject.Client?.TryGetModelSpaceBounds(out var bounds) != true)
        {
            return;
        }

        var exhaustTemplate = context.GameEngine.AssetLoadContext.AssetStore.FXParticleSystemTemplates
            .GetByName(exhaustName);
        if (exhaustTemplate == null)
        {
            return;
        }

        missileAIUpdate.AttachExhaust(
            exhaustTemplate,
            new Vector3(bounds.Min.X - bounds.Max.X, 0f, 0f));
    }

    private static void SetLeadingEdgeRotationPivot(GameObject projectileObject)
    {
        if (projectileObject.Client?.TryGetModelSpaceBounds(out var bounds) != true)
        {
            return;
        }

        var center = bounds.GetCenter();
        var leadingPoint = new Vector3(bounds.Max.X, center.Y, center.Z);
        if (!float.IsFinite(leadingPoint.X)
            || !float.IsFinite(leadingPoint.Y)
            || !float.IsFinite(leadingPoint.Z))
        {
            return;
        }

        // Keep trajectory, collision, and detonation at the simulation origin. Only the W3D
        // model moves behind it, making the local +X leading point the visual rotation pivot.
        projectileObject.ModelTransform.Translation = -leadingPoint;

        // The model's authored origin (the SCUD missile's tail) sits on the launch bone. With
        // the nose now the visual pivot, advance the simulation origin by the same offset IN
        // THE OBJECT'S LOCAL FRAME (translation-first, so the bone rotation carries it up the
        // rail) — the rendered model keeps its authored launch position instead of dropping a
        // body length below the rail at the spawn swap.
        projectileObject.SetTransformMatrix(
            Matrix4x4.CreateTranslation(leadingPoint) * projectileObject.TransformMatrix);
    }

    // Tank cannons and artillery use "NoTracer" FireFX variants in the original data, so the
    // shell itself is the only flight visual. Engines that want a visible shell streak add a
    // tracer that follows the projectile. Weapons that already have a flight visual are
    // excluded: stream weapons draw a ribbon, and missiles with an exhaust trail already show
    // one. Scale follows the game's own tracer data (WeaponFX_GenericTankGun): a tank shell
    // tracer is twice an infantry tracer's length and width, tied to the shell's speed.
    private void CreateShellTracer(WeaponEffectExecutionContext context, GameObject projectileObject)
    {
        var weapon = ParentWeaponTemplate;
        if (!string.IsNullOrEmpty(weapon.ProjectileStreamName)
            || !string.IsNullOrEmpty(weapon.ProjectileExhaust))
        {
            return;
        }

        context.GameEngine.Scene3D?.TracerManager.CreateTracking(
            projectileObject,
            new ColorRgb(255, 230, 170),
            length: Math.Clamp(weapon.WeaponSpeed * 1.5f, 15f, 30f),
            width: Math.Clamp(projectileObject.Geometry.BoundingCircleRadius * 0.5f, 0.4f, 0.8f));
    }

    // Stream weapons (ProjectileStreamName, e.g. the Dragon Tank's flame) draw a textured
    // ribbon trailing the projectile instead of a tracer streak. The stream object definition
    // carries the rendering parameters in its W3dProjectileStreamDraw module.
    private void CreateProjectileStream(WeaponEffectExecutionContext context, GameObject projectileObject)
    {
        var streamName = ParentWeaponTemplate.ProjectileStreamName;
        if (string.IsNullOrEmpty(streamName))
        {
            return;
        }

        var streamDefinition = context.GameEngine.AssetStore.ObjectDefinitions.GetByName(streamName);
        var streamData = streamDefinition?.Draws.Values
            .Select(b => b.Data)
            .OfType<W3dProjectileStreamDrawModuleData>()
            .FirstOrDefault();
        if (streamData == null)
        {
            return;
        }

        var texture = context.GameEngine.AssetStore.Textures.GetByName(streamData.Texture)?.Texture;
        context.GameEngine.Scene3D?.ProjectileStreamManager.Create(projectileObject, streamData, texture);
    }
}
