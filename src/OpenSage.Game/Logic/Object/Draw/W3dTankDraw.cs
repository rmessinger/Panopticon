using System;
using System.Numerics;
using OpenSage.Client;
using OpenSage.Content;
using OpenSage.Data.Ini;
using OpenSage.Graphics;
using OpenSage.Graphics.ParticleSystems;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object;

public class W3dTankDraw : W3dModelDraw
{
    private readonly W3dTankDrawModuleData _data;
    private readonly FXParticleSystemTemplate _treadDebrisLeft;
    private readonly FXParticleSystemTemplate _treadDebrisRight;

    // Tread meshes follow the vanilla naming convention TREADS[L|R]nn (e.g. TREADSL01);
    // TREADFX* meshes are effect geometry and never scroll.
    private const string TreadMeshPrefix = "TREADS";

    private ModelInstance _treadModelInstance;
    private ModelMeshInstance _treadMeshLeft;
    private ModelMeshInstance _treadMeshRight;
    private float _treadOffsetLeft;
    private float _treadOffsetRight;
    private float _lastYaw;
    private bool _hasLastYaw;
    private uint _lastFrame;
    private bool _hasLastFrame;

    internal W3dTankDraw(W3dTankDrawModuleData data, Drawable drawable, IGameEngine gameEngine)
        : base(data, drawable, gameEngine)
    {
        _data = data;
        _treadDebrisLeft = data.TreadDebrisLeft?.Value ?? gameEngine.AssetLoadContext.AssetStore.FXParticleSystemTemplates.GetByName("TrackDebrisDirtLeft");
        _treadDebrisRight = data.TreadDebrisRight?.Value ?? gameEngine.AssetLoadContext.AssetStore.FXParticleSystemTemplates.GetByName("TrackDebrisDirtRight");
    }

    internal override void Update(in TimeInterval gameTime)
    {
        base.Update(gameTime);

        UpdateTreadAnimation(gameTime);

        // Turret yaw/pitch is applied by W3dModelDraw.Update from the condition state's
        // Turret/TurretPitch bones. TODO: Tread debris.
    }

    private void UpdateTreadAnimation(in TimeInterval gameTime)
    {
        if (_data.TreadAnimationRate <= 0f)
        {
            return;
        }

        var modelInstance = ActiveModelInstance;
        if (modelInstance == null)
        {
            return;
        }

        if (_treadModelInstance != modelInstance)
        {
            _treadModelInstance = modelInstance;
            _treadMeshLeft = null;
            _treadMeshRight = null;
            foreach (var (name, meshInstance) in modelInstance.NamedMeshInstances)
            {
                if (!name.StartsWith(TreadMeshPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var side = name.Length > TreadMeshPrefix.Length ? name[TreadMeshPrefix.Length] : '\0';
                if (side is 'L' or 'l')
                {
                    _treadMeshLeft = meshInstance;
                }
                else if (side is 'R' or 'r')
                {
                    _treadMeshRight = meshInstance;
                }
            }

            // The tread materials bake a time-based UV scroll into the asset (linear-offset
            // mapper); vanilla suppresses it and drives the texture by drive speed instead.
            _treadMeshLeft?.SetUvOffset(Vector2.Zero, suppressBakedTextureScroll: true);
            _treadMeshRight?.SetUvOffset(Vector2.Zero, suppressBakedTextureScroll: true);
        }

        if (_treadMeshLeft == null && _treadMeshRight == null)
        {
            return;
        }

        var gameObject = GameObject;

        // Integrate against logic frames, not render time: render updates fire at arbitrary
        // rates (headless capture renders a handful of frames per logic second), so wall-clock
        // dt would accumulate the wrong total.
        var currentFrame = _gameEngine.GameLogic.CurrentFrame.Value;
        var logicSeconds = 0f;
        if (_hasLastFrame)
        {
            logicSeconds = (currentFrame - _lastFrame) / _gameEngine.LogicFramesPerSecond;
        }

        _lastFrame = currentFrame;
        _hasLastFrame = true;

        if (logicSeconds <= 0f)
        {
            return;
        }

        var velocity = gameObject?.FindBehavior<PhysicsBehavior>()?.Velocity ?? Vector3.Zero;
        var yaw = gameObject?.Yaw ?? 0f;
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);

        // Velocity and locomotor speeds are per logic frame; the tread offset integrates in
        // texture units per second, so only their ratio (0..1) feeds the rate.
        var locomotor = gameObject?.AIUpdate?.CurrentLocomotor;
        var maxSpeed = locomotor?.GetMaxSpeedForCondition(gameObject.BodyModule.DamageState) ?? 0f;
        var forwardSpeed = Vector3.Dot(velocity, forward);
        var driveRatio = maxSpeed > 0f
            ? Math.Clamp(MathF.Abs(forwardSpeed) / maxSpeed, 0f, 1f)
            : 0f;
        var drive = _data.TreadAnimationRate
            * (_data.TreadDriveSpeedFraction > 0f ? _data.TreadDriveSpeedFraction : 1f)
            * driveRatio
            * MathF.Sign(forwardSpeed);

        var yawRate = 0f;
        if (_hasLastYaw)
        {
            yawRate = MathUtility.CalculateAngleDelta(yaw, _lastYaw) / logicSeconds;
        }

        _lastYaw = yaw;
        _hasLastYaw = true;

        var maxTurnRateRadPerSecond = locomotor != null
            ? MathUtility.ToRadians(locomotor.GetMaxTurnRate(gameObject.BodyModule.DamageState))
                * _gameEngine.LogicFramesPerSecond
            : 0f;
        var pivotRatio = maxTurnRateRadPerSecond > 0f
            ? Math.Clamp(MathF.Abs(yawRate) / maxTurnRateRadPerSecond, 0f, 1f)
            : 0f;
        var pivot = _data.TreadAnimationRate
            * (_data.TreadPivotSpeedFraction > 0f ? _data.TreadPivotSpeedFraction : 0.8f)
            * pivotRatio
            * MathF.Sign(yawRate);

        _treadOffsetLeft += (drive + pivot) * logicSeconds;
        _treadOffsetRight += (drive - pivot) * logicSeconds;

        _treadMeshLeft?.SetUvOffset(new Vector2(_treadOffsetLeft, 0f), suppressBakedTextureScroll: true);
        _treadMeshRight?.SetUvOffset(new Vector2(_treadOffsetRight, 0f), suppressBakedTextureScroll: true);
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();
    }
}

/// <summary>
/// Default Draw used by tanks. Hardcoded to call for the TrackDebrisDirtRight and
/// TrackDebrisDirtLeft particle system definitions.
/// </summary>
public class W3dTankDrawModuleData : W3dModelDrawModuleData
{
    internal static W3dTankDrawModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    internal static new readonly IniParseTable<W3dTankDrawModuleData> FieldParseTable = W3dModelDrawModuleData.FieldParseTable
        .Concat(new IniParseTable<W3dTankDrawModuleData>
        {
            { "TreadDebrisLeft", (parser, x) => x.TreadDebrisLeft = parser.ParseFXParticleSystemTemplateReference() },
            { "TreadDebrisRight", (parser, x) => x.TreadDebrisRight = parser.ParseFXParticleSystemTemplateReference() },

            { "TreadAnimationRate", (parser, x) => x.TreadAnimationRate = parser.ParseFloat() },
            { "TreadDriveSpeedFraction", (parser, x) => x.TreadDriveSpeedFraction = parser.ParseFloat() },
            { "TreadPivotSpeedFraction", (parser, x) => x.TreadPivotSpeedFraction = parser.ParseFloat() },
        });

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LazyAssetReference<FXParticleSystemTemplate> TreadDebrisLeft { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LazyAssetReference<FXParticleSystemTemplate> TreadDebrisRight { get; private set; }

    /// <summary>
    /// How fast to move the tread texture, in texture units per second.
    /// </summary>
    public float TreadAnimationRate { get; private set; }

    public float TreadDriveSpeedFraction { get; private set; }
    public float TreadPivotSpeedFraction { get; private set; }

    internal override DrawModule CreateDrawModule(Drawable drawable, IGameEngine gameEngine)
    {
        return new W3dTankDraw(this, drawable, gameEngine);
    }
}
