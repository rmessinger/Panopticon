using System;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.FX;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object;

public sealed class MissileAIUpdate : AIUpdate
{
    internal override MissileAIUpdateModuleData ModuleData { get; }

    private MissileState _state;
    private LogicFrame _nextStateChangeTime;
    private LogicFrame _fuelExhaustionFrame;

    private Vector3 _unknownPosition;
    private uint _stateMaybe;
    private uint _unknownFrame1;
    private ObjectId _launcherObjectId;
    private ObjectId _unknownObjectId;
    private bool _unknownBool1;
    private uint _unknownFrame2;
    private float _unknownFloat1;
    private WeaponTemplate _weaponTemplate;
    private FXParticleSystemTemplate _exhaustParticleSystemTemplate;
    private bool _unknownBool2;
    private Vector3 _currentPositionMaybe;
    private int _unknownInt2;
    private int _unknownInt3;

    internal FXList DetonationFX { get; set; }

    // How far ahead on the parabola the steering point sits; bounds corner-cutting without
    // smoothing away the arc. Clamped to the trajectory range inside SteerPoint.
    private const float ParabolicLookaheadDistance = 80f;

    // Parabolic boost distance: just enough for the tail to clear the erector rail. The INI's
    // DistanceToTravelBeforeTurning (200 for the SCUD) is a legacy boost length that flies
    // far past the rail before any steering starts.
    private const float RailClearanceDistance = 40f;

    // Pitch-over easing: while the nose is more than this far (degrees) from the steer
    // heading, orientation slews at the capped rate below instead of the template's full
    // 540 deg/sec, so the nose eases onto the arc like a gravity turn.
    private const float ParabolicPitchOverRatePerSecond = 120f;
    private const float ParabolicPitchOverReleaseAngle = 15f;

    // Below this speed the velocity direction is too noisy to orient the model by.
    private const float VelocityOrientationMinSpeed = 40f;

    /// <summary>
    /// Set by <see cref="MissileAIUpdateModuleData.CreateModule"/>: fly the launch-computed
    /// parabola (see <see cref="ParabolicTrajectory"/>) instead of homing directly. The SCUD
    /// missile and rocket buggy missiles opt in by definition name because their INIs live
    /// in the read-only install.
    /// </summary>
    internal bool ParabolicSteeringEnabled { get; set; }

    private ParabolicTrajectory? _parabola;

    // Rocket-motor exhaust (the weapon's ProjectileExhaust system): starts at ignition,
    // follows the model's tail, finishes on fuel-out or death. SystemLifetime is 0 for these
    // templates, so the system never stops emitting on its own.
    private FXParticleSystemTemplate? _exhaustTemplate;
    private Vector3 _exhaustTailOffset;
    private ParticleSystem? _exhaustSystem;
    private Matrix4x4 _exhaustMatrix;

    internal void AttachExhaust(FXParticleSystemTemplate template, Vector3 tailOffset)
    {
        _exhaustTemplate = template;
        _exhaustTailOffset = tailOffset;
    }

    private void StartExhaust()
    {
        if (_exhaustTemplate == null || _exhaustSystem != null)
        {
            return;
        }

        UpdateExhaustMatrix();
        _exhaustSystem = GameEngine.ParticleSystems.Create(_exhaustTemplate, () => ref _exhaustMatrix);
    }

    private void UpdateExhaustMatrix()
    {
        _exhaustMatrix = Matrix4x4.CreateTranslation(_exhaustTailOffset) * GameObject.TransformMatrix;
    }

    private void FinishExhaust()
    {
        _exhaustSystem?.Finish();
        _exhaustSystem = null;
    }

    // The firing unit, assigned at spawn (see ProjectileNugget) so collision can skip it.
    internal ObjectId LauncherObjectId
    {
        get => _launcherObjectId;
        set => _launcherObjectId = value;
    }

    internal MissileAIUpdate(GameObject gameObject, IGameEngine gameEngine, MissileAIUpdateModuleData moduleData)
        : base(gameObject, gameEngine, moduleData)
    {
        ModuleData = moduleData;

        _state = MissileState.Inactive;
    }

    public override UpdateSleepTime Update()
    {
        var currentFrame = GameEngine.GameLogic.CurrentFrame;

        switch (_state)
        {
            case MissileState.Inactive:
                _nextStateChangeTime = currentFrame + ModuleData.IgnitionDelay;
                _state = MissileState.WaitingForIgnition;
                goto case MissileState.WaitingForIgnition;

            case MissileState.WaitingForIgnition:
                if (currentFrame >= _nextStateChangeTime)
                {
                    ModuleData.IgnitionFX?.Value?.Execute(
                        new FXListExecutionContext(
                            GameObject.Rotation,
                            GameObject.Translation,
                            GameEngine));

                    if (ModuleData.DistanceToTravelBeforeTurning > 0)
                    {
                        var boostDistance = ParabolicSteeringEnabled
                            ? MathF.Min(ModuleData.DistanceToTravelBeforeTurning, RailClearanceDistance)
                            : ModuleData.DistanceToTravelBeforeTurning;
                        var pointToReachBeforeTurning = GameObject.Translation
                            + Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix) * boostDistance;
                        AddTargetPoint(pointToReachBeforeTurning);
                    }

                    // The calculated arc is frozen at ignition: launch point, impact point,
                    // and apex height all come from this moment; the boost waypoint above is
                    // the only queued target point.
                    // If the target died before the missile finished igniting, there's nowhere
                    // new to aim; keep whatever target point was already queued.
                    var currentTarget = GameObject.CurrentWeapon.CurrentTarget;
                    if (currentTarget != null && !currentTarget.IsDestroyed)
                    {
                        if (ParabolicSteeringEnabled)
                        {
                            // Pod-launched rockets (short boost) fly the arc whose launch tangent
                            // is the battery's current muzzle angle: they leave the launcher
                            // along the pod and loft over onto the target. Rail-launched
                            // missiles use the lofted factor arc and climb the rail first.
                            _parabola = ModuleData.DistanceToTravelBeforeTurning < RailClearanceDistance
                                ? ParabolicTrajectory.FromLaunchDirection(
                                    GameObject.Translation,
                                    currentTarget.TargetPosition,
                                    Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix))
                                : ParabolicTrajectory.FromApexHeightFactor(
                                    GameObject.Translation,
                                    currentTarget.TargetPosition,
                                    ModuleData.ParabolicApexHeightFactor);
                        }
                        else
                        {
                            AddTargetPoint(currentTarget.TargetPosition);
                        }
                    }

                    // Ignition impulse along the muzzle direction. ApplyForce adds mass * deltaV
                    // to the acceleration, which physics integrates into velocity exactly once
                    // (acceleration is reset at the end of each physics update).
                    if (GameObject.Physics is { } physics)
                    {
                        var forward = Vector3.TransformNormal(Vector3.UnitX, GameObject.TransformMatrix);
                        physics.ApplyForce(forward * (physics.Mass * ModuleData.InitialVelocity));
                    }

                    _fuelExhaustionFrame = currentFrame + ModuleData.FuelLifetime;

                    StartExhaust();

                    _state = MissileState.Moving;
                }

                break;

            case MissileState.Moving:
                // The booster burn terminates at the arc's apex: past the parabola's midpoint
                // the flight is unpowered descent, so the motor (and its plume) shuts off.
                if (_exhaustSystem != null
                    && _parabola is { } parabola
                    && parabola.Progress(GameObject.Translation) >= parabola.Range * 0.5f)
                {
                    FinishExhaust();
                }

                UpdateExhaustMatrix();
                SteerTowardTargetPoints(currentFrame);
                // TODO: DistanceToTargetBeforeDiving
                BezierProjectileBehavior.CheckForHit(GameObject, GameEngine, ModuleData.DetonateCallsKill, DetonationFX, groundHitFX: null, _launcherObjectId);
                if (GameObject.IsEffectivelyDead)
                {
                    // The warhead delivered: the motor stops with the missile.
                    FinishExhaust();
                }

                break;

            default:
                throw new InvalidOperationException();
        }

        // TODO(Port): Use correct value.
        return UpdateSleepTime.None;
    }

    private void SteerTowardTargetPoints(LogicFrame currentFrame)
    {
        var parabolic = _parabola.HasValue;

        // Keep homing on a moving target: the final target point tracks its live position.
        // Parabolic flight freezes its impact point at ignition, so there is nothing to track.
        if (!parabolic
            && ModuleData.TryToFollowTarget
            && TargetPoints.Count > 0
            && GameObject.CurrentWeapon?.CurrentTarget is { } target
            && !target.IsDestroyed)
        {
            TargetPoints[^1] = target.TargetPosition;
        }

        // Terminal dive: once within DistanceToTargetBeforeDiving, the remaining flight
        // points straight at the target. Cruising locomotors hold a PreferredHeight cruise
        // altitude (the SCUD missile's is 120); the dive must also disable that hold so the
        // goal's actual Z is flown. Parabolic flight skips the dive (the curve already
        // terminates at its impact point) but keeps the height hold disabled so the curve's
        // Z is flown directly.
        // The proximity check is 2D: the original engine "has a 2D heart, being over the
        // target counts as there" (TomahawkMissile INI). A 3D check could never trigger for a
        // missile cruising above the dive radius (Tomahawk: altitude 120 vs radius 100), so it
        // would overfly the target and cruise on.
        var liveTarget = GameObject.CurrentWeapon?.CurrentTarget;
        var diving = !parabolic
            && ModuleData.DistanceToTargetBeforeDiving > 0
            && liveTarget != null
            && !liveTarget.IsDestroyed
            && (liveTarget.TargetPosition - GameObject.Translation).Vector2XY().Length()
                < ModuleData.DistanceToTargetBeforeDiving;
        if (diving)
        {
            TargetPoints.Clear();
            AddTargetPoint(liveTarget.TargetPosition);
        }

        CurrentLocomotor?.SetPreciseZPosition(diving || parabolic);

        // Out of fuel: thrust stops and gravity takes over, unless configured to detonate.
        if (currentFrame >= _fuelExhaustionFrame)
        {
            FinishExhaust();

            if (ModuleData.DetonateOnNoFuel)
            {
                // Detonating means the warhead is delivered where the missile dies, not just
                // a death animation: fire it at the target before the kill.
                BezierProjectileBehavior.DeliverWarhead(GameObject, GameEngine, DetonationFX);
                GameObject.Kill(deathType: DeathType.Detonated);
            }
            else
            {
                GameObject.Physics?.RequestOrientToVelocity();
            }

            return;
        }

        // The missile's THRUST locomotor does the steering: it accelerates along its forward
        // vector toward the target point (bounded by MaxThrustAngle), compensating for the
        // gravity physics applies. This mirrors AIUpdate.Update's movement block, minus the
        // infantry braking/waypoint behavior missiles don't use.
        var locomotor = CurrentLocomotor;
        if (locomotor == null || (TargetPoints.Count == 0 && !parabolic))
        {
            return;
        }

        // Consume intermediate waypoints that were reached or overshot: a missile moving
        // several units per frame can cross a nearby waypoint mid-step and never enter its
        // arrival radius, which would otherwise leave it trying to U-turn back toward the
        // point behind it. The FINAL point is exempt: it is the target itself, and a missile
        // that flew past it (e.g. lofted above) must still home back onto it, not discard it
        // and cruise away ballistic. Parabolic flight queues only the boost point, which is
        // intermediate: once passed, the parabola takes over as the steering reference.
        var closeEnoughDist = locomotor.LocomotorTemplate.CloseEnoughDist;
        var velocity = GameObject.Physics?.Velocity ?? Vector3.Zero;
        var maxQueuedPoints = parabolic ? 1 : 2;
        while (TargetPoints.Count >= maxQueuedPoints)
        {
            var toPoint = TargetPoints[0] - GameObject.Translation;
            if (toPoint.LengthSquared() < closeEnoughDist * closeEnoughDist
                || (velocity != Vector3.Zero && Vector3.Dot(toPoint, velocity) < 0))
            {
                TargetPoints.RemoveAt(0);
            }
            else
            {
                break;
            }
        }

        if (TargetPoints.Count == 0 && !parabolic)
        {
            return;
        }

        locomotor.SetPhysicsOptions(GameObject);

        // Until the boost waypoint is consumed, steer at it as before; after that, the
        // parabola is the steering reference: aim at the curve point ahead of the missile.
        // On short-range arcs the lookahead shrinks proportionally: a fixed 80 units on a
        // ~180-unit arc sits near mid-curve, whose direction is far shallower than the
        // launch tangent, and the nose would pitch over immediately instead of climbing.
        var steerPoint = TargetPoints.Count > 0
            ? TargetPoints[0]
            : _parabola!.Value.SteerPoint(
                GameObject.Translation,
                MathF.Min(ParabolicLookaheadDistance, _parabola.Value.Range * 0.35f));

        // TODO(Port): Use correct parameter values.
        var blocked = false;
        locomotor.LocoUpdateMoveTowardsPosition(
            GameObject,
            steerPoint,
            (steerPoint - GameObject.Translation).Vector2XY().Length(),
            locomotor.GetMaxSpeedForCondition(GameObject.BodyModule.DamageState),
            ref blocked);

        if (parabolic)
        {
            EaseOntoTrajectory(locomotor, steerPoint);
        }
    }

    /// <summary>
    /// Nose handling for parabolic flight. While the nose is far from the steer heading (the
    /// pitch-over after the rail clears), cap the orientation rate so it eases over instead of
    /// whipping at the template's full turn rate; thrust is bounded by the nose, so the path
    /// gentles too. Once converged and moving, lock the model to the flight path exactly: the
    /// velocity vector changes continuously, so orientation riding it is smooth by construction.
    /// </summary>
    private void EaseOntoTrajectory(Locomotor locomotor, Vector3 steerPoint)
    {
        var forward = GameObject.TransformMatrix.GetXVector();
        var toSteer = steerPoint - GameObject.Translation;

        var pitchOver = true;
        if (MathF.Abs(forward.LengthSquared()) > 0.0001f && toSteer.LengthSquared() > 0.0001f)
        {
            var headingError = MathF.Acos(Math.Clamp(
                Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(toSteer)),
                -1f, 1f));
            pitchOver = MathUtility.ToDegrees(headingError) > ParabolicPitchOverReleaseAngle;
        }

        locomotor.ThrustTurnRateLimitPerFrame = pitchOver
            ? ParabolicPitchOverRatePerSecond / GameEngine.LogicFramesPerSecond
            : null;

        if (!pitchOver && GameObject.Physics is { } physics && physics.VelocityMagnitude > VelocityOrientationMinSpeed)
        {
            physics.RequestOrientToVelocity();
        }
    }

    internal override void Load(StatePersister reader)
    {
        var version = reader.PersistVersion(6);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistVector3(ref _unknownPosition);
        reader.PersistUInt32(ref _stateMaybe);
        reader.PersistFrame(ref _unknownFrame1);

        var unknownInt1 = int.MaxValue;
        reader.PersistInt32(ref unknownInt1);
        if (unknownInt1 != int.MaxValue)
        {
            throw new InvalidStateException();
        }

        reader.PersistObjectId(ref _launcherObjectId);
        reader.PersistObjectId(ref _unknownObjectId);
        reader.PersistBoolean(ref _unknownBool1);
        reader.PersistFrame(ref _unknownFrame2);
        reader.PersistSingle(ref _unknownFloat1);

        var unknownFloat2 = 99999.0f;
        reader.PersistSingle(ref unknownFloat2);
        if (unknownFloat2 != 99999.0f)
        {
            throw new InvalidStateException();
        }

        var weaponTemplateName = _weaponTemplate?.Name;
        reader.PersistAsciiString(ref weaponTemplateName);
        _weaponTemplate = reader.AssetStore.WeaponTemplates.GetByName(weaponTemplateName);

        var exhaustParticleSystemTemplateName = _exhaustParticleSystemTemplate?.Name;
        reader.PersistAsciiString(ref exhaustParticleSystemTemplateName);
        if (reader.Mode == StatePersistMode.Read)
        {
            _exhaustParticleSystemTemplate = reader.AssetStore.FXParticleSystemTemplates.GetByName(exhaustParticleSystemTemplateName);
        }

        reader.PersistBoolean(ref _unknownBool2);
        reader.PersistVector3(ref _currentPositionMaybe);
        reader.PersistInt32(ref _unknownInt2); // 0, 0x20000
        reader.PersistInt32(ref _unknownInt3); // 1960

        if (version >= 6)
        {
            reader.SkipUnknownBytes(6);
        }
    }

    private enum MissileState
    {
        Inactive,
        WaitingForIgnition,
        Moving,
    }
}

public sealed class MissileAIUpdateModuleData : AIUpdateModuleData
{
    internal new static MissileAIUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private new static readonly IniParseTable<MissileAIUpdateModuleData> FieldParseTable = AIUpdateModuleData.FieldParseTable.Concat(new IniParseTable<MissileAIUpdateModuleData>
    {
        { "TryToFollowTarget", (parser, x) => x.TryToFollowTarget = parser.ParseBoolean() },
        { "FuelLifetime", (parser, x) => x.FuelLifetime = parser.ParseTimeMillisecondsToLogicFrames() },
        { "DetonateOnNoFuel", (parser, x) => x.DetonateOnNoFuel = parser.ParseBoolean() },
        { "InitialVelocity", (parser, x) => x.InitialVelocity = parser.ParseVelocityToLogicFrames() },
        { "IgnitionDelay", (parser, x) => x.IgnitionDelay = parser.ParseTimeMillisecondsToLogicFrames() },
        { "DistanceToTravelBeforeTurning", (parser, x) => x.DistanceToTravelBeforeTurning = parser.ParseInteger() },
        { "DistanceToTargetBeforeDiving", (parser, x) => x.DistanceToTargetBeforeDiving = parser.ParseInteger() },
        { "DistanceToTargetForLock", (parser, x) => x.DistanceToTargetForLock = parser.ParseInteger() },
        { "GarrisonHitKillRequiredKindOf", (parser, x) => x.GarrisonHitKillRequiredKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillForbiddenKindOf", (parser, x) => x.GarrisonHitKillForbiddenKindOf = parser.ParseEnum<ObjectKinds>() },
        { "GarrisonHitKillCount", (parser, x) => x.GarrisonHitKillCount = parser.ParseInteger() },
        { "GarrisonHitKillFX", (parser, x) => x.GarrisonHitKillFX = parser.ParseFXListReference() },
        { "DetonateCallsKill", (parser, x) => x.DetonateCallsKill = parser.ParseBoolean() },
        { "IgnitionFX", (parser, x) => x.IgnitionFX = parser.ParseFXListReference() },
        { "KillSelfDelay", (parser, x) => x.KillSelfDelay = parser.ParseInteger() },
        { "DistanceScatterWhenJammed", (parser, x) => x.DistanceScatterWhenJammed = parser.ParseInteger() },

        // OpenSAGE extensions (not vanilla SAGE fields).
        { "ParabolicSteering", (parser, x) => x.ParabolicSteering = parser.ParseBoolean() },
        { "ParabolicApexHeightFactor", (parser, x) => x.ParabolicApexHeightFactor = parser.ParseFloat() },
    });

    public bool TryToFollowTarget { get; private set; }
    public LogicFrameSpan FuelLifetime { get; private set; }
    public bool DetonateOnNoFuel { get; private set; }
    public float InitialVelocity { get; private set; }
    public LogicFrameSpan IgnitionDelay { get; private set; }
    public int DistanceToTravelBeforeTurning { get; private set; }
    public int DistanceToTargetBeforeDiving { get; private set; }
    public int DistanceToTargetForLock { get; private set; }
    public ObjectKinds GarrisonHitKillRequiredKindOf { get; private set; }
    public ObjectKinds GarrisonHitKillForbiddenKindOf { get; private set; }
    public int GarrisonHitKillCount { get; private set; }
    public LazyAssetReference<FXList> GarrisonHitKillFX { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public bool DetonateCallsKill { get; private set; }
    public LazyAssetReference<FXList> IgnitionFX { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public int KillSelfDelay { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public int DistanceScatterWhenJammed { get; private set; }

    /// <summary>
    /// OpenSAGE extension: fly a launch-computed parabola (see <see cref="ParabolicTrajectory"/>)
    /// instead of homing directly at the target.
    /// </summary>
    public bool ParabolicSteering { get; private set; }

    /// <summary>OpenSAGE extension: apex height as a multiple of the horizontal launch-to-impact distance.</summary>
    public float ParabolicApexHeightFactor { get; private set; } = 1f;

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        // The calculated arcs are engine behavior: these projectiles' INIs live inside the
        // read-only game install, so the opt-in is keyed on the definition names here.
        return new MissileAIUpdate(gameObject, gameEngine, this)
        {
            ParabolicSteeringEnabled = ParabolicSteering
                || gameObject.Definition.Name == "SCUDMissile"
                || gameObject.Definition.Name == "RocketBuggyMissile",
        };
    }
}
