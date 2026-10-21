using System.Numerics;
using ImGuiNET;

namespace Panopticon.Logic.Object;

public sealed class Weapon : IPersistableObject
{
    private readonly ModelConditionFlag _usingFlag;

    private readonly WeaponStateMachine _stateMachine;

    private int _currentRounds;

    private uint _unknownInt1;
    private uint _unknownInt2;
    private uint _unknownInt3;
    private uint _unknownFrame1;
    private uint _unknownFrame2;
    private uint _unknownFrame3;
    private uint _unknownFrame4;
    private ObjectId _unknownObjectId;
    private uint _unknownInt4;
    private uint _unknownInt5;
    private uint _unknownInt6;
    private bool _unknownBool1;
    private bool _unknownBool2;

    public readonly int WeaponIndex;

    public readonly GameObject ParentGameObject;

    public readonly WeaponTemplate Template;

    public int CurrentRounds
    {
        get => _currentRounds;
        internal set => _currentRounds = value;
    }

    internal WeaponTarget CurrentTarget { get; private set; }

    public bool HasValidTarget => IsTargetInRange(CurrentTarget);

    internal bool IsTargetInRange(WeaponTarget target)
    {
        if (target == null || target.IsDestroyed)
        {
            return false;
        }

        var targetPosition = target.TargetPosition;
        var targetRadius = target.TargetType == WeaponTargetType.Object
            ? target.GetTargetObject()?.RoughCollider?.WorldBounds.Radius ?? 0f
            : 0f;
        return IsPositionInRange(targetPosition, targetRadius);
    }

    internal bool IsObjectInRange(GameObject target) => !target.IsEffectivelyDead
        && IsPositionInRange(target.Translation, target.RoughCollider?.WorldBounds.Radius ?? 0f);

    private bool IsPositionInRange(Vector3 targetPosition, float targetRadius)
    {
        var distance = Vector3.Distance(targetPosition, ParentGameObject.Translation);

        // Imported weapons whose source authors ranges on the ground plane (WI-M3-01)
        // measure the horizontal component: altitude must not consume the authored range.
        if (Template.MeasuresRangeOnGround)
        {
            distance = Vector2.Distance(
                new Vector2(targetPosition.X, targetPosition.Y),
                new Vector2(ParentGameObject.Translation.X, ParentGameObject.Translation.Y));
        }

        // Attack range is measured from the target's center, but large objects (e.g. buildings)
        // have their own footprint radius. Without subtracting it, a vehicle can be blocked by
        // the target's collision at its edge while still being reported "out of range" because
        // its own position is farther from the target's center than a short-ranged weapon allows.
        var effectiveDistance = distance - targetRadius;
        return effectiveDistance <= Template.AttackRange
            // Artillery-style launchers cannot depress onto nearby targets: inside the
            // minimum attack range the weapon holds fire (the fire cycle stalls in Inactive,
            // matching vanilla's dead zone).
            && effectiveDistance >= Template.MinimumAttackRange;
    }

    public bool IsInactive => _stateMachine.IsInactive;

    public readonly WeaponSlot Slot;

    internal Weapon(
        GameObject gameObject,
        WeaponTemplate weaponTemplate,
        WeaponSlot slot,
        IGameEngine gameEngine)
    {
        ParentGameObject = gameObject;
        Template = weaponTemplate;

        WeaponIndex = (int)slot;

        Slot = slot;

        FillClip();

        _stateMachine = new WeaponStateMachine(
            new WeaponStateContext(
                gameObject,
                this,
                gameEngine));

        _usingFlag = ModelConditionFlagUtility.GetUsingWeaponFlag(WeaponIndex);
    }

    public bool UsesClip => Template.ClipSize > 0;

    public bool IsClipEmpty() => UsesClip && CurrentRounds == 0;

    public void FillClip()
    {
        CurrentRounds = Template.ClipSize;
    }

    internal void SetTarget(WeaponTarget target)
    {
        if (CurrentTarget == target)
        {
            return;
        }

        if (CurrentTarget != null)
        {
            ParentGameObject.ModelConditionFlags.Set(_usingFlag, false);
        }

        CurrentTarget = target;

        if (CurrentTarget != null)
        {
            ParentGameObject.ModelConditionFlags.Set(_usingFlag, true);
            // Enable weapon logic ticking. AI attack states are not fully implemented,
            // so we gate on having a target rather than waiting for the AI to set Attacking.
            ParentGameObject.ModelConditionFlags.Set(ModelConditionFlag.Attacking, true);
        }
        else
        {
            ParentGameObject.ModelConditionFlags.Set(ModelConditionFlag.Attacking, false);

            // Clearing Attacking stops GameObject.LogicTick from calling LogicTick (and hence
            // Update) on this weapon, so a state machine caught mid fire-cycle would otherwise
            // never run OnExitState, leaving flags like FiringA stuck on forever (e.g. a
            // persisting muzzle flash). Force it back to Inactive so those flags always clear.
            _stateMachine.ForceInactive();
        }
    }

    public void LogicTick()
    {
        _stateMachine.Update();

        if (CurrentTarget != null && CurrentTarget.IsDestroyed)
        {
            SetTarget(null);
        }
    }

    public void Fire()
    {
        _stateMachine.Fire();
    }

    public void Persist(StatePersister reader)
    {
        reader.PersistVersion(3);

        var templateName = Template.Name;
        reader.PersistAsciiString(ref templateName);
        if (templateName != Template.Name)
        {
            throw new InvalidStateException();
        }

        reader.PersistUInt32(ref _unknownInt1);
        reader.PersistUInt32(ref _unknownInt2);
        reader.PersistUInt32(ref _unknownInt3);
        reader.PersistFrame(ref _unknownFrame1);

        reader.SkipUnknownBytes(4);

        reader.PersistFrame(ref _unknownFrame2);
        reader.PersistFrame(ref _unknownFrame3);
        reader.PersistFrame(ref _unknownFrame4);
        reader.PersistObjectId(ref _unknownObjectId);

        reader.SkipUnknownBytes(4);

        reader.PersistUInt32(ref _unknownInt4);
        reader.PersistUInt32(ref _unknownInt5);
        reader.PersistUInt32(ref _unknownInt6);

        reader.SkipUnknownBytes(2);

        reader.PersistBoolean(ref _unknownBool1);
        reader.PersistBoolean(ref _unknownBool2);
    }

    internal void DrawInspector()
    {
        // TODO: Weapon template

        ImGui.InputInt("CurrentRounds", ref _currentRounds);

        ImGui.LabelText("CurrentTarget", CurrentTarget?.TargetType.ToString() ?? "[none]");

        if (CurrentTarget != null && CurrentTarget.TargetType == WeaponTargetType.Position)
        {
            var currentTargetPosition = CurrentTarget.TargetPosition;
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.6f);
            ImGui.InputFloat3("CurrentTargetPosition", ref currentTargetPosition);
            ImGui.PopStyleVar();
        }
    }
}
