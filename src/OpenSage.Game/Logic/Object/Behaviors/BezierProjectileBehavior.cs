using System.Linq;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.FX;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object;

public class BezierProjectileBehavior : UpdateModule
{
    private readonly BezierProjectileBehaviorData _moduleData;

    private ObjectId _launcherObjectId;
    private ObjectId _targetObjectId;
    private uint _unknown1;
    private readonly float[] _unknownFloats = new float[7];
    private string _weaponThatFiredThis;
    private uint _unknown2;

    internal FXList DetonationFX { get; set; }

    // The firing unit, assigned at spawn (see ProjectileNugget) so collision can skip it.
    internal ObjectId LauncherObjectId
    {
        get => _launcherObjectId;
        set => _launcherObjectId = value;
    }

    internal BezierProjectileBehavior(GameObject gameObject, IGameEngine gameEngine, BezierProjectileBehaviorData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;
    }

    public override UpdateSleepTime Update()
    {
        // TODO: Bezier implementation.
        if (!GameObject.IsProjectile)
        {
            // TODO: Use proper value.
            return UpdateSleepTime.None;
        }

        var direction = Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix);
        var velocity = direction * GameObject.Speed;

        // Speed is already in distance per logic frame (ParseVelocityToLogicFrames).
        GameObject.SetTranslation(GameObject.Translation + velocity);

        CheckForHit(
            GameObject,
            GameEngine,
            _moduleData.DetonateCallsKill,
            DetonationFX ?? _moduleData.GroundHitFX?.Value,
            _launcherObjectId);

        // TODO: Use proper value.
        return UpdateSleepTime.None;
    }

    internal static void CheckForHit(
        GameObject gameObject,
        IGameEngine gameEngine,
        bool detonateCallsKill,
        FXList groundHitFX,
        ObjectId launcherId)
    {
        // Object collision: a projectile detonates on the first object it reaches whose kind or
        // allegiance matches the warhead's ProjectileCollidesWith. Vehicles are always collided
        // with regardless of relationship. The swept sphere (bounding radius + this frame's
        // travel) prevents fast projectiles tunneling through small targets between frames.
        var hitObject = FindObjectCollision(gameObject, gameEngine, launcherId);
        if (hitObject != null)
        {
            Detonate(gameObject, gameEngine, new WeaponTarget(gameEngine.Scene3D.GameObjects, hitObject.Id), detonateCallsKill, groundHitFX);
            return;
        }

        // TODO: Replace the per-frame sphere overlap with a precise segment-vs-geometry test.

        if (DidHitGround(gameObject, gameEngine))
        {
            // If the projectile was fired at a specific object, preserve that target so that
            // DamageNugget.Execute applies damage to the object. WeaponTarget.DoDamage only
            // deals damage when the target type is Object; a position target does nothing.
            var currentTarget = gameObject.CurrentWeapon?.CurrentTarget;
            var detonationTarget = currentTarget?.TargetType == WeaponTargetType.Object
                ? currentTarget
                : new WeaponTarget(gameObject.Translation);

            Detonate(gameObject, gameEngine, detonationTarget, detonateCallsKill, groundHitFX);
        }
    }

    private static GameObject FindObjectCollision(GameObject gameObject, IGameEngine gameEngine, ObjectId launcherId)
    {
        var quadtree = gameEngine.Quadtree;
        if (quadtree == null)
        {
            return null;
        }

        var filter = gameObject.CurrentWeapon?.Template.ProjectileCollidesWith ?? WeaponCollideTypes.None;
        var radius = gameObject.Geometry.BoundingCircleRadius + DistancePerFrame(gameObject);

        return quadtree
            .FindIntersecting(new SphereCollider(gameObject.Transform, radius))
            .FirstOrDefault(o => o != gameObject
                && o.Id != launcherId
                && !o.IsEffectivelyDead
                && !o.IsProjectile
                && ShouldCollide(o, gameObject, filter));
    }

    // How far the projectile travels in one logic frame. Bezier projectiles move kinematically
    // by Speed (units per frame) with physics adding gravity; MissileAIUpdate missiles move
    // only through their physics velocity (Speed is not their actual travel rate).
    private static float DistancePerFrame(GameObject gameObject)
    {
        var physicsVelocity = gameObject.Physics?.Velocity ?? Vector3.Zero;
        if (gameObject.FindBehavior<BezierProjectileBehavior>() == null)
        {
            return physicsVelocity.Length();
        }

        var direction = Vector3.TransformNormal(Vector3.UnitX, gameObject.TransformMatrix);
        return (physicsVelocity + direction * gameObject.Speed).Length();
    }

    private static bool ShouldCollide(GameObject target, GameObject projectile, WeaponCollideTypes filter)
    {
        // Vehicles always stop a projectile, regardless of relationship.
        if (target.IsKindOf(ObjectKinds.Vehicle))
        {
            return true;
        }

        if (target.IsKindOf(ObjectKinds.Structure) && filter.HasFlag(WeaponCollideTypes.Structures))
        {
            return true;
        }

        if (target.IsKindOf(ObjectKinds.Shrubbery) && filter.HasFlag(WeaponCollideTypes.Shrubbery))
        {
            return true;
        }

        if ((target.IsKindOf(ObjectKinds.Wall) || target.IsKindOf(ObjectKinds.DefensiveWall))
            && filter.HasFlag(WeaponCollideTypes.Walls))
        {
            return true;
        }

        if (target.IsKindOf(ObjectKinds.Monster) && filter.HasFlag(WeaponCollideTypes.Monsters))
        {
            return true;
        }

        // Relationship tokens (Enemies/Allies/Neutral) match objects of any kind.
        return filter.HasFlag(RelationshipFlag(projectile.Owner, target.Owner));
    }

    private static WeaponCollideTypes RelationshipFlag(Player owner, Player targetOwner)
    {
        if (owner == null || targetOwner == null)
        {
            return WeaponCollideTypes.None;
        }

        if (owner == targetOwner || owner.Allies.Contains(targetOwner))
        {
            return WeaponCollideTypes.Allies;
        }

        if (owner.Enemies.Contains(targetOwner))
        {
            return WeaponCollideTypes.Enemies;
        }

        return WeaponCollideTypes.Neutral;
    }

    private static bool DidHitGround(GameObject gameObject, IGameEngine gameEngine)
    {
        var translation = gameObject.Translation;

        var terrainHeight = gameEngine.Terrain.HeightMap.GetHeight(
            translation.X,
            translation.Y);

        return translation.Z <= terrainHeight;
    }

    // Fires the projectile's warhead at its current target (or its own position when the
    // target is gone) and executes the detonation FX for position hits. Used by the
    // fuel-exhaustion detonation path.
    internal static void DeliverWarhead(GameObject gameObject, IGameEngine gameEngine, FXList detonationFX)
    {
        // If the projectile was fired at a specific object, preserve that target so that
        // DamageNugget.Execute applies damage to the object. WeaponTarget.DoDamage only
        // deals damage when the target type is Object; a position target does nothing.
        var currentTarget = gameObject.CurrentWeapon?.CurrentTarget;
        var detonationTarget = currentTarget?.TargetType == WeaponTargetType.Object
            ? currentTarget
            : new WeaponTarget(gameObject.Translation);

        FireWarhead(gameObject, gameEngine, detonationTarget, detonationFX);
    }

    private static void FireWarhead(GameObject gameObject, IGameEngine gameEngine, WeaponTarget target, FXList fx)
    {
        // TODO: Should this ever be null?
        if (gameObject.CurrentWeapon != null)
        {
            gameObject.CurrentWeapon.SetTarget(target);
            gameObject.CurrentWeapon.Fire();
        }

        if (target.TargetType == WeaponTargetType.Position)
        {
            fx?.Execute(new FXListExecutionContext(
                gameObject.Rotation,
                gameObject.Translation,
                gameEngine));
        }
    }

    private static void Detonate(
        GameObject gameObject,
        IGameEngine gameEngine,
        WeaponTarget target,
        bool detonateCallsKill,
        FXList groundHitFX)
    {
        FireWarhead(gameObject, gameEngine, target, groundHitFX);

        if (detonateCallsKill)
        {
            gameObject.Kill(deathType: DeathType.Detonated);
        }
        else
        {
            gameObject.Destroy();
        }
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistObjectId(ref _launcherObjectId);
        reader.PersistObjectId(ref _targetObjectId);
        reader.PersistUInt32(ref _unknown1);

        reader.PersistArray(
            _unknownFloats,
            static (StatePersister persister, ref float item) =>
            {
                persister.PersistSingleValue(ref item);
            });

        reader.PersistAsciiString(ref _weaponThatFiredThis);
        reader.PersistUInt32(ref _unknown2);
    }
}

[AddedIn(SageGame.Bfme)]
public class BezierProjectileBehaviorData : UpdateModuleData
{
    internal static BezierProjectileBehaviorData Parse(IniParser parser) => parser.ParseBlock(BezierProjectileFieldParseTable);

    internal static readonly IniParseTable<BezierProjectileBehaviorData> BezierProjectileFieldParseTable = new IniParseTable<BezierProjectileBehaviorData>
    {
        { "FirstHeight", (parser, x) => x.FirstHeight = parser.ParseInteger() },
        { "SecondHeight", (parser, x) => x.SecondHeight = parser.ParseInteger() },
        { "FirstPercentIndent", (parser, x) => x.FirstPercentIndent = parser.ParsePercentage() },
        { "SecondPercentIndent", (parser, x) => x.SecondPercentIndent = parser.ParsePercentage() },
        { "TumbleRandomly", (parser, x) => x.TumbleRandomly = parser.ParseBoolean() },
        { "CrushStyle", (parser, x) => x.CrushStyle = parser.ParseBoolean() },
        { "DieOnImpact", (parser, x) => x.DieOnImpact = parser.ParseBoolean() },
        { "BounceCount", (parser, x) => x.BounceCount = parser.ParseInteger() },
        { "BounceDistance", (parser, x) => x.BounceDistance = parser.ParseInteger() },
        { "BounceFirstHeight", (parser, x) => x.BounceFirstHeight = parser.ParseInteger() },
        { "BounceSecondHeight", (parser, x) => x.BounceSecondHeight = parser.ParseInteger() },
        { "BounceFirstPercentIndent", (parser, x) => x.BounceFirstPercentIndent = parser.ParsePercentage() },
        { "BounceSecondPercentIndent", (parser, x) => x.BounceSecondPercentIndet = parser.ParsePercentage() },
        { "GroundHitFX", (parser, x) => x.GroundHitFX = parser.ParseFXListReference() },
        { "GroundHitWeapon", (parser, x) => x.GroundHitWeapon = parser.ParseAssetReference() },
        { "GroundBounceFX", (parser, x) => x.GroundBounceFX = parser.ParseAssetReference() },
        { "GroundBounceWeapon", (parser, x) => x.GroundBounceWeapon = parser.ParseAssetReference() },
        { "DetonateCallsKill", (parser, x) => x.DetonateCallsKill = parser.ParseBoolean() },
        { "FlightPathAdjustDistPerSecond", (parser, x) => x.FlightPathAdjustDistPerSecond = parser.ParseInteger() },
        { "CurveFlattenMinDist", (parser, x) => x.CurveFlattenMinDist = parser.ParseFloat() },
        { "InvisibleFrames", (parser, x) => x.InvisibleFrames = parser.ParseInteger() },
        { "PreLandingStateTime", (parser, x) => x.PreLandingStateTime = parser.ParseInteger() },
        { "PreLandingEmotion", (parser, x) => x.PreLandingEmotion = parser.ParseEnum<EmotionType>() },
        { "PreLandingEmotionRadius", (parser, x) => x.PreLandingEmotionRadius = parser.ParseFloat() },
        { "FadeInTime", (parser, x) => x.FadeInTime = parser.ParseInteger() },
        { "IgnoreTerrainHeight", (parser, x) => x.IgnoreTerrainHeight = parser.ParseBoolean() },
        { "FirstPercentHeight", (parser, x) => x.FirstPercentHeight = parser.ParsePercentage() },
        { "SecondPercentHeight", (parser, x) => x.SecondPercentHeight = parser.ParsePercentage() },
        { "FinalStuckTime", (parser, x) => x.FinalStuckTime = parser.ParseInteger() },
        { "OrientToFlightPath", (parser, x) => x.OrientToFlightPath = parser.ParseBoolean() },
        { "GarrisonHitKillRequiredKindOf", (parser, x) => x.GarrisonHitKillRequiredKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillForbiddenKindOf", (parser, x) => x.GarrisonHitKillForbiddenKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillCount", (parser, x) => x.GarrisonHitKillCount = parser.ParseInteger() },
        { "GarrisonHitKillFX", (parser, x) => x.GarrisonHitKillFX = parser.ParseAssetReference() },
        { "PreLandingEmotionAffectsAllies", (parser, x) => x.PreLandingEmotionAffectsAllies = parser.ParseBoolean() },
    };

    public int FirstHeight { get; private set; }
    public int SecondHeight { get; private set; }
    public Percentage FirstPercentIndent { get; private set; }
    public Percentage SecondPercentIndent { get; private set; }
    public bool TumbleRandomly { get; private set; }

    public bool CrushStyle { get; private set; }
    public bool DieOnImpact { get; private set; }
    public int BounceCount { get; private set; }
    public int BounceDistance { get; private set; }
    public int BounceFirstHeight { get; private set; }
    public int BounceSecondHeight { get; private set; }
    public Percentage BounceFirstPercentIndent { get; private set; }
    public Percentage BounceSecondPercentIndet { get; private set; }
    public LazyAssetReference<FXList> GroundHitFX { get; private set; }
    public string GroundHitWeapon { get; private set; }
    public string GroundBounceFX { get; private set; }
    public string GroundBounceWeapon { get; private set; }
    public bool DetonateCallsKill { get; private set; }
    public int FlightPathAdjustDistPerSecond { get; private set; }
    public float CurveFlattenMinDist { get; private set; }
    public int InvisibleFrames { get; private set; }
    public int PreLandingStateTime { get; private set; }
    public EmotionType PreLandingEmotion { get; private set; }
    public float PreLandingEmotionRadius { get; private set; }
    public int FadeInTime { get; private set; }
    public bool IgnoreTerrainHeight { get; private set; }
    public Percentage FirstPercentHeight { get; private set; }
    public Percentage SecondPercentHeight { get; private set; }
    public int FinalStuckTime { get; private set; }
    public bool OrientToFlightPath { get; private set; }
    public ObjectKinds GarrisonHitKillRequiredKindOf { get; private set; }
    public ObjectKinds GarrisonHitKillForbiddenKindOf { get; private set; }
    public int GarrisonHitKillCount { get; private set; }
    public string GarrisonHitKillFX { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool PreLandingEmotionAffectsAllies { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new BezierProjectileBehavior(gameObject, gameEngine, this);
    }
}
