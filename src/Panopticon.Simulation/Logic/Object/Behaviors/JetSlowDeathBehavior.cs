using System;
using System.Numerics;
using Panopticon.FX;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

public sealed class JetSlowDeathBehavior : SlowDeathBehavior
{
    private readonly JetSlowDeathBehaviorModuleData _moduleData;
    private bool _dying;
    private bool _hitGround;
    private bool _secondaryExecuted;
    private Vector3 _velocity;
    private LogicFrame _secondaryTime;
    private LogicFrame _destructionTime;
    private float _rollRate;

    internal JetSlowDeathBehavior(GameObject gameObject, IGameEngine gameEngine, JetSlowDeathBehaviorModuleData moduleData)
        : base(gameObject, gameEngine, moduleData)
    {
        _moduleData = moduleData;
    }

    public override void OnDie(in DamageInfoInput damageInput)
    {
        if (_dying || !IsDieApplicable(damageInput))
        {
            return;
        }

        _dying = true;
        _velocity = GameObject.Physics?.Velocity ?? Vector3.Zero;
        _rollRate = _moduleData.RollRate;
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.Dying, true);
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.JetExhaust, false);
        GameObject.ModelConditionFlags.Set(ModelConditionFlag.JetAfterburner, false);

        if (!GameObject.IsAirborne())
        {
            ExecuteEffects(_moduleData.FXOnGroundDeath, _moduleData.OCLOnGroundDeath);
            GameObject.Destroy();
            return;
        }

        ExecuteEffects(_moduleData.FXInitialDeath, _moduleData.OCLInitialDeath);
        _secondaryTime = GameEngine.GameLogic.CurrentFrame + Delay(_moduleData.DelaySecondaryFromInitialDeath);
    }

    public override UpdateSleepTime Update()
    {
        if (!_dying)
        {
            return UpdateSleepTime.None;
        }

        var frame = GameEngine.GameLogic.CurrentFrame;
        if (_hitGround)
        {
            if (frame >= _destructionTime)
            {
                ExecuteEffects(_moduleData.FXFinalBlowUp, _moduleData.OCLFinalBlowUp);
                GameObject.Destroy();
            }

            return UpdateSleepTime.None;
        }

        if (!_secondaryExecuted && frame >= _secondaryTime)
        {
            _secondaryExecuted = true;
            ExecuteEffects(_moduleData.FXSecondary, _moduleData.OCLSecondary);
        }

        // Normal physics and flight AI stop ticking at zero health. The crash owns
        // movement until impact, preserving forward momentum without cruise-height hold.
        var fallFactor = MathF.Max((float)_moduleData.FallHowFast, 0.01f);
        _velocity.Z -= MathF.Abs(GameEngine.AssetStore.GameData.Current.Gravity) * fallFactor;
        var position = GameObject.Translation + _velocity;
        var groundHeight = GameEngine.Terrain.HeightMap.GetHeight(position.X, position.Y);
        if (position.Z <= groundHeight)
        {
            position.Z = groundHeight;
            GameObject.SetTranslation(position);
            _hitGround = true;
            _destructionTime = frame + Delay(_moduleData.DelayFinalBlowUpFromHitGround);
            ExecuteEffects(_moduleData.FXHitGround, _moduleData.OCLHitGround);
        }
        else
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                MathUtility.ToRadians(_moduleData.PitchRate) / GameEngine.LogicFramesPerSecond)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX,
                    MathUtility.ToRadians(_rollRate) / GameEngine.LogicFramesPerSecond);
            GameObject.UpdateTransform(position, Quaternion.Normalize(GameObject.Rotation * rotation));
            _rollRate += _rollRate * _moduleData.RollRateDelta / GameEngine.LogicFramesPerSecond;
        }

        return UpdateSleepTime.None;
    }

    private LogicFrameSpan Delay(int milliseconds) =>
        new((uint)MathF.Ceiling(milliseconds * GameEngine.LogicFramesPerSecond / 1000f));

    private void ExecuteEffects(string fxName, string oclName)
    {
        if (!string.IsNullOrEmpty(fxName))
        {
            GameEngine.AssetStore.FXLists.GetByName(fxName)?.Execute(
                new FXListExecutionContext(GameObject.Rotation, GameObject.Translation, GameEngine));
        }

        if (!string.IsNullOrEmpty(oclName)
            && GameEngine.AssetStore.ObjectCreationLists.GetByName(oclName) is { } ocl)
        {
            GameEngine.ObjectCreationLists.Create(ocl, GameObject, GameEngine);
        }
    }
}
